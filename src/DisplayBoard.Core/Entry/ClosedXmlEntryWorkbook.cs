using ClosedXML.Excel;
using DisplayBoard.Core.Excel;

namespace DisplayBoard.Core.Entry;

/// <summary>
/// Mở file bằng ClosedXML. Ô công thức trả về giá trị Excel đã lưu, không tự tính lại. Dùng để đọc danh sách và tình hình
/// trong ngày cho trang nhập liệu, và để test. Không dùng để ghi file thật vì ClosedXML không tính lại công thức và làm mất biểu đồ.
/// </summary>
public sealed class ClosedXmlEntryWorkbook(XLWorkbook workbook) : IEntryWorkbook
{
    public XLWorkbook Workbook { get; } = workbook;

    /// <summary>Đọc file kể cả khi Excel đang mở (FileShare.ReadWrite), chép vào bộ nhớ để không giữ file.</summary>
    public static async Task<ClosedXmlEntryWorkbook> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var buffer = new MemoryStream();
        await using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true))
            await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
        buffer.Position = 0;
        return await Task.Run(() => new ClosedXmlEntryWorkbook(new XLWorkbook(buffer)), ct).ConfigureAwait(false);
    }

    public IEntrySheet? Sheet(string name)
    {
        var sheet = Workbook.Worksheets.FirstOrDefault(ws => SheetTable.NormalizeHeader(ws.Name) == SheetTable.NormalizeHeader(name));
        return sheet is null ? null : new SheetAdapter(sheet);
    }

    private sealed class SheetAdapter(IXLWorksheet sheet) : IEntrySheet
    {
        public string Name => sheet.Name;
        public int LastRow => Math.Max(sheet.LastRowUsed()?.RowNumber() ?? 0, Table?.RangeAddress.LastAddress.RowNumber ?? 0);
        public int LastColumn => Math.Max(sheet.LastColumnUsed()?.ColumnNumber() ?? 0, Table?.RangeAddress.LastAddress.ColumnNumber ?? 0);

        private IXLTable? Table => sheet.Tables.FirstOrDefault();

        public object? Get(int row, int column)
        {
            var value = CellParser.Raw(sheet.Cell(row, column));
            if (value.IsBlank || value.IsError)
                return null;
            if (value.IsNumber)
                return value.GetNumber();
            if (value.IsText)
                return value.GetText().Length == 0 ? null : value.GetText();
            if (value.IsBoolean)
                return value.GetBoolean();
            if (value.IsDateTime)
                return value.GetDateTime();
            if (value.IsTimeSpan)
                return value.GetTimeSpan().TotalDays;
            return null;
        }

        public void Set(int row, int column, object? value)
        {
            var cell = sheet.Cell(row, column);
            switch (value)
            {
                case null: cell.Clear(XLClearOptions.Contents); break;
                case string s: cell.SetValue(s); break;
                case DateTime d: cell.SetValue(d); break;
                case TimeSpan t: cell.SetValue(t); break;
                case double n: cell.SetValue(n); break;
                case decimal m: cell.SetValue((double)m); break;
                case int i: cell.SetValue(i); break;
                default: cell.SetValue(value.ToString()); break;
            }
        }

        public (int First, int Last)? TableBody()
        {
            var table = Table;
            if (table is null)
                return null;
            var first = table.RangeAddress.FirstAddress.RowNumber + (table.ShowHeaderRow ? 1 : 0);
            var last = table.RangeAddress.LastAddress.RowNumber - (table.ShowTotalsRow ? 1 : 0);
            return last >= first ? (first, last) : null;
        }

        /// <summary>Nới Table thêm một dòng và chép công thức của dòng trên xuống (giống Excel điền cột công thức).</summary>
        public int AppendTableRow()
        {
            var table = Table;
            if (table is null)
                return LastRow + 1;
            var range = table.RangeAddress;
            var newRow = range.LastAddress.RowNumber + 1;
            table.Resize(sheet.Range(range.FirstAddress.RowNumber, range.FirstAddress.ColumnNumber, newRow, range.LastAddress.ColumnNumber));
            if (newRow - 1 > range.FirstAddress.RowNumber)
            {
                for (var col = range.FirstAddress.ColumnNumber; col <= range.LastAddress.ColumnNumber; col++)
                {
                    var above = sheet.Cell(newRow - 1, col);
                    if (above.HasFormula)
                        sheet.Cell(newRow, col).FormulaR1C1 = above.FormulaR1C1;
                    sheet.Cell(newRow, col).Style = above.Style;
                }
            }
            return newRow;
        }
    }
}
