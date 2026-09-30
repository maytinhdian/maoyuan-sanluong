namespace DisplayBoard.Core.Excel;

/// <summary>
/// A problem with a single data row. <paramref name="RowNumber"/> is the Excel row number (1-based, as shown in Excel).
/// </summary>
public sealed record ExcelRowIssue(int RowNumber, string? Column, string Message)
{
    public override string ToString() =>
        Column is null ? $"Dòng {RowNumber}: {Message}" : $"Dòng {RowNumber}, cột '{Column}': {Message}";
}
