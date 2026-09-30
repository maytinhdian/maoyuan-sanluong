using ClosedXML.Excel;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Excel;

/// <summary>Đọc workbook bằng ClosedXML. Mở file với FileShare.ReadWrite để đọc được khi Excel đang mở file.</summary>
public sealed class ExcelWorkbookReader : IExcelDataReader
{
    public const string DataSheet = "DATA";
    public static readonly string[] RequiredColumns = ["Ngày", "Mã NV", "Họ tên", "Bộ phận", "Sản lượng"];

    public async Task<WorkbookData> ReadAsync(string filePath, CancellationToken ct)
    {
        // Copy ra bộ nhớ trước để giữ file trong thời gian ngắn nhất.
        using var buffer = new MemoryStream();
        await using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true))
        {
            await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
        }
        buffer.Position = 0;
        ct.ThrowIfCancellationRequested();
        return await Task.Run(() => Read(buffer), ct).ConfigureAwait(false);
    }

    public static WorkbookData Read(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var warnings = new List<string>();

        var records = ReadData(workbook, warnings);
        return new WorkbookData(
            records,
            ReadEmployees(workbook),
            ReadDepartments(workbook, warnings),
            ReadNotices(workbook, warnings),
            ReadSlogans(workbook),
            ReadSettings(workbook),
            warnings);
    }

    private static List<ProductionRecord> ReadData(XLWorkbook workbook, List<string> warnings)
    {
        var table = SheetTable.TryOpen(workbook, DataSheet)
            ?? throw new ExcelValidationException($"Không tìm thấy sheet \"{DataSheet}\" trong file Excel.");

        var missing = RequiredColumns.Where(c => !table.Has(c)).ToList();
        if (missing.Count > 0)
            throw new ExcelValidationException($"Sheet {DataSheet} thiếu cột bắt buộc: {string.Join(", ", missing)}.");

        var records = new List<ProductionRecord>();
        foreach (var row in table.DataRows())
        {
            var errors = new List<string>();

            if (!CellParser.TryGetDate(table.Cell(row, "Ngày")!, out var date))
                errors.Add("Ngày không hợp lệ");
            var code = table.Text(row, "Mã NV");
            if (code is null)
                errors.Add("thiếu Mã NV");
            var name = table.Text(row, "Họ tên");
            if (name is null)
                errors.Add("thiếu Họ tên");
            var department = table.Text(row, "Bộ phận");
            if (department is null)
                errors.Add("thiếu Bộ phận");
            if (!CellParser.TryGetDecimal(table.Cell(row, "Sản lượng")!, out var quantity))
                errors.Add("Sản lượng không phải số");

            decimal? target = null;
            var targetCell = table.Cell(row, "Mục tiêu");
            if (targetCell is not null && !CellParser.IsBlank(targetCell))
            {
                if (CellParser.TryGetDecimal(targetCell, out var t))
                    target = t;
                else
                    warnings.Add($"DATA dòng {row}: Mục tiêu không phải số, bỏ qua mục tiêu.");
            }

            TimeOnly? time = null;
            var timeCell = table.Cell(row, "Giờ");
            if (timeCell is not null && !CellParser.IsBlank(timeCell))
            {
                if (CellParser.TryGetTime(timeCell, out var t))
                    time = t;
                else
                    warnings.Add($"DATA dòng {row}: Giờ sai định dạng, dòng này không vẽ trên biểu đồ xu hướng.");
            }

            if (errors.Count > 0)
            {
                warnings.Add($"DATA dòng {row}: {string.Join(", ", errors)}. Đã bỏ qua dòng này.");
                continue;
            }

            records.Add(new ProductionRecord(
                row, date, time, table.Text(row, "Ca"),
                code!, name!, department!, quantity, target, table.Text(row, "Ghi chú")));
        }
        return records;
    }

    private static List<EmployeeInfo> ReadEmployees(XLWorkbook workbook)
    {
        var table = SheetTable.TryOpen(workbook, "DANH_MUC");
        if (table is null || !table.Has("Mã NV"))
            return [];
        return table.DataRows()
            .Select(row => (row, code: table.Text(row, "Mã NV")))
            .Where(x => x.code is not null)
            .Select(x => new EmployeeInfo(
                x.code!,
                table.Text(x.row, "Họ tên"),
                table.Text(x.row, "Bộ phận"),
                table.Text(x.row, "Trạng thái"),
                table.Text(x.row, "Ảnh")))
            .ToList();
    }

    private static List<DepartmentInfo> ReadDepartments(XLWorkbook workbook, List<string> warnings)
    {
        var table = SheetTable.TryOpen(workbook, "BO_PHAN");
        if (table is null || !table.Has("Bộ phận"))
            return [];
        var result = new List<DepartmentInfo>();
        foreach (var row in table.DataRows())
        {
            var name = table.Text(row, "Bộ phận");
            if (name is null)
                continue;
            decimal? target = null;
            var targetCell = table.Cell(row, "Mục tiêu");
            if (targetCell is not null && !CellParser.IsBlank(targetCell))
            {
                if (CellParser.TryGetDecimal(targetCell, out var t))
                    target = t;
                else
                    warnings.Add($"BO_PHAN dòng {row}: Mục tiêu không phải số.");
            }
            int? order = null;
            var orderCell = table.Cell(row, "Thứ tự");
            if (orderCell is not null && CellParser.TryGetDecimal(orderCell, out var o))
                order = (int)o;
            result.Add(new DepartmentInfo(name, target, table.Text(row, "Màu"), table.Text(row, "Icon"), order));
        }
        return result;
    }

    private static List<NoticeRow> ReadNotices(XLWorkbook workbook, List<string> warnings)
    {
        var table = SheetTable.TryOpen(workbook, "THONG_BAO");
        if (table is null || !table.Has("Nội dung"))
            return [];
        var result = new List<NoticeRow>();
        foreach (var row in table.DataRows())
        {
            var content = table.Text(row, "Nội dung");
            if (content is null)
                continue;
            DateOnly? from = null, to = null;
            var fromCell = table.Cell(row, "Từ ngày");
            if (fromCell is not null && !CellParser.IsBlank(fromCell))
            {
                if (CellParser.TryGetDate(fromCell, out var d)) from = d;
                else warnings.Add($"THONG_BAO dòng {row}: Từ ngày không hợp lệ.");
            }
            var toCell = table.Cell(row, "Đến ngày");
            if (toCell is not null && !CellParser.IsBlank(toCell))
            {
                if (CellParser.TryGetDate(toCell, out var d)) to = d;
                else warnings.Add($"THONG_BAO dòng {row}: Đến ngày không hợp lệ.");
            }
            var order = row;
            var orderCell = table.Cell(row, "Thứ tự");
            if (orderCell is not null && CellParser.TryGetDecimal(orderCell, out var o))
                order = (int)o;
            // Không có cột "Bật" thì coi như mọi dòng đều bật.
            var enabled = !table.Has("Bật") || CellParser.IsTruthy(table.Text(row, "Bật"));
            result.Add(new NoticeRow(table.Text(row, "Tiêu đề") ?? "THÔNG BÁO", content, table.Text(row, "Ảnh nền"), from, to, order, enabled));
        }
        return result;
    }

    private static List<Slogan> ReadSlogans(XLWorkbook workbook)
    {
        var table = SheetTable.TryOpen(workbook, "KHAU_HIEU");
        if (table is null || !table.Has("Dòng 1"))
            return [];
        return table.DataRows()
            .Select(row => (row, line1: table.Text(row, "Dòng 1")))
            .Where(x => x.line1 is not null)
            .Select(x => new Slogan(table.Text(x.row, "Icon") ?? "star", x.line1!, table.Text(x.row, "Dòng 2")))
            .Take(4)
            .ToList();
    }

    private static Dictionary<string, string> ReadSettings(XLWorkbook workbook)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var table = SheetTable.TryOpen(workbook, "CAU_HINH");
        if (table is null || !table.Has("Khóa") || !table.Has("Giá trị"))
            return result;
        foreach (var row in table.DataRows())
        {
            var key = table.Text(row, "Khóa");
            var valueCell = table.Cell(row, "Giá trị")!;
            if (key is null)
                continue;
            // Ô giờ trong Excel là số thập phân; đổi về "HH:mm" cho dễ dùng.
            string? value = !valueCell.Value.IsText && CellParser.TryGetTime(valueCell, out var t) && key.StartsWith("Gio", StringComparison.OrdinalIgnoreCase)
                ? t.ToString("HH:mm")
                : CellParser.GetText(valueCell);
            if (value is not null)
                result[SheetTable.NormalizeHeader(key)] = value;
        }
        return result;
    }
}
