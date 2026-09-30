using DisplayBoard.Core.Models;
using DisplayBoard.Core.Services;

namespace DisplayBoard.Tests;

public class DataProcessorTests
{
    private static ProductionRecord Record(string code, string department, decimal quantity, decimal? target, int day = 1) =>
        new(new DateOnly(2026, 9, day), code, $"Name {code}", department, quantity, target, null);

    [Fact]
    public void Empty_input_gives_empty_summary()
    {
        Assert.Equal(ProductionSummary.Empty, DataProcessor.Summarize([]));
    }

    [Fact]
    public void Computes_totals_completion_and_target_status()
    {
        var summary = DataProcessor.Summarize(
        [
            Record("NV1", "Ép", 1250, 1200),
            Record("NV2", "Sơn", 980, 1200),
            Record("NV3", "Sơn", 500, null),
        ]);

        Assert.Equal(3, summary.TotalEmployees);
        Assert.Equal(2730, summary.TotalQuantity);
        Assert.Equal(2400, summary.TotalTarget);
        Assert.Equal(113.8m, summary.CompletionRate);
        Assert.Equal(1, summary.EmployeesMetTarget);
        Assert.Equal(1, summary.EmployeesBelowTarget);
    }

    [Fact]
    public void Completion_rate_is_zero_without_targets()
    {
        var summary = DataProcessor.Summarize([Record("NV1", "Ép", 100, null)]);

        Assert.Equal(0, summary.CompletionRate);
        Assert.Null(Assert.Single(summary.Ranking).CompletionRate);
    }

    [Fact]
    public void Ranks_employees_by_quantity_and_aggregates_multiple_days()
    {
        var summary = DataProcessor.Summarize(
        [
            Record("NV1", "Ép", 300, 400, day: 1),
            Record("NV2", "Ép", 500, 400, day: 1),
            Record("NV1", "Ép", 300, 400, day: 2),
        ]);

        Assert.Collection(
            summary.Ranking,
            r => Assert.Equal((1, "NV1", 600m, 800m, 75.0m), (r.Rank, r.EmployeeCode, r.Quantity, r.Target!.Value, r.CompletionRate!.Value)),
            r => Assert.Equal((2, "NV2", 500m), (r.Rank, r.EmployeeCode, r.Quantity)));
    }

    [Fact]
    public void Groups_by_department()
    {
        var summary = DataProcessor.Summarize(
        [
            Record("NV1", "Ép", 100, 100),
            Record("NV2", "Sơn", 300, 200),
            Record("NV3", "Sơn", 100, 200),
        ]);

        Assert.Collection(
            summary.Departments,
            d => Assert.Equal(new DepartmentSummary("Sơn", 2, 400, 400, 100), d),
            d => Assert.Equal(new DepartmentSummary("Ép", 1, 100, 100, 100), d));
    }
}
