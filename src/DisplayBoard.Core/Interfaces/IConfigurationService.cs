using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface IConfigurationService
{
    DisplayConfiguration Current { get; }
    DisplayConfiguration Load();
    void Save(DisplayConfiguration configuration);
}
