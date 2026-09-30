using System.IO;

namespace DisplayBoard.App;

/// <summary>Per-user writable locations (the install folder may be read-only).</summary>
internal static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayBoard");

    public static string ConfigurationFile { get; } = Path.Combine(DataDirectory, "display-config.json");

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");
}
