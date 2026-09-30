namespace DisplayBoard.Core.Models;

/// <summary>
/// Figures the app computes from the records. Excel never has to contain these totals.
/// </summary>
public sealed record ProductionSummary(
    int TotalEmployees,
    decimal TotalQuantity,
    decimal TotalTarget,
    int AchievedCount,
    int NotAchievedCount,
    IReadOnlyList<DepartmentSummary> Departments)
{
    public static ProductionSummary Empty { get; } = new(0, 0m, 0m, 0, 0, []);

    /// <summary>Completion rate in percent (0–100+).</summary>
    public decimal CompletionRate => CalculateCompletionRate(TotalQuantity, TotalTarget);

    public static decimal CalculateCompletionRate(decimal quantity, decimal target) =>
        target > 0 ? quantity / target * 100m : 0m;
}
