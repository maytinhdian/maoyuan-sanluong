using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface IExcelDataReader
{
    Task<IReadOnlyList<ProductionRecord>> ReadAsync(string filePath, CancellationToken ct);
}
