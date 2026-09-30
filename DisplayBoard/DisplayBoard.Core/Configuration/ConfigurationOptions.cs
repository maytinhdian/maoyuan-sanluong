namespace DisplayBoard.Core.Configuration;

public sealed class ConfigurationOptions
{
    public const string FileName = "display-config.json";

    /// <summary>Full path of <c>display-config.json</c>.</summary>
    public required string FilePath { get; init; }
}
