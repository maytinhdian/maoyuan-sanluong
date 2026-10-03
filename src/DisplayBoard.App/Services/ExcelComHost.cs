using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using DisplayBoard.Core.Entry;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.App.Services;

/// <summary>
/// Ghi file nhập liệu qua chính Microsoft Excel trên máy chủ (COM), để công thức tự tính lại và TV thấy số mới ngay.
/// File đang mở trong Excel thì ghi thẳng vào cửa sổ đó rồi lưu; chưa mở thì mở ngầm một Excel riêng, ghi, lưu, đóng.
/// Mọi lệnh COM chạy trên một thread STA riêng, lần lượt từng phiếu.
/// </summary>
public sealed class ExcelComHost : IWorkbookHost, IDisposable
{
    private const int XlCalculationManual = -4135;

    // Excel từ chối lệnh khi có người đang sửa ô hoặc đang mở hộp thoại.
    private static readonly HashSet<int> BusyCodes =
    [
        unchecked((int)0x80010001), // RPC_E_CALL_REJECTED
        unchecked((int)0x8001010A), // RPC_E_SERVERCALL_RETRYLATER
        unchecked((int)0x800AC472), // VBA_E_IGNORE: Excel đang ở chế độ sửa ô
    ];

    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;
    private readonly ILogger<ExcelComHost> _logger;
    private readonly Lazy<bool> _installed = new(() => Type.GetTypeFromProgID("Excel.Application") is not null);

    public ExcelComHost(ILogger<ExcelComHost> logger)
    {
        _logger = logger;
        _thread = new Thread(Run) { IsBackground = true, Name = "Excel COM" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public string? UnavailableReason => _installed.Value ? null : "Máy chủ chưa cài Microsoft Excel nên không ghi được dữ liệu nhập từ trình duyệt.";

    public Task RunAsync(string path, Action<IEntryWorkbook> action, CancellationToken ct = default)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(() =>
        {
            if (ct.IsCancellationRequested)
            {
                done.TrySetCanceled(ct);
                return;
            }
            try
            {
                Execute(Path.GetFullPath(path), action);
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }, ct);
        return done.Task;
    }

    private void Run()
    {
        foreach (var work in _work.GetConsumingEnumerable())
            work();
    }

    private void Execute(string path, Action<IEntryWorkbook> action)
    {
        dynamic? excel = null;
        dynamic? workbook = null;
        var ownsExcel = false;
        try
        {
            workbook = FindOpenWorkbook(path);
            if (workbook is null)
            {
                if (IsLocked(path))
                    throw new WorkbookBusyException("File Excel đang được mở ở máy khác hoặc chương trình khác.");
                var type = Type.GetTypeFromProgID("Excel.Application")
                    ?? throw new EntryException(UnavailableReason ?? "Không mở được Excel.");
                excel = Activator.CreateInstance(type)!;
                ownsExcel = true;
                excel.Visible = false;
                excel.DisplayAlerts = false;
                excel.ScreenUpdating = false;
                excel.AskToUpdateLinks = false;
                workbook = excel.Workbooks.Open(path, 0, false);
                _logger.LogInformation("Mở ngầm Excel để ghi {Path}", path);
            }
            if ((bool)workbook!.ReadOnly)
                throw new WorkbookBusyException("File Excel trên máy chủ đang mở ở chế độ chỉ đọc. Hãy mở lại file bình thường.");

            action(new ComWorkbook(workbook));

            var application = workbook.Application;
            if ((int)application.Calculation == XlCalculationManual)
                application.Calculate();
            workbook.Save();
        }
        catch (COMException ex) when (BusyCodes.Contains(ex.HResult))
        {
            throw new WorkbookBusyException("Excel trên máy chủ đang bận (có người đang sửa ô hoặc đang mở hộp thoại). Bấm Enter hoặc Esc trong Excel để ghi tiếp.", ex);
        }
        catch (COMException ex)
        {
            _logger.LogError(ex, "Lỗi COM khi ghi Excel {Path}", path);
            throw new EntryException($"Excel báo lỗi khi ghi (0x{ex.HResult:X8}): {ex.Message}");
        }
        finally
        {
            if (ownsExcel)
            {
                try { workbook?.Close(false); } catch (COMException) { }
                try { excel?.Quit(); } catch (COMException) { }
            }
            Release(workbook);
            Release(excel);
            workbook = null;
            excel = null;
            // Giải phóng các đối tượng COM trung gian để Excel chạy ngầm thoát hẳn.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
            Marshal.FinalReleaseComObject(com);
    }

    /// <summary>File đang mở trong Excel (bất kỳ cửa sổ Excel nào của người dùng này): tìm trong Running Object Table.</summary>
    private static object? FindOpenWorkbook(string path)
    {
        if (GetRunningObjectTable(0, out var table) != 0 || table is null)
            return null;
        if (CreateBindCtx(0, out var context) != 0)
            return null;
        table.EnumRunning(out var monikers);
        var one = new IMoniker[1];
        try
        {
            while (monikers.Next(1, one, IntPtr.Zero) == 0)
            {
                var moniker = one[0];
                try
                {
                    moniker.GetDisplayName(context, null, out var name);
                    if (string.Equals(name, path, StringComparison.OrdinalIgnoreCase))
                    {
                        table.GetObject(moniker, out var workbook);
                        return workbook;
                    }
                }
                catch (COMException)
                {
                }
                finally
                {
                    Marshal.ReleaseComObject(moniker);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(monikers);
            Marshal.ReleaseComObject(context);
            Marshal.ReleaseComObject(table);
        }
        return null;
    }

    private static bool IsLocked(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            throw new EntryException("Không có quyền ghi file Excel (file chỉ đọc hoặc thư mục bị khoá).");
        }
    }

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable? table);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx context);

    public void Dispose() => _work.CompleteAdding();

    private sealed class ComWorkbook(dynamic workbook) : IEntryWorkbook
    {
        public IEntrySheet? Sheet(string name)
        {
            int count = workbook.Worksheets.Count;
            for (var i = 1; i <= count; i++)
            {
                var sheet = workbook.Worksheets[i];
                if (Normalize((string)sheet.Name) == Normalize(name))
                    return new ComSheet(sheet);
            }
            return null;
        }

        private static string Normalize(string name) => name.Trim().ToUpperInvariant();
    }

    private sealed class ComSheet(dynamic sheet) : IEntrySheet
    {
        public string Name => sheet.Name;

        public int LastRow
        {
            get
            {
                var used = sheet.UsedRange;
                return (int)used.Row + (int)used.Rows.Count - 1;
            }
        }

        public int LastColumn
        {
            get
            {
                var used = sheet.UsedRange;
                return (int)used.Column + (int)used.Columns.Count - 1;
            }
        }

        public object? Get(int row, int column)
        {
            object? value = sheet.Cells[row, column].Value2;
            // Ô lỗi (#N/A...) trả về số nguyên mã lỗi.
            return value is int ? null : value;
        }

        public void Set(int row, int column, object? value)
        {
            var cell = sheet.Cells[row, column];
            switch (value)
            {
                case null: cell.ClearContents(); break;
                case DateTime date: cell.Value2 = date.ToOADate(); break;
                case TimeSpan time: cell.Value2 = time.TotalDays; break;
                case decimal number: cell.Value2 = (double)number; break;
                default: cell.Value2 = value; break;
            }
        }

        public (int First, int Last)? TableBody()
        {
            if ((int)sheet.ListObjects.Count == 0)
                return null;
            var body = sheet.ListObjects[1].DataBodyRange;
            if (body is null)
                return null;
            return ((int)body.Row, (int)body.Row + (int)body.Rows.Count - 1);
        }

        public int AppendTableRow()
        {
            if ((int)sheet.ListObjects.Count == 0)
                return LastRow + 1;
            var row = sheet.ListObjects[1].ListRows.Add();
            return (int)row.Range.Row;
        }

        public void FillFormulasFromAbove(int row)
        {
            var last = LastColumn;
            for (var col = 1; col <= last; col++)
            {
                var cell = sheet.Cells[row, col];
                var above = sheet.Cells[row - 1, col];
                if ((bool)above.HasFormula && !(bool)cell.HasFormula && cell.Value2 is null)
                    cell.FormulaR1C1 = above.FormulaR1C1;
            }
        }
    }
}
