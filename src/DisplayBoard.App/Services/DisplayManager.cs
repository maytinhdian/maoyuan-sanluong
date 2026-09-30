using System.Windows;
using DisplayBoard.App.ViewModels;
using DisplayBoard.App.Views;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.App.Services;

/// <summary>Tạo/đóng cửa sổ TV theo cấu hình. Chạy trên UI thread.</summary>
public sealed class DisplayManager : IDisplayManager
{
    private readonly IScreenManager _screens;
    private readonly Func<DisplayHostViewModel> _hostFactory;
    private readonly ILogger<DisplayManager> _logger;
    private readonly List<(DisplayWindow Window, string MonitorId)> _windows = [];
    private readonly List<DisplayHostViewModel> _hosts = [];
    private DisplayConfiguration? _configuration;

    public DisplayManager(IScreenManager screens, Func<DisplayHostViewModel> hostFactory, ILogger<DisplayManager> logger)
    {
        _screens = screens;
        _hostFactory = hostFactory;
        _logger = logger;
        _screens.MonitorsChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(OnMonitorsChanged);
    }

    public bool IsRunning => _configuration is not null;
    public event EventHandler? RunningChanged;

    public void Start(DisplayConfiguration configuration)
    {
        CloseAll();
        _configuration = configuration;
        OpenWindows();
        _logger.LogInformation("Bắt đầu trình chiếu ({Mode}) trên {Count} màn hình", configuration.DisplayMode, _windows.Count);
        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        CloseAll();
        _configuration = null;
        _logger.LogInformation("Dừng trình chiếu");
        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OpenWindows()
    {
        var config = _configuration!;
        var monitors = _screens.GetMonitors().ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var assignments = config.Screens.Where(s => !string.IsNullOrWhiteSpace(s.MonitorId)).ToList();

        DisplayHostViewModel? shared = null;
        if (config.DisplayMode == DisplayMode.Mirror && assignments.Count > 0)
        {
            shared = _hostFactory();
            shared.Start(assignments[0].Playlist, config);
            _hosts.Add(shared);
        }

        foreach (var assignment in assignments)
        {
            if (!monitors.TryGetValue(assignment.MonitorId, out var monitor))
            {
                _logger.LogWarning("Không tìm thấy màn hình {Monitor}, bỏ qua", assignment.MonitorId);
                continue;
            }
            if (_windows.Any(w => string.Equals(w.MonitorId, monitor.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            var host = shared;
            if (host is null)
            {
                host = _hostFactory();
                host.Start(assignment.Playlist, config);
                _hosts.Add(host);
            }
            var window = new DisplayWindow(monitor) { DataContext = host };
            window.Show();
            _windows.Add((window, monitor.Id));
            _logger.LogInformation("Mở cửa sổ trên {Monitor} {Width}x{Height} tại ({X},{Y})", monitor.Id, monitor.Width, monitor.Height, monitor.X, monitor.Y);
        }
    }

    /// <summary>Rút/cắm màn hình: đóng cửa sổ của màn hình đã mất, đặt lại vị trí, mở lại cửa sổ cho màn hình vừa cắm.</summary>
    private void OnMonitorsChanged()
    {
        if (_configuration is null)
            return;
        var monitors = _screens.GetMonitors();
        _logger.LogInformation("Thay đổi màn hình: {Monitors}", string.Join(", ", monitors.Select(m => m.Id)));
        try
        {
            // Mở lại toàn bộ là cách an toàn nhất khi tọa độ/độ phân giải đổi.
            CloseAll();
            OpenWindows();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi sắp xếp lại cửa sổ sau khi đổi màn hình");
        }
    }

    private void CloseAll()
    {
        foreach (var (window, _) in _windows)
            window.Close();
        _windows.Clear();
        foreach (var host in _hosts)
            host.Dispose();
        _hosts.Clear();
    }
}
