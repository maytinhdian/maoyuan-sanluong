using ClosedXML.Excel;
using DisplayBoard.Core.Excel;

namespace DisplayBoard.Tests;

public class ProductionWorkbookReaderTests
{
    private static readonly string[] Headers =
    [
        "产品代码\nMÃ SẢN PHẨM",
        "每日工时数\nTHỜI GIAN LÊN CA",
        "每小时目标产量PCS\nSẢN LƯỢNG MỤC TIÊU MỖI GIỜ",
        "每日目标产量PCS\nMỤC TIỆU TRONG NGÀY",
        "每日实际产量PCS\nSẢN LƯỢNG THỰC TẾ TRONG NGÀY",
        "当日达成率%\nTỶ LỆ ĐẠT TRONG NGÀY",
        "当月总目标产量PCS\nTỔNG SẢN LƯỢNG TRONG THÁNG",
        "当月累计产能PCS\n LŨY KẾ SẢN LƯỢNG TRONG THÁNG",
        "当月累计差异PCS\nLŨY KẾ CHÊNH LỆCH TRONG THÁNG",
        "当月总达成率\nTỔNG ĐẠT % TRONG THÁNG",
    ];

    /// <summary>Giống file thật của khách: A1 là ô chữ chứa ngày, header dòng 2, công thức ở D/F/I/J, 2 dòng trống cuối.</summary>
    private static void CustomerSheet(IXLWorksheet sheet, string dateText, params object[][] rows)
    {
        sheet.Range("A1:B1").Merge();
        sheet.Cell("A1").Value = dateText;
        for (var i = 0; i < Headers.Length; i++)
            sheet.Cell(2, i + 1).Value = Headers[i];
        var r = 3;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = XLCellValue.FromObject(row[0]);
            sheet.Cell(r, 2).Value = XLCellValue.FromObject(row[1]);
            sheet.Cell(r, 3).Value = XLCellValue.FromObject(row[2]);
            sheet.Cell(r, 4).FormulaA1 = $"B{r}*C{r}";
            sheet.Cell(r, 5).Value = XLCellValue.FromObject(row[3]);
            sheet.Cell(r, 6).FormulaA1 = $"+E{r}/D{r}";
            sheet.Cell(r, 7).Value = XLCellValue.FromObject(row[4]);
            sheet.Cell(r, 8).Value = XLCellValue.FromObject(row[5]);
            sheet.Cell(r, 9).FormulaA1 = $"+H{r}-G{r}";
            sheet.Cell(r, 10).FormulaA1 = $"+H{r}/G{r}";
            r++;
        }
        sheet.Range(2, 1, r + 1, 11).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    private static readonly object[][] CustomerRows =
    [
        ["ĐAI LƯNG", 10, 100, 1000, 40000, 38000],
        ["BAO TAY XANH", 10, 100, 1000, 40000, 39000],
        [883, 10, 185, 1850, 50000, 45000],
        [700, 10, 185, 1850, 50000, 45000],
        [972, 10, 185, 2000, 50000, 55000],
        [959, 10, 185, 1850, 50000, 45000],
    ];

    [Fact]
    public void Reads_customer_layout()
    {
        using var stream = WorkbookFactory.Create(wb => CustomerSheet(wb.AddWorksheet("Sheet2"), "日期：2026/09/30\nNGÀY THÁNG", CustomerRows));

        var sheet = ProductionWorkbookReader.Read(stream);

        Assert.Equal("Sheet2", sheet.SheetName);
        Assert.Equal(new DateOnly(2026, 9, 30), sheet.Date);
        Assert.Empty(sheet.Warnings);
        Assert.Equal(["ĐAI LƯNG", "BAO TAY XANH", "883", "700", "972", "959"], sheet.Records.Select(r => r.ProductCode));
        var p972 = sheet.Records[4];
        Assert.Equal(10m, p972.ShiftHours);
        Assert.Equal(185m, p972.HourlyTarget);
        Assert.Equal(1850m, p972.DailyTarget); // từ công thức B*C
        Assert.Equal(2000m, p972.DailyActual);
        Assert.Equal(50000m, p972.MonthTarget);
        Assert.Equal(55000m, p972.MonthCumulative);
    }

    [Fact]
    public void Columns_matched_by_header_not_position()
    {
        using var stream = WorkbookFactory.Create(wb =>
        {
            var ws = wb.AddWorksheet("X");
            ws.Cell("A3").Value = "Ngày 30/09/2026";
            // Thứ tự cột khác, chỉ có tiếng Việt, có sai chính tả "MỤC TIỆU"
            ws.Cell(5, 1).Value = "SẢN LƯỢNG THỰC TẾ TRONG NGÀY";
            ws.Cell(5, 2).Value = "MÃ SẢN PHẨM";
            ws.Cell(5, 3).Value = "MỤC TIỆU TRONG NGÀY";
            ws.Cell(5, 4).Value = "LŨY KẾ SẢN LƯỢNG TRONG THÁNG";
            ws.Cell(5, 5).Value = "TỔNG SẢN LƯỢNG TRONG THÁNG";
            ws.Cell(6, 1).Value = "1,250";
            ws.Cell(6, 2).Value = "A1";
            ws.Cell(6, 3).Value = 1000;
            ws.Cell(6, 4).Value = 9000;
            ws.Cell(6, 5).Value = 10000;
        });

        var sheet = ProductionWorkbookReader.Read(stream);

        var r = Assert.Single(sheet.Records);
        Assert.Equal("A1", r.ProductCode);
        Assert.Equal(1250m, r.DailyActual);
        Assert.Equal(1000m, r.DailyTarget);
        Assert.Equal(10000m, r.MonthTarget);
        Assert.Equal(9000m, r.MonthCumulative);
        Assert.Equal(new DateOnly(2026, 9, 30), sheet.Date);
    }

    [Fact]
    public void Picks_sheet_with_newest_date()
    {
        using var stream = WorkbookFactory.Create(wb =>
        {
            wb.AddWorksheet("Ghi chú").Cell("A1").Value = "không phải bảng sản lượng";
            CustomerSheet(wb.AddWorksheet("Ngay30"), "日期：2026/09/30", ["A", 10, 100, 900, 0, 0]);
            CustomerSheet(wb.AddWorksheet("Ngay29"), "日期：2026/09/29", ["A", 10, 100, 800, 0, 0]);
        });

        var sheet = ProductionWorkbookReader.Read(stream);

        Assert.Equal("Ngay30", sheet.SheetName);
        Assert.Equal(900m, sheet.Records[0].DailyActual);
    }

    [Fact]
    public void Configured_sheet_name_wins()
    {
        using var stream = WorkbookFactory.Create(wb =>
        {
            CustomerSheet(wb.AddWorksheet("Ngay30"), "日期：2026/09/30", ["A", 10, 100, 900, 0, 0]);
            CustomerSheet(wb.AddWorksheet("Ngay29"), "日期：2026/09/29", ["A", 10, 100, 800, 0, 0]);
        });

        Assert.Equal("Ngay29", ProductionWorkbookReader.Read(stream, "ngay29").SheetName);
    }

    [Fact]
    public void Same_date_sheets_are_read_as_lines()
    {
        using var stream = WorkbookFactory.Create(wb =>
        {
            CustomerSheet(wb.AddWorksheet("Chuyền 1"), "日期：2026/09/30", ["X", 10, 100, 1, 0, 0], ["Y", 10, 100, 5, 0, 0]);
            CustomerSheet(wb.AddWorksheet("Chuyền 2"), "日期：2026/09/30", ["X", 10, 100, 2, 0, 0]);
            // Sheet ngày cũ còn sót lại thì bỏ qua.
            CustomerSheet(wb.AddWorksheet("Cũ"), "日期：2026/09/29", ["X", 10, 100, 9, 0, 0]);
        });

        var sheet = ProductionWorkbookReader.Read(stream);

        Assert.Equal(["Chuyền 1", "Chuyền 2"], sheet.Lines);
        Assert.Equal("Chuyền 1, Chuyền 2", sheet.SheetName);
        Assert.Equal(new DateOnly(2026, 9, 30), sheet.Date);
        Assert.Equal([("Chuyền 1", "X", 1m), ("Chuyền 1", "Y", 5m), ("Chuyền 2", "X", 2m)],
            sheet.Records.Select(r => (r.Line, r.ProductCode, r.DailyActual)).ToList());
    }

    [Fact]
    public void Missing_date_warns()
    {
        using var stream = WorkbookFactory.Create(wb => CustomerSheet(wb.AddWorksheet("S"), "NGÀY THÁNG", ["A", 10, 100, 900, 0, 0]));

        var sheet = ProductionWorkbookReader.Read(stream);

        Assert.Null(sheet.Date);
        Assert.Contains(sheet.Warnings, w => w.Contains("ngày"));
    }

    [Fact]
    public void No_product_header_throws_clear_error()
    {
        using var stream = WorkbookFactory.Create(wb => wb.AddWorksheet("Sheet1").Cell("A1").Value = "abc");

        var ex = Assert.Throws<ExcelValidationException>(() => ProductionWorkbookReader.Read(stream));
        Assert.Contains("MÃ SẢN PHẨM", ex.Message);
    }

    [Fact]
    public void Missing_actual_column_is_listed()
    {
        using var stream = WorkbookFactory.Create(wb =>
        {
            var ws = wb.AddWorksheet("S");
            ws.Cell(1, 1).Value = "产品代码\nMÃ SẢN PHẨM";
            ws.Cell(1, 2).Value = "每日目标产量PCS\nMỤC TIỆU TRONG NGÀY";
        });

        var ex = Assert.Throws<ExcelValidationException>(() => ProductionWorkbookReader.Read(stream));
        Assert.Contains("THỰC TẾ", ex.Message);
    }

    [Fact]
    public void Row_without_actual_is_skipped_with_warning()
    {
        using var stream = WorkbookFactory.Create(wb => CustomerSheet(wb.AddWorksheet("S"), "日期：2026/09/30",
            ["A", 10, 100, 900, 0, 0],
            ["B", 10, 100, "", 0, 0]));

        var sheet = ProductionWorkbookReader.Read(stream);

        Assert.Single(sheet.Records);
        Assert.Contains(sheet.Warnings, w => w.Contains("dòng 4") && w.Contains("B"));
    }

    [Theory]
    [InlineData("日期：2026/09/30\nNGÀY THÁNG", 2026, 9, 30)]
    [InlineData("Ngày 5/1/2026", 2026, 1, 5)]
    [InlineData("2026-12-01", 2026, 12, 1)]
    [InlineData("2026年10月2日", 2026, 10, 2)]
    public void Parses_date_in_text(string text, int y, int m, int d)
    {
        Assert.True(ProductionWorkbookReader.TryParseDateInText(text, out var date));
        Assert.Equal(new DateOnly(y, m, d), date);
    }

    [Fact]
    public void Generated_sample_is_readable()
    {
        using var stream = File.OpenRead(Path.Combine(TestPaths.RepoRoot(), "samples", "SanLuong-khach-mau.xlsx"));

        var sheet = ProductionWorkbookReader.Read(stream);

        Assert.Equal(6, sheet.Lines.Count);
        Assert.Equal(24, sheet.Records.Count);
        Assert.Empty(sheet.Warnings);
    }
}
