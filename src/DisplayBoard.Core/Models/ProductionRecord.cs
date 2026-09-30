namespace DisplayBoard.Core.Models;

/// <summary>Một dòng trong sheet DATA. <see cref="Quantity"/> là phần tăng thêm của lần nhập đó.</summary>
public sealed record ProductionRecord(
    int RowNumber,
    DateOnly Date,
    TimeOnly? Time,
    string? Shift,
    string EmployeeCode,
    string EmployeeName,
    string Department,
    decimal Quantity,
    decimal? Target,
    string? Note);
