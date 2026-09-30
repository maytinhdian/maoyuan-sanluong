namespace DisplayBoard.Core.Models;

/// <summary>One row of the <c>DATA</c> sheet.</summary>
public sealed record ProductionRecord(
    DateOnly Date,
    string EmployeeCode,
    string EmployeeName,
    string Department,
    decimal Quantity,
    decimal? Target,
    string? Note);
