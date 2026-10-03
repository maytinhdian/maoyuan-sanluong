using ClosedXML.Excel;
using DisplayBoard.Core.Entry;

namespace DisplayBoard.Tests;

public sealed class ProductionWorkbookTests
{
    private static readonly DateOnly SampleDay = new(2026, 9, 30);
    private static readonly DateOnly NextDay = new(2026, 10, 1);

    private static ClosedXmlEntryWorkbook Sample() =>
        new(new XLWorkbook(Path.Combine(TestPaths.RepoRoot(), "samples", "Theo_doi_san_luong_V20_mau.xlsx")));

    [Fact]
    public void Reads_lists_and_shift_slots()
    {
        var context = ProductionWorkbook.ReadContext(Sample(), SampleDay, new TimeOnly(13, 42), null, "chuyen 3");

        Assert.Contains("Chuyền 3", context.Lines);
        Assert.Contains(context.Products, p => p.Code == "883");
        Assert.Contains("Bung chỉ", context.DefectTypes);
        var shift = Assert.Single(context.Shifts, s => s.Code == "11H30");
        Assert.Equal(11.5m, shift.Hours);
        Assert.Equal(12, shift.Slots.Count);
        Assert.Equal(new TimeOnly(12, 30), shift.Slots[4].Start);
        Assert.Equal(new TimeOnly(20, 0), shift.Slots[11].Start);
        Assert.Equal(0.5, shift.Slots[11].Hours);

        var day = context.Line!;
        Assert.Equal("Chuyền 3", day.Line);
        Assert.True(day.HasRow);
        Assert.Equal(new DayPlan("883", "11H30", 185), day.Plan);
        Assert.Equal([180m, 170m, 185m, 190m, 186m], day.Hours.Take(5).Select(h => h.Quantity!.Value));
        Assert.Null(day.Hours[5].Quantity);
        Assert.Equal(911m, day.Actual);
        Assert.Equal(2128m, day.DailyTarget);
        Assert.Equal(6, day.SuggestedHour);
        Assert.Equal(93m, day.Hours[11].Target);
    }

    [Fact]
    public void Only_allowed_lines_are_listed()
    {
        var context = ProductionWorkbook.ReadContext(Sample(), SampleDay, new TimeOnly(9, 0), ["Chuyền 3"], "Chuyền 1");
        Assert.Equal(["Chuyền 3"], context.Lines);
        Assert.Null(context.Line);
    }

    [Fact]
    public void New_day_copies_plan_from_previous_day()
    {
        var workbook = Sample();
        var day = ProductionWorkbook.ReadLineDay(workbook, NextDay, new TimeOnly(7, 40), "Chuyền 3");
        Assert.False(day.HasRow);
        Assert.Equal(SampleDay, day.PlanCopiedFrom);
        Assert.Equal(1, day.SuggestedHour);

        ProductionWorkbook.WriteHour(workbook, NextDay, "Chuyền 3", 1, 150);

        var after = ProductionWorkbook.ReadLineDay(workbook, NextDay, new TimeOnly(8, 40), "Chuyền 3");
        Assert.True(after.HasRow);
        Assert.Equal(new DayPlan("883", "11H30", 185), after.Plan);
        Assert.Equal(150m, after.Hours[0].Quantity);
        Assert.Equal(2, after.SuggestedHour);

        var sheet = workbook.Workbook.Worksheet("NHAP_LIEU");
        Assert.Equal("A4:AL10", sheet.Tables.First().RangeAddress.ToString());
        Assert.Equal(new DateTime(2026, 10, 1), sheet.Cell("A10").GetDateTime());
        Assert.True(sheet.Cell("G10").HasFormula);
    }

    [Fact]
    public void Writing_twice_updates_the_same_row()
    {
        var workbook = Sample();
        ProductionWorkbook.WriteHour(workbook, SampleDay, "Chuyền 3", 6, 182);
        ProductionWorkbook.WriteHour(workbook, SampleDay, "Chuyền 3", 6, 184);
        var sheet = workbook.Workbook.Worksheet("NHAP_LIEU");
        Assert.Equal(184, sheet.Cell("M7").GetDouble());
        Assert.Equal("A4:AL9", sheet.Tables.First().RangeAddress.ToString());
    }

    [Fact]
    public void Line_without_any_plan_needs_one_first()
    {
        var workbook = Sample();
        var ex = Assert.Throws<EntryException>(() => ProductionWorkbook.WriteHour(workbook, NextDay, "Chuyền 5", 1, 10));
        Assert.Contains("chưa có kế hoạch", ex.Message);

        ProductionWorkbook.EnsureDayRow(workbook, NextDay, "Chuyền 5", new DayPlan("883", "8H", 50));
        ProductionWorkbook.WriteHour(workbook, NextDay, "Chuyền 5", 1, 10);
        var day = ProductionWorkbook.ReadLineDay(workbook, NextDay, new TimeOnly(8, 40), "Chuyền 5");
        Assert.Equal(new DayPlan("883", "8H", 50), day.Plan);
        Assert.Equal(10m, day.Hours[0].Quantity);
        Assert.Equal(8, day.Hours.Count);
    }

    [Fact]
    public void Defect_row_is_appended_to_the_table()
    {
        var workbook = Sample();
        var row = ProductionWorkbook.AddDefect(workbook,
            new DefectRecord(SampleDay, new TimeOnly(13, 44), "Chuyền 3", "Bung chỉ", 4, "chuyen3_1.jpg; chuyen3_2.jpg", "vai trái"));

        Assert.Equal(11, row);
        var sheet = workbook.Workbook.Worksheet("HANG_LOI");
        Assert.Equal("A4:J11", sheet.Tables.First().RangeAddress.ToString());
        Assert.Equal("Chuyền 3", sheet.Cell("C11").GetString());
        Assert.Equal("Bung chỉ", sheet.Cell("E11").GetString());
        Assert.Equal(4, sheet.Cell("F11").GetDouble());
        Assert.Equal("chuyen3_1.jpg; chuyen3_2.jpg", sheet.Cell("G11").GetString());
        Assert.Equal(TimeSpan.FromMinutes(13 * 60 + 44), sheet.Cell("B11").GetTimeSpan());
        Assert.True(sheet.Cell("D11").HasFormula);
    }
}
