using DisplayBoard.Core.Data;

namespace DisplayBoard.Tests.Data;

public sealed class DisplayCalculatorTests
{
    [Theory]
    [InlineData("IF(A5=\"\",\"\",ROUND(((E5-D5)+(G5-F5))*24,2))", 3, "IF(A8=\"\",\"\",ROUND(((E8-D8)+(G8-F8))*24,2))")]
    [InlineData("SUMIFS($W$5:$W$10004,$B$5:$B$10004,B5,$A$5:$A$10004,AA5)", 1, "SUMIFS($W$5:$W$10004,$B$5:$B$10004,B6,$A$5:$A$10004,AA6)")]
    [InlineData("IF(COUNT(H5:S5)=0,\"\",SUM(H5:S5))", 2, "IF(COUNT(H7:S7)=0,\"\",SUM(H7:S7))")]
    [InlineData("IF($AJ5=\"\",\"\",INDEX(NHAP_LIEU!$C$5:$C$10004,$AJ5))", 1, "IF($AJ6=\"\",\"\",INDEX(NHAP_LIEU!$C$5:$C$10004,$AJ6))")]
    [InlineData("IF(A5=\"A5 giữ nguyên\",\"\",LOG10(A5))", 1, "IF(A6=\"A5 giữ nguyên\",\"\",LOG10(A6))")]
    public void Formula_rows_shift_like_excel_fill_down(string formula, int delta, string expected) =>
        Assert.Equal(expected, ExcelExporter.ShiftFormula(formula, delta));

    [Fact]
    public void Working_days_skip_sundays_and_follow_calendar()
    {
        CalendarDay[] calendar =
        [
            new(new(2026, 10, 10), CalendarKinds.Off),     // T7 nghỉ
            new(new(2026, 10, 18), CalendarKinds.Extra),   // CN làm bù
            new(new(2026, 10, 25), CalendarKinds.Off),     // CN ghi nghỉ: vốn đã nghỉ
            new(new(2026, 10, 13), CalendarKinds.Extra),   // T3 ghi làm bù: vốn đã làm
        ];
        Assert.Equal(27, DisplayCalculator.WorkingDays([], new(2026, 10, 1), new(2026, 10, 31)));
        Assert.Equal(27, DisplayCalculator.WorkingDays(calendar, new(2026, 10, 1), new(2026, 10, 31)));
        Assert.Equal(17, DisplayCalculator.WorkingDays(calendar, new(2026, 10, 14), new(2026, 10, 31)));
        Assert.Equal(0, DisplayCalculator.WorkingDays(calendar, new(2026, 10, 25), new(2026, 10, 25)));
    }

    [Fact]
    public void Shows_latest_day_unless_a_day_is_chosen()
    {
        var data = SampleProduction.Create();
        Assert.Equal(SampleProduction.LastDay, DisplayCalculator.Compute(data).Date);
        Assert.Equal(new DateOnly(2026, 9, 30), DisplayCalculator.Compute(data with { DisplayDateOverride = new(2026, 9, 30) }).Date);
        Assert.Null(DisplayCalculator.Compute(ProductionData.Empty).Date);
    }

    [Fact]
    public void Inactive_lines_are_not_shown()
    {
        var sheet = DisplayCalculator.Compute(SampleProduction.Create());
        Assert.DoesNotContain(sheet.Lines, l => l.Line == "Chuyền 7");
        Assert.Contains(sheet.DefectLog, d => d.Line == "Chuyền 7" && d.ProductCode is null);
    }
}

public sealed class V20SampleImportTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>File mẫu V20 do Excel/LibreOffice tính sẵn: nhập vào rồi tính lại phải ra đúng số đang lưu trong file.</summary>
    [Fact]
    public void Sample_v20_workbook_imports_with_matching_numbers()
    {
        var path = Path.Combine(TestPaths.RepoRoot(), "samples", "Theo_doi_san_luong_V20_mau.xlsx");
        var imported = V20Importer.Read(path);
        foreach (var w in imported.Warnings)
            output.WriteLine(w);
        Assert.NotEmpty(imported.Data.Entries);
        Assert.Equal(6, imported.Data.Lines.Count);

        using var workbook = new ClosedXML.Excel.XLWorkbook(path);
        var check = V20Importer.Check(workbook, imported.Data);
        foreach (var row in check.Rows.Where(r => !r.Matches))
            output.WriteLine(row.ToString());
        foreach (var d in check.DisplayDifferences)
            output.WriteLine(d);
        Assert.True(check.HasExcelValues);
        Assert.Equal(0, check.Mismatches);
    }
}
