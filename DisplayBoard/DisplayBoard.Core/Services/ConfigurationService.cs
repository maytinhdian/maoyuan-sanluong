using System.Text.Json;
using System.Text.Json.Serialization;
using DisplayBoard.Core.Configuration;
using DisplayBoard.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Services;

/// <summary>Reads and writes <c>display-config.json</c>.</summary>
public sealed class ConfigurationService(ConfigurationOptions options, ILogger<ConfigurationService> logger)
    : IConfigurationService
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; } = options.FilePath;

    public async Task<DisplayConfiguration> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(FilePath))
        {
            logger.LogInformation("Configuration file {Path} not found, using defaults", FilePath);
            return DisplayConfiguration.Default;
        }

        try
        {
            await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            var configuration = await JsonSerializer.DeserializeAsync<DisplayConfiguration>(stream, JsonOptions, ct);
            if (configuration is null)
            {
                logger.LogWarning("Configuration file {Path} is empty, using defaults", FilePath);
                return DisplayConfiguration.Default;
            }

            logger.LogInformation("Configuration loaded from {Path}", FilePath);
            return configuration.Normalize();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Configuration file {Path} is invalid, using defaults", FilePath);
            return DisplayConfiguration.Default;
        }
    }

    public async Task SaveAsync(DisplayConfiguration configuration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temp file first so a crash mid-write never leaves a half-written config.
        var tempPath = FilePath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, configuration.Normalize(), JsonOptions, ct);
        }

        File.Move(tempPath, FilePath, overwrite: true);
        logger.LogInformation("Configuration saved to {Path}", FilePath);
    }
}
