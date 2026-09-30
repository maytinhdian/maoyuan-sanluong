using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;

namespace DisplayBoard.Tests;

public class DataProcessorTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 10, 42, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 28)));

    private static int _row = 1;

    private static ProductionRecord Rec(string code, string dept, decimal qty, decimal? target = 1200, string? time = null, DateOnly? date = null, string? shift = "A") =>
        new(++_row, date ?? Today, time is null ? null : TimeOnly.Parse(time), shift, code, "Tên " + code, dept, qty, target, null);

    private static WorkbookData Data(IEnumerable<ProductionRecord> records,
        IEnumerable<DepartmentInfo>? departments = null,
        IEnumerable<NoticeRow>? notices = null,
        Dictionary<string, string>? settings = null) =>
        new(records.ToList(), [], (departments ?? []).ToList(), (notices ?? []).ToList(), [],
            settings ?? new Dictionary<string, string>(), []);

    [Fact]
    public void Sums_increments_per_employee_and_takes_target_once()
    {
        var snapshot = new DataProcessor().Build(Data([
            Rec("NV001", "Ép", 150, time: "08:00"),
            Rec("NV001", "Ép", 160, time: "09:00"),
            Rec("NV002", "Sơn", 1300),
        ]), Now, null);

        var nv1 = snapshot.Employees.Single(e => e.EmployeeCode == "NV001");
        Assert.Equal(310m, nv1.Quantity);
        Assert.Equal(1200m, nv1.Target);
        Assert.Equal(26m, nv1.CompletionRate);
        Assert.False(nv1.IsMet);
        Assert.Equal(-890m, nv1.Shortfall);

        Assert.Equal(1610m, snapshot.Summary.TotalQuantity);
        Assert.Equal(2400m, snapshot.Summary.TotalTarget);
        Assert.Equal(67m, snapshot.Summary.CompletionRate);
        Assert.Equal(1, snapshot.Summary.MetCount);
        Assert.Equal(1, snapshot.Summary.NotMetCount);
    }

    [Fact]
    public void Ranks_by_quantity_then_rate_then_code()
    {
        var snapshot = new DataProcessor().Build(Data([
            Rec("NV003", "Ép", 1000, target: 1000),
            Rec("NV001", "Ép", 1000, target: 2000),
            Rec("NV002", "Ép", 1500),
            Rec("NV004", "Ép", 1000, target: 1000),
        ]), Now, null);

        Assert.Equal(["NV002", "NV003", "NV004", "NV001"], snapshot.Employees.Select(e => e.EmployeeCode));
        Assert.Equal([1, 2, 3, 4], snapshot.Employees.Select(e => e.Rank));
    }

    [Fact]
    public void Employee_without_target_is_neither_met_nor_not_met()
    {
        var snapshot = new DataProcessor().Build(Data([Rec("NV001", "Ép", 500, target: null)]), Now, null);

        var e = Assert.Single(snapshot.Employees);
        Assert.Null(e.IsMet);
        Assert.Null(e.CompletionRate);
        Assert.Equal(0, snapshot.Summary.NotMetCount);
        Assert.Equal(0m, snapshot.Summary.CompletionRate);
    }

    [Fact]
    public void Uses_today_or_falls_back_to_latest_date()
    {
        var yesterday = Today.AddDays(-1);
        var older = Today.AddDays(-3);
        var processor = new DataProcessor();

        var withToday = processor.Build(Data([Rec("NV001", "Ép", 1, date: yesterday), Rec("NV002", "Ép", 2)]), Now, null);
        Assert.Equal(Today, withToday.Summary.Date);
        Assert.True(withToday.Summary.IsToday);
        Assert.Equal(2m, withToday.Summary.TotalQuantity);

        var withoutToday = processor.Build(Data([Rec("NV001", "Ép", 1, date: older), Rec("NV002", "Ép", 2, date: yesterday)]), Now, null);
        Assert.Equal(yesterday, withoutToday.Summary.Date);
        Assert.False(withoutToday.Summary.IsToday);
    }

    [Fact]
    public void Department_target_from_config_overrides_employee_sum()
    {
        var snapshot = new DataProcessor().Build(Data(
            [Rec("NV001", "Ép", 3000), Rec("NV002", "Ép", 2220), Rec("NV003", "Sơn", 900)],
            departments: [new DepartmentInfo("Sơn", null, "#ABCDEF", "paint", 1), new DepartmentInfo("Ép", 5000, null, null, 2)]),
            Now, null);

        Assert.Equal(["Sơn", "Ép"], snapshot.Departments.Select(d => d.Name));
        var ep = snapshot.Departments[1];
        Assert.Equal(5220m, ep.Quantity);
        Assert.Equal(5000m, ep.Target);
        Assert.Equal(104m, ep.CompletionRate);
        var son = snapshot.Departments[0];
        Assert.Equal(1200m, son.Target);
        Assert.Equal("#ABCDEF", son.Color);
    }

    [Fact]
    public void Hourly_rounds_up_accumulates_and_hides_future()
    {
        var points = DataProcessor.BuildHourly(
            [Rec("NV001", "Ép", 100, time: "08:00"), Rec("NV001", "Ép", 50, time: "08:20"), Rec("NV002", "Ép", 30, time: "10:00"), Rec("NV003", "Ép", 999)],
            totalTarget: 1000, start: new TimeOnly(7, 0), end: new TimeOnly(17, 0), now: new TimeOnly(10, 42));

        Assert.Equal(11, points.Count);
        Assert.Equal(new TimeOnly(7, 0), points[0].Hour);
        Assert.Equal(0m, points[0].CumulativeQuantity);
        Assert.Equal(0m, points[0].CumulativeTarget);
        Assert.Equal(100m, points[1].CumulativeQuantity);   // 08:00
        Assert.Equal(150m, points[2].CumulativeQuantity);   // 09:00 gồm cả 08:20
        Assert.Equal(180m, points[3].CumulativeQuantity);   // 10:00
        Assert.Equal(180m, points[4].CumulativeQuantity);   // 11:00 = giờ hiện tại làm tròn lên
        Assert.Null(points[5].CumulativeQuantity);          // 12:00 tương lai
        Assert.Equal(1000m, points[10].CumulativeTarget);
        Assert.Equal(300m, points[3].CumulativeTarget);
    }

    [Fact]
    public void Notices_filtered_by_enabled_and_date_range()
    {
        var snapshot = new DataProcessor().Build(Data([], notices:
        [
            new NoticeRow("B", "hiện 2", null, null, null, 2, true),
            new NoticeRow("A", "hiện 1", null, Today, Today, 1, true),
            new NoticeRow("C", "tắt", null, null, null, 0, false),
            new NoticeRow("D", "hết hạn", null, null, Today.AddDays(-1), 0, true),
            new NoticeRow("E", "chưa tới", null, Today.AddDays(1), null, 0, true),
        ]), Now, null);

        Assert.Equal(["hiện 1", "hiện 2"], snapshot.Notices.Select(n => n.Content));
    }

    [Fact]
    public void Invalid_working_hours_fall_back_with_warning()
    {
        var snapshot = new DataProcessor().Build(Data([Rec("NV001", "Ép", 1, time: "08:00")],
            settings: new Dictionary<string, string> { ["giobatdau"] = "18:00", ["gioketthuc"] = "07:00" }), Now, null);

        Assert.Contains(snapshot.Warnings, w => w.Contains("GioKetThuc"));
        Assert.Equal(new TimeOnly(7, 0), snapshot.Hourly[0].Hour);
    }

    [Fact]
    public void Resolves_employee_photo_by_code()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "NV001.png"), [1]);
            var snapshot = new DataProcessor().Build(Data([Rec("NV001", "Ép", 1), Rec("NV002", "Ép", 1)]), Now, folder.FullName);

            Assert.EndsWith("NV001.png", snapshot.Employees.Single(e => e.EmployeeCode == "NV001").PhotoPath);
            Assert.Null(snapshot.Employees.Single(e => e.EmployeeCode == "NV002").PhotoPath);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
