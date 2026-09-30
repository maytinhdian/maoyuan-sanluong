namespace DisplayBoard.Core.Models;

/// <summary>Aggregated output of one department.</summary>
public sealed record DepartmentSummary(
    string Department,
    int EmployeeCount,
    decimal TotalQuantity,
    decimal TotalTarget)
{
    public decimal CompletionRate => ProductionSummary.CalculateCompletionRate(TotalQuantity, TotalTarget);
}
