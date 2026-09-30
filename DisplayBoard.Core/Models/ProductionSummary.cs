namespace DisplayBoard.Core.Models;

/// <summary>Aggregates computed by the app; Excel never has to contain these values.</summary>
public sealed record ProductionSummary(
    int TotalEmployees,
    decimal TotalQuantity,
    decimal TotalTarget,
    decimal CompletionRate,
    int EmployeesMetTarget,
    int EmployeesBelowTarget,
    IReadOnlyList<DepartmentSummary> Departments,
    IReadOnlyList<EmployeeResult> Ranking)
{
    public static ProductionSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, [], []);
}

public sealed record DepartmentSummary(
    string Department,
    int EmployeeCount,
    decimal TotalQuantity,
    decimal TotalTarget,
    decimal CompletionRate);

/// <summary>Per-employee totals, ordered by quantity in <see cref="ProductionSummary.Ranking"/>.</summary>
public sealed record EmployeeResult(
    int Rank,
    string EmployeeCode,
    string EmployeeName,
    string Department,
    decimal Quantity,
    decimal? Target,
    decimal? CompletionRate)
{
    /// <summary>Null when the employee has no target.</summary>
    public bool? MetTarget => Target is > 0 ? Quantity >= Target : null;
}
