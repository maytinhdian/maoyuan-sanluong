using System.Text.Json;
using System.Text.Json.Serialization;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Services;

/// <summary>Persists <see cref="DisplayConfiguration"/> as <c>display-config.json</c>.</summary>
public sealed class ConfigurationService(string configurationPath, ILogger<ConfigurationService> logger)
    : IConfigurationService
{
    public const string FileName = "display-config.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ConfigurationPath { get; } = configurationPath;

    /// <summary>%LOCALAPPDATA%\DisplayBoard\display-config.json</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayBoard", FileName);

    public async Task<DisplayConfiguration> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ConfigurationPath))
        {
            logger.LogInformation("No configuration at {Path}; using defaults", ConfigurationPath);
            return DisplayConfiguration.Default;
        }

        try
        {
            await using var stream = File.OpenRead(ConfigurationPath);
            var configuration = await JsonSerializer.DeserializeAsync<DisplayConfiguration>(stream, JsonOptions, ct)
                .ConfigureAwait(false);
            return configuration ?? DisplayConfiguration.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not read configuration at {Path}; using defaults", ConfigurationPath);
            return DisplayConfiguration.Default;
        }
    }

    public async Task SaveAsync(DisplayConfiguration configuration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var directory = Path.GetDirectoryName(ConfigurationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temp file and swap, so a crash mid-write never leaves a truncated config.
        var tempPath = ConfigurationPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, ConfigurationPath, overwrite: true);
        logger.LogInformation("Configuration saved to {Path}", ConfigurationPath);
    }
}
