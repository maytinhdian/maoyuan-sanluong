namespace DisplayBoard.Core.Models;

/// <summary>
/// A physical monitor in virtual-screen coordinates. X and Y can be negative when the
/// monitor sits left of or above the primary screen.
/// </summary>
public sealed record MonitorInfo(
    string Id,
    string DeviceName,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary);
