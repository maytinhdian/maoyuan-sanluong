using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Excel;

/// <summary>
/// Outcome of reading a workbook: the valid records plus one warning per skipped row.
/// </summary>
public sealed record ExcelReadResult(
    IReadOnlyList<ProductionRecord> Records,
    IReadOnlyList<ExcelRowIssue> Warnings);
