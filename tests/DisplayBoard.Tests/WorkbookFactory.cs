using ClosedXML.Excel;

namespace DisplayBoard.Tests;

/// <summary>Tạo workbook trong bộ nhớ cho test.</summary>
internal static class WorkbookFactory
{
    public static MemoryStream Create(Action<XLWorkbook> build)
    {
        using var workbook = new XLWorkbook();
        build(workbook);
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    public static IXLWorksheet AddTable(this XLWorkbook workbook, string name, string[] headers, params object?[][] rows)
    {
        var sheet = workbook.AddWorksheet(name);
        for (var c = 0; c < headers.Length; c++)
            sheet.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                sheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
        return sheet;
    }

    public static readonly string[] DataHeaders = ["Ngày", "Giờ", "Ca", "Mã NV", "Họ tên", "Bộ phận", "Sản lượng", "Mục tiêu", "Ghi chú"];
}
