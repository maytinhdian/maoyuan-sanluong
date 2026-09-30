using ClosedXML.Excel;
using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Excel;

/// <summary>Đọc display-content.xlsx: THONG_BAO, KHAU_HIEU, SAN_PHAM, CAU_HINH. Mọi sheet đều tùy chọn.</summary>
public static class ContentWorkbookReader
{
    public static ContentData Read(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var warnings = new List<string>();
        return new ContentData(
            ReadNotices(workbook, warnings),
            ReadSlogans(workbook),
            ReadProducts(workbook),
            ReadSettings(workbook),
            warnings);
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
            var from = OptionalDate(table, row, "Từ ngày", warnings);
            var to = OptionalDate(table, row, "Đến ngày", warnings);
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

    private static DateOnly? OptionalDate(SheetTable table, int row, string header, List<string> warnings)
    {
        var cell = table.Cell(row, header);
        if (cell is null || CellParser.IsBlank(cell))
            return null;
        if (CellParser.TryGetDate(cell, out var date))
            return date;
        warnings.Add($"THONG_BAO dòng {row}: {header} không hợp lệ.");
        return null;
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

    private static List<ProductInfo> ReadProducts(XLWorkbook workbook)
    {
        var table = SheetTable.TryOpen(workbook, "SAN_PHAM");
        if (table is null || !table.Has("Mã sản phẩm"))
            return [];
        var result = new List<ProductInfo>();
        foreach (var row in table.DataRows())
        {
            var code = table.Text(row, "Mã sản phẩm");
            if (code is null)
                continue;
            int? order = null;
            var orderCell = table.Cell(row, "Thứ tự");
            if (orderCell is not null && CellParser.TryGetDecimal(orderCell, out var o))
                order = (int)o;
            result.Add(new ProductInfo(code, table.Text(row, "Tên hiển thị"), table.Text(row, "Màu"), table.Text(row, "Ảnh"), order));
        }
        return result;
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
            var value = table.Text(row, "Giá trị");
            if (key is not null && value is not null)
                result[SheetTable.NormalizeHeader(key)] = value;
        }
        return result;
    }
}
