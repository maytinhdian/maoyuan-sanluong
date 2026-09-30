namespace DisplayBoard.Core.Models;

/// <summary>Records read from the workbook plus the rows that were skipped.</summary>
public sealed record ExcelReadResult(
    IReadOnlyList<ProductionRecord> Records,
    IReadOnlyList<RowWarning> Warnings);

/// <summary>A row that failed validation. <see cref="RowNumber"/> is the Excel row number (1-based).</summary>
public sealed record RowWarning(int RowNumber, string Message);
