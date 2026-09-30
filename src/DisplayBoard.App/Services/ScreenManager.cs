using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Win32;
using Screen = System.Windows.Forms.Screen;

namespace DisplayBoard.App.Services;

/// <summary>Liệt kê màn hình bằng WinForms Screen (tọa độ pixel thật, có thể âm).</summary>
public sealed class ScreenManager : IScreenManager, IDisposable
{
    public ScreenManager() => SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

    public event EventHandler? MonitorsChanged;

    public IReadOnlyList<MonitorInfo> GetMonitors() =>
        Screen.AllScreens
            .Select(s => new MonitorInfo(
                ToId(s.DeviceName), s.DeviceName,
                s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height,
                s.Primary))
            .OrderBy(m => m.IsPrimary)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>"\\.\DISPLAY2" → "DISPLAY2".</summary>
    public static string ToId(string deviceName) => deviceName.TrimStart('\\', '.');

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => MonitorsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose() => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
}
