using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DisplayBoard.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxLogEntries = 200;

    private readonly IConfigurationService _config;
    private readonly ISnapshotService _snapshots;
    private readonly IExcelWatcher _watcher;
    private readonly IScreenManager _screens;
    private readonly IDisplayManager _display;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(
        IConfigurationService config,
        ISnapshotService snapshots,
        IExcelWatcher watcher,
        IScreenManager screens,
        IDisplayManager display,
        IEnumerable<IDisplayViewDefinition> views,
        DisplayHostViewModel previewHost,
        ILogger<MainViewModel> logger)
    {
        _config = config;
        _snapshots = snapshots;
        _watcher = watcher;
        _screens = screens;
        _display = display;
        _logger = logger;
        PreviewHost = previewHost;

        var viewList = views.ToList();
        Screens = [new ScreenSettingsViewModel("TV1", viewList), new ScreenSettingsViewModel("TV2", viewList)];

        _snapshots.StatusChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshStatus);
        _screens.MonitorsChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshMonitors);
        _display.RunningChanged += (_, _) => IsRunning = _display.IsRunning;

        LoadFromConfiguration(_config.Current);
        RefreshStatus();
    }

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];
    public IReadOnlyList<ScreenSettingsViewModel> Screens { get; }
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<string> Log { get; } = [];
    public DisplayHostViewModel PreviewHost { get; }

    [ObservableProperty] private string? _excelFile;
    [ObservableProperty] private string? _sheetName;
    [ObservableProperty] private string? _contentFile;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private LoadStatus _status;
    [ObservableProperty] private string? _lastLoadedText;
    [ObservableProperty] private string? _dataInfoText;
    [ObservableProperty] private string? _lastError;
    [ObservableProperty] private bool _isMirror = true;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _previewScreenIndex;
    [ObservableProperty] private string _defaultViewSeconds = "15";
    [ObservableProperty] private string _maxPagedViewSeconds = "60";
    [ObservableProperty] private string _debounceMilliseconds = "800";
    [ObservableProperty] private string? _imagesFolder;
    [ObservableProperty] private bool _autoReload = true;

    public bool IsIndependent
    {
        get => !IsMirror;
        set => IsMirror = !value;
    }

    partial void OnIsMirrorChanged(bool value) => OnPropertyChanged(nameof(IsIndependent));

    public bool IsHealthy => Status is LoadStatus.Updated or LoadStatus.Ready;

    private void LoadFromConfiguration(DisplayConfiguration config)
    {
        ExcelFile = config.ExcelFile;
        SheetName = config.SheetName;
        ContentFile = config.ContentFile;
        IsMirror = config.DisplayMode == DisplayMode.Mirror;
        DefaultViewSeconds = config.DefaultViewSeconds.ToString();
        MaxPagedViewSeconds = config.MaxPagedViewSeconds.ToString();
        DebounceMilliseconds = config.DebounceMilliseconds.ToString();
        ImagesFolder = config.ImagesFolder;
        AutoReload = config.AutoReload;
        RefreshMonitors();

        var secondary = Monitors.Where(m => !m.IsPrimary).ToList();
        for (var i = 0; i < Screens.Count; i++)
        {
            var assignment = i < config.Screens.Count ? config.Screens[i] : null;
            if (assignment is null && i < secondary.Count)
                assignment = new ScreenAssignment { MonitorId = secondary[i].Id };
            Screens[i].Load(assignment, Monitors, i == 0 ? ViewIds.Overview : ViewIds.Ranking);
        }
    }

    public DisplayConfiguration BuildConfiguration() => new()
    {
        ExcelFile = ExcelFile,
        SheetName = string.IsNullOrWhiteSpace(SheetName) ? null : SheetName.Trim(),
        ContentFile = string.IsNullOrWhiteSpace(ContentFile) ? null : ContentFile,
        DisplayMode = IsMirror ? DisplayMode.Mirror : DisplayMode.Independent,
        AutoReload = AutoReload,
        DebounceMilliseconds = ParseInt(DebounceMilliseconds, 800, 100, 10000),
        ImagesFolder = string.IsNullOrWhiteSpace(ImagesFolder) ? null : ImagesFolder,
        DefaultViewSeconds = ParseInt(DefaultViewSeconds, 15, 3, 3600),
        MaxPagedViewSeconds = ParseInt(MaxPagedViewSeconds, 60, 5, 3600),
        Screens = Screens.Select(s => s.ToAssignment()).ToList()
    };

    private static int ParseInt(string? text, int fallback, int min, int max) =>
        int.TryParse(text, out var value) ? Math.Clamp(value, min, max) : fallback;

    [RelayCommand]
    private void RefreshMonitors()
    {
        var monitors = _screens.GetMonitors();
        Monitors.Clear();
        foreach (var monitor in monitors)
            Monitors.Add(monitor);
        foreach (var screen in Screens)
            screen.SelectedMonitor = monitors.FirstOrDefault(m => string.Equals(m.Id, screen.MonitorId, StringComparison.OrdinalIgnoreCase));
        AddLog($"Phát hiện {monitors.Count} màn hình: {string.Join(", ", monitors.Select(m => m.DisplayName))}");
    }

    [RelayCommand]
    private async Task ChooseFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file Excel dữ liệu",
            Filter = "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm",
            FileName = ExcelFile ?? ""
        };
        if (dialog.ShowDialog() != true)
            return;
        ExcelFile = dialog.FileName;
        _logger.LogInformation("Chọn file Excel {Path}", ExcelFile);
        await ApplySettingsAsync();
    }

    [RelayCommand]
    private void ChooseContentFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file nội dung phụ (thông báo, tên sản phẩm)",
            Filter = "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm",
            FileName = ContentFile ?? ""
        };
        if (dialog.ShowDialog() == true)
            ContentFile = dialog.FileName;
    }

    [RelayCommand]
    private void ChooseImagesFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Chọn thư mục ảnh" };
        if (dialog.ShowDialog() == true)
            ImagesFolder = dialog.FolderName;
    }

    /// <summary>Lưu cấu hình, theo dõi lại file và đọc lại dữ liệu.</summary>
    [RelayCommand]
    private async Task ApplySettingsAsync()
    {
        var config = BuildConfiguration();
        try
        {
            _config.Save(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Không lưu được cấu hình");
            MessageBox.Show($"Không lưu được cấu hình: {ex.Message}", "Display Board", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (!string.IsNullOrWhiteSpace(config.ExcelFile) && config.AutoReload)
            _watcher.Watch(config.WatchedFiles(), config.DebounceMilliseconds);
        else
            _watcher.Stop();

        await _snapshots.ReloadAsync();
        StartPreview();
        if (_display.IsRunning)
            _display.Start(config);
    }

    [RelayCommand]
    private Task ReloadAsync() => _snapshots.ReloadAsync();

    [RelayCommand]
    private void Preview() => StartPreview();

    [RelayCommand]
    private void NextPreviewView() => PreviewHost.Next();

    partial void OnPreviewScreenIndexChanged(int value) => StartPreview();

    private void StartPreview()
    {
        var config = BuildConfiguration();
        var index = IsMirror ? 0 : Math.Clamp(PreviewScreenIndex, 0, config.Screens.Count - 1);
        PreviewHost.Start(config.Screens[index].Playlist, config);
    }

    [RelayCommand]
    private void StartPresentation()
    {
        var config = BuildConfiguration();
        var assigned = config.Screens.Where(s => !string.IsNullOrWhiteSpace(s.MonitorId)).ToList();
        if (assigned.Count == 0)
        {
            MessageBox.Show("Chưa chọn màn hình cho TV1/TV2.", "Display Board", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var primary = Monitors.FirstOrDefault(m => m.IsPrimary);
        if (primary is not null && assigned.Any(s => string.Equals(s.MonitorId, primary.Id, StringComparison.OrdinalIgnoreCase))
            && MessageBox.Show("Một TV đang được gán vào màn hình chính. Cửa sổ trình chiếu sẽ che màn hình làm việc. Tiếp tục?",
                "Display Board", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            _config.Save(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Không lưu được cấu hình");
        }
        _display.Start(config);
        AddLog("Bắt đầu trình chiếu");
    }

    [RelayCommand]
    private void StopPresentation()
    {
        _display.Stop();
        AddLog("Dừng trình chiếu");
    }

    private void RefreshStatus()
    {
        Status = _snapshots.Status;
        StatusText = Status.ToVietnamese();
        LastError = _snapshots.LastError;
        LastLoadedText = _snapshots.LastLoadedAt is { } at ? at.LocalDateTime.ToString("HH:mm:ss dd/MM/yyyy") : null;
        OnPropertyChanged(nameof(IsHealthy));

        if (_snapshots.Current is { } current && current.SheetName.Length > 0)
            DataInfoText = current.HasMultipleLines
                ? $"{current.Lines.Count} chuyền ({current.SheetName}) · ngày {current.Summary.Date:dd/MM/yyyy} · {current.Summary.ProductCount} sản phẩm"
                : $"Sheet \"{current.SheetName}\" · ngày {current.Summary.Date:dd/MM/yyyy} · {current.Summary.ProductCount} sản phẩm";
        if (Status == LoadStatus.Updated && _snapshots.Current is { } snapshot)
        {
            Warnings.Clear();
            foreach (var warning in snapshot.Warnings)
                Warnings.Add(warning);
        }
        if (Status != LoadStatus.Reading)
            AddLog(LastError is null ? StatusText : $"{StatusText}: {LastError}");
    }

    private void AddLog(string message)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (Log.Count > MaxLogEntries)
            Log.RemoveAt(Log.Count - 1);
    }
}
