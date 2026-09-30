namespace DisplayBoard.Core.Models;

/// <summary>A physical monitor. X/Y may be negative (monitors left of or above the primary).</summary>
public sealed record MonitorInfo(
    string Id,
    string DeviceName,
    int X, int Y,
    int Width, int Height,
    bool IsPrimary);
