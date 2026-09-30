using DisplayBoard.Core.Excel;

namespace DisplayBoard.Core.Interfaces;

/// <summary>
/// Reads production data from the DATA sheet of an Excel workbook. Read-only: never writes the file.
/// </summary>
public interface IExcelDataReader
{
    /// <summary>
    /// Reads and validates the workbook. Invalid rows are skipped and reported in
    /// <see cref="ExcelReadResult.Warnings"/>.
    /// </summary>
    /// <exception cref="ExcelValidationException">The workbook itself is unusable (missing file, missing DATA sheet, missing required columns).</exception>
    /// <exception cref="IOException">The file could not be opened, e.g. it is locked. Callers may retry.</exception>
    Task<ExcelReadResult> ReadAsync(string filePath, CancellationToken ct = default);
}
