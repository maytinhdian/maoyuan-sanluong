using ClosedXML.Excel;
using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Excel;

/// <summary>
/// Đọc sheet HANG_LOI (V20): mỗi dòng một lần ghi hàng lỗi kèm tên file ảnh. Chỉ lấy các dòng Excel đánh dấu
/// HIỆN TRÊN TV = "CÓ" (ngày đang hiển thị), app không tự lọc theo ngày. File V19 trở về trước không có sheet này.
/// </summary>
public static class DefectLogReader
{
    public const string SheetName = "HANG_LOI";
    private const int HeaderSearchRows = 8;

    private enum Column { Date, Time, Line, Product, DefectType, Quantity, ImageFile, Note, ShowOnTv }

    // Tiêu đề tiếng Việt ở dòng 3, so khớp phần đầu sau khi bỏ dấu/khoảng trắng ("MÃ SẢN PHẨM (tự lấy)").
    private static readonly (Column Column, string Header)[] Headers =
    [
        (Column.Date, "NGÀY"),
        (Column.Time, "GIỜ"),
        (Column.Line, "CHUYỀN"),
        (Column.Product, "MÃ SẢN PHẨM"),
        (Column.DefectType, "LOẠI LỖI"),
        (Column.Quantity, "SỐ LƯỢNG"),
        (Column.ImageFile, "TÊN FILE ẢNH"),
        (Column.Note, "GHI CHÚ"),
        (Column.ShowOnTv, "HIỆN TRÊN TV"),
    ];

    public static IReadOnlyList<DefectEntry> Read(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.FirstOrDefault(ws => SheetTable.NormalizeHeader(ws.Name) == SheetTable.NormalizeHeader(SheetName));
        if (sheet is null)
            return [];
        var found = FindHeader(sheet);
        if (found is null)
            return [];
        var (headerRow, columns) = found.Value;

        var entries = new List<DefectEntry>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            string? Text(Column c) => columns.TryGetValue(c, out var col) ? CellParser.GetText(sheet.Cell(row, col)) : null;
            var show = Text(Column.ShowOnTv);
            if (show is null || SheetTable.NormalizeHeader(show) != "co")
                continue;
            var line = Text(Column.Line);
            if (line is null)
                continue;
            entries.Add(new DefectEntry
            {
                Date = columns.TryGetValue(Column.Date, out var dc) && CellParser.TryGetDate(sheet.Cell(row, dc), out var d) ? d : null,
                Time = columns.TryGetValue(Column.Time, out var tc) ? ReadTime(sheet.Cell(row, tc)) : null,
                Line = line,
                ProductCode = Text(Column.Product),
                DefectType = Text(Column.DefectType),
                Quantity = columns.TryGetValue(Column.Quantity, out var qc) && CellParser.TryGetDecimal(sheet.Cell(row, qc), out var q) ? q : null,
                ImageFile = Text(Column.ImageFile),
                Note = Text(Column.Note),
            });
        }
        return entries;
    }

    private static TimeOnly? ReadTime(IXLCell cell)
    {
        var raw = CellParser.Raw(cell);
        if (raw.IsTimeSpan)
        {
            var span = raw.GetTimeSpan();
            return span >= TimeSpan.Zero && span < TimeSpan.FromDays(1) ? TimeOnly.FromTimeSpan(span) : null;
        }
        if (raw.IsDateTime)
            return TimeOnly.FromDateTime(raw.GetDateTime());
        if (raw.IsNumber)
        {
            var fraction = raw.GetNumber() % 1;
            return fraction >= 0 ? TimeOnly.FromTimeSpan(TimeSpan.FromDays(fraction)) : null;
        }
        return raw.IsText && TimeOnly.TryParse(raw.GetText(), out var t) ? t : null;
    }

    private static (int Row, Dictionary<Column, int> Columns)? FindHeader(IXLWorksheet sheet)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var row = 1; row <= HeaderSearchRows; row++)
        {
            var columns = new Dictionary<Column, int>();
            for (var col = 1; col <= lastColumn; col++)
            {
                var text = CellParser.GetText(sheet.Cell(row, col));
                if (text is null)
                    continue;
                var key = SheetTable.NormalizeHeader(text);
                // Khớp tiêu đề dài nhất trước để "GIỜ" không ăn nhầm cột khác.
                foreach (var (column, header) in Headers.OrderByDescending(h => h.Header.Length))
                {
                    var h = SheetTable.NormalizeHeader(header);
                    if (key == h || (key.StartsWith(h, StringComparison.Ordinal) && column is Column.Product))
                    {
                        columns.TryAdd(column, col);
                        break;
                    }
                }
            }
            if (columns.ContainsKey(Column.Line) && columns.ContainsKey(Column.ShowOnTv))
                return (row, columns);
        }
        return null;
    }
}
