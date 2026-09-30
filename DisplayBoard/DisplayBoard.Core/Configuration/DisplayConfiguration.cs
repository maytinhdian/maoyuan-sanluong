namespace DisplayBoard.Core.Configuration;

/// <summary>User settings persisted to <c>display-config.json</c>. Never stored in Excel.</summary>
public sealed record DisplayConfiguration
{
    public const int DefaultDebounceMilliseconds = 800;

    public static DisplayConfiguration Default { get; } = new();

    public string? ExcelFile { get; init; }

    public DisplayMode DisplayMode { get; init; } = DisplayMode.Mirror;

    public bool AutoReload { get; init; } = true;

    public int DebounceMilliseconds { get; init; } = DefaultDebounceMilliseconds;

    public IReadOnlyList<ScreenAssignment> Screens { get; init; } = [];

    /// <summary>Replaces values a hand-edited file could get wrong with safe defaults.</summary>
    public DisplayConfiguration Normalize() => this with
    {
        ExcelFile = string.IsNullOrWhiteSpace(ExcelFile) ? null : ExcelFile.Trim(),
        DisplayMode = Enum.IsDefined(DisplayMode) ? DisplayMode : DisplayMode.Mirror,
        DebounceMilliseconds = DebounceMilliseconds > 0 ? DebounceMilliseconds : DefaultDebounceMilliseconds,
        Screens = (Screens ?? [])
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.MonitorId) && !string.IsNullOrWhiteSpace(s.ViewId))
            .ToArray(),
    };
}
