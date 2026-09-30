namespace DisplayBoard.Core.Models;

public enum DisplayMode
{
    /// <summary>Every screen shows the same view.</summary>
    Mirror,

    /// <summary>Each screen shows its own view.</summary>
    Independent,
}

/// <summary>Contents of <c>display-config.json</c>. Monitor settings live here, never in Excel.</summary>
public sealed record DisplayConfiguration
{
    public string? ExcelFile { get; init; }
    public DisplayMode DisplayMode { get; init; } = DisplayMode.Mirror;
    public bool AutoReload { get; init; } = true;
    public int DebounceMilliseconds { get; init; } = 800;
    public IReadOnlyList<ScreenAssignment> Screens { get; init; } = [];

    public static DisplayConfiguration Default { get; } = new();
}

public sealed record ScreenAssignment(string MonitorId, string ViewId);
