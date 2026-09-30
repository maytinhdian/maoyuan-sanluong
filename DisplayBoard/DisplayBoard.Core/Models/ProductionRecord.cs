namespace DisplayBoard.Core.Models;

/// <summary>
/// One row of the <c>DATA</c> sheet. An employee can have several rows per day (one per hourly entry);
/// <see cref="Quantity"/> is the value of that row only, aggregation happens later.
/// </summary>
public sealed record ProductionRecord(
    DateOnly Date,
    TimeOnly? Time,
    string? Shift,
    string EmployeeCode,
    string EmployeeName,
    string Department,
    decimal Quantity,
    decimal? Target,
    string? Note);
