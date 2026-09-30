using DisplayBoard.Core.Configuration;

namespace DisplayBoard.Core.Interfaces;

public interface IConfigurationService
{
    string FilePath { get; }

    /// <summary>
    /// Loads the saved configuration. Returns <see cref="DisplayConfiguration.Default"/> when the
    /// file does not exist or cannot be parsed.
    /// </summary>
    Task<DisplayConfiguration> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(DisplayConfiguration configuration, CancellationToken ct = default);
}
