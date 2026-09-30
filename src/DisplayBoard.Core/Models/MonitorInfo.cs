namespace DisplayBoard.Core.Models;

/// <summary>Màn hình vật lý. Tọa độ tính bằng pixel thật, có thể âm.</summary>
public sealed record MonitorInfo(
    string Id,
    string DeviceName,
    int X, int Y,
    int Width, int Height,
    bool IsPrimary)
{
    public string DisplayName => $"{DeviceName.TrimStart('\\', '.')} ({Width}×{Height}){(IsPrimary ? " – chính" : "")}";
}
