using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface IExcelDataReader
{
    /// <summary>Reads the <c>DATA</c> sheet without locking the file for Excel.</summary>
    /// <exception cref="ExcelValidationException">The file or workbook cannot be used.</exception>
    Task<ExcelReadResult> ReadAsync(string filePath, CancellationToken ct = default);
}
