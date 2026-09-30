using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface IConfigurationService
{
    string ConfigurationPath { get; }

    /// <summary>Returns defaults when the file is missing or unreadable.</summary>
    Task<DisplayConfiguration> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(DisplayConfiguration configuration, CancellationToken ct = default);
}
