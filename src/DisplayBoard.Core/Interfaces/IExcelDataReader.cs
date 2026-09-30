using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface IExcelDataReader
{
    Task<WorkbookData> ReadAsync(string filePath, CancellationToken ct);
}
