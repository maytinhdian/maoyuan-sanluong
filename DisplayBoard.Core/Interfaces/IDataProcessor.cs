using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface IDataProcessor
{
    DisplayDataSnapshot BuildSnapshot(IReadOnlyList<ProductionRecord> records);
}
