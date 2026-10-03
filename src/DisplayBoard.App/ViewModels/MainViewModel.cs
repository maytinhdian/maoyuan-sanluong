using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Entry;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using DisplayBoard.Server;
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
    private readonly IBoardServer _server;
    private readonly EntryService _entry;
    private readonly ILogger<MainViewModel> _logger;
    private readonly List<IDisplayViewDefinition> _views;

    public MainViewModel(
        IConfigurationService config,
        ISnapshotService snapshots,
        IExcelWatcher watcher,
        IScreenManager screens,
        IDisplayManager display,
        IBoardServer server,
        EntryService entry,
        IEnumerable<IDisplayViewDefinition> views,
        DisplayHostViewModel previewHost,
        ILogger<MainViewModel> logger)
    {
        _config = config;
        _snapshots = snapshots;
        _watcher = watcher;
        _screens = screens;
        _display = display;
        _server = server;
        _entry = entry;
        _logger = logger;
        PreviewHost = previewHost;

        _views = views.ToList();
        Screens = [new ScreenSettingsViewModel("TV1", _views), new ScreenSettingsViewModel("TV2", _views)];

        _snapshots.StatusChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshStatus);
        _screens.MonitorsChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshMonitors);
        _display.RunningChanged += (_, _) => IsRunning = _display.IsRunning;
        _server.StateChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshServer);
        _entry.Changed += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshEntry);

        LoadFromConfiguration(_config.Current);
        RefreshStatus();
        RefreshServer();
    }

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];
    public IReadOnlyList<ScreenSettingsViewModel> Screens { get; }
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<string> Log { get; } = [];
    public DisplayHostViewModel PreviewHost { get; }

    /// <summary>Các TV xem qua mạng LAN (phần chính của bản 2.x).</summary>
    public ObservableCollection<NetworkTvViewModel> NetworkTvs { get; } = [];

    /// <summary>Địa chỉ trang chọn TV của máy chủ, mỗi địa chỉ IP một dòng.</summary>
    public ObservableCollection<string> ServerAddresses { get; } = [];
    public ObservableCollection<string> ConnectedTvs { get; } = [];

    /// <summary>Người được nhập liệu qua trình duyệt (bản 3.x).</summary>
    public ObservableCollection<EntryUserViewModel> EntryUsers { get; } = [];

    /// <summary>Địa chỉ trang nhập liệu /nhap, mỗi địa chỉ IP một dòng.</summary>
    public ObservableCollection<string> EntryAddresses { get; } = [];

    /// <summary>Các phiếu nhập gần đây (mới nhất trước).</summary>
    public ObservableCollection<string> EntryJobs { get; } = [];

    [ObservableProperty] private bool _entryEnabled = true;
    [ObservableProperty] private EntryUserViewModel? _selectedEntryUser;
    [ObservableProperty] private string _entryStatusText = "";
    [ObservableProperty] private string? _entryError;

    [ObservableProperty] private NetworkTvViewModel? _selectedTv;
    [ObservableProperty] private NetworkTvViewModel? _previewTv;

    [ObservableProperty] private string? _appName;
    /// <summary>Tên đang dùng (đã lưu); để trống ô tên thì là "Display Board".</summary>
    [ObservableProperty] private string _appTitle = DisplayConfiguration.DefaultAppName;
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
    [ObservableProperty] private string _defaultViewSeconds = "15";
    [ObservableProperty] private string _maxPagedViewSeconds = "60";
    [ObservableProperty] private string _debounceMilliseconds = "800";
    [ObservableProperty] private string? _imagesFolder;
    [ObservableProperty] private bool _autoReload = true;
    [ObservableProperty] private bool _serverEnabled = true;
    [ObservableProperty] private string _serverPort = LanServerSettings.DefaultPort.ToString();
    [ObservableProperty] private string? _serverAccessKey;
    [ObservableProperty] private string _serverStatusText = "";
    [ObservableProperty] private bool _serverRunning;
    [ObservableProperty] private string? _serverError;

    public bool IsIndependent
    {
        get => !IsMirror;
        set => IsMirror = !value;
    }

    partial void OnIsMirrorChanged(bool value) => OnPropertyChanged(nameof(IsIndependent));

    public bool IsHealthy => Status is LoadStatus.Updated or LoadStatus.Ready;

    private void LoadFromConfiguration(DisplayConfiguration config)
    {
        AppName = config.AppName;
        AppTitle = config.ResolveAppName();
        ExcelFile = config.ExcelFile;
        SheetName = config.SheetName;
        ContentFile = config.ContentFile;
        IsMirror = config.DisplayMode == DisplayMode.Mirror;
        DefaultViewSeconds = config.DefaultViewSeconds.ToString();
        MaxPagedViewSeconds = config.MaxPagedViewSeconds.ToString();
        DebounceMilliseconds = config.DebounceMilliseconds.ToString();
        ImagesFolder = config.ImagesFolder;
        AutoReload = config.AutoReload;
        EntryEnabled = config.DataEntry.Enabled;
        EntryUsers.Clear();
        foreach (var user in config.DataEntry.Users)
            EntryUsers.Add(EntryUserViewModel.From(user));
        SelectedEntryUser = EntryUsers.FirstOrDefault();
        ServerEnabled = config.Server.Enabled;
        ServerPort = config.Server.Port.ToString();
        ServerAccessKey = config.Server.AccessKey;
        RefreshMonitors();

        NetworkTvs.Clear();
        foreach (var screen in config.ResolveNetworkScreens())
        {
            var tv = new NetworkTvViewModel(screen.Number, _views);
            tv.Load(screen);
            NetworkTvs.Add(tv);
        }
        SelectedTv = NetworkTvs.FirstOrDefault();
        PreviewTv = NetworkTvs.FirstOrDefault();

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
        AppName = string.IsNullOrWhiteSpace(AppName) ? null : AppName.Trim(),
        ExcelFile = ExcelFile,
        SheetName = string.IsNullOrWhiteSpace(SheetName) ? null : SheetName.Trim(),
        ContentFile = string.IsNullOrWhiteSpace(ContentFile) ? null : ContentFile,
        DisplayMode = IsMirror ? DisplayMode.Mirror : DisplayMode.Independent,
        AutoReload = AutoReload,
        DebounceMilliseconds = ParseInt(DebounceMilliseconds, 800, 100, 10000),
        ImagesFolder = string.IsNullOrWhiteSpace(ImagesFolder) ? null : ImagesFolder,
        DefaultViewSeconds = ParseInt(DefaultViewSeconds, 15, 3, 3600),
        MaxPagedViewSeconds = ParseInt(MaxPagedViewSeconds, 60, 5, 3600),
        Screens = Screens.Select(s => s.ToAssignment()).ToList(),
        NetworkScreens = NetworkTvs.Select(t => t.ToScreen()).ToList(),
        Server = new LanServerSettings
        {
            Enabled = ServerEnabled,
            Port = ParseInt(ServerPort, LanServerSettings.DefaultPort, 1024, 65535),
            AccessKey = string.IsNullOrWhiteSpace(ServerAccessKey) ? null : ServerAccessKey.Trim()
        },
        DataEntry = new DataEntrySettings
        {
            Enabled = EntryEnabled,
            Users = EntryUsers.Select(u => u.ToUser()).Where(u => u.Name.Length > 0 || u.Pin.Length > 0).ToList()
        }
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
        if (EntryUsersProblem(config.DataEntry) is { } problem)
        {
            MessageBox.Show(problem, AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        AppTitle = config.ResolveAppName();
        try
        {
            _config.Save(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Không lưu được cấu hình");
            MessageBox.Show($"Không lưu được cấu hình: {ex.Message}", AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (!string.IsNullOrWhiteSpace(config.ExcelFile) && config.AutoReload)
            _watcher.Watch(config.WatchedFiles(), config.DebounceMilliseconds);
        else
            _watcher.Stop();

        await _snapshots.ReloadAsync();
        await _server.ApplyAsync(config);
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

    partial void OnPreviewTvChanged(NetworkTvViewModel? value) => StartPreview();

    /// <summary>Xem trước nội dung của TV đang chọn ở ô "Xem nội dung của".</summary>
    private void StartPreview()
    {
        var config = BuildConfiguration();
        var tv = PreviewTv ?? NetworkTvs.FirstOrDefault();
        PreviewHost.Start(tv?.ToScreen().Playlist ?? [], config);
    }

    [RelayCommand]
    private void AddTv()
    {
        var number = NetworkTvs.Count == 0 ? 1 : NetworkTvs.Max(t => t.Number) + 1;
        var tv = new NetworkTvViewModel(number, _views);
        tv.Load(new NetworkScreen { Number = number, Playlist = [new PlaylistItem { ViewId = ViewIds.Overview }] });
        NetworkTvs.Add(tv);
        SelectedTv = tv;
        UpdateTvLinks();
        AddLog($"Thêm {tv.DisplayName}. Bấm Lưu để áp dụng.");
    }

    [RelayCommand]
    private void RemoveTv(NetworkTvViewModel? tv)
    {
        tv ??= SelectedTv;
        if (tv is null)
            return;
        if (NetworkTvs.Count <= 1)
        {
            MessageBox.Show("Cần giữ ít nhất một TV.", AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Xoá {tv.DisplayName}? TV đang mở địa chỉ này sẽ báo chưa có TV.", AppTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var index = NetworkTvs.IndexOf(tv);
        NetworkTvs.Remove(tv);
        SelectedTv = NetworkTvs[Math.Clamp(index, 0, NetworkTvs.Count - 1)];
        if (PreviewTv == tv)
            PreviewTv = NetworkTvs.FirstOrDefault();
        AddLog($"Xoá {tv.DisplayName}. Bấm Lưu để áp dụng.");
    }

    /// <summary>Mỗi người cần tên và PIN riêng (ít nhất 4 ký tự) vì đăng nhập chỉ bằng PIN.</summary>
    private static string? EntryUsersProblem(DataEntrySettings settings)
    {
        var users = settings.Users;
        if (users.FirstOrDefault(u => u.Name.Length == 0) is { } noName)
            return $"Người nhập liệu có PIN {noName.Pin} chưa có tên.";
        if (users.FirstOrDefault(u => u.Pin.Length < 4) is { } shortPin)
            return $"Mã PIN của {shortPin.Name} cần ít nhất 4 ký tự.";
        if (users.GroupBy(u => u.Pin).FirstOrDefault(g => g.Count() > 1) is { } same)
            return $"{string.Join(" và ", same.Select(u => u.Name))} đang trùng mã PIN. Mỗi người cần một mã PIN riêng.";
        if (users.GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice)
            return $"Có hai người cùng tên \"{twice.Key}\".";
        return null;
    }

    [RelayCommand]
    private void AddEntryUser()
    {
        var pins = EntryUsers.Select(u => u.Pin).ToHashSet();
        string pin;
        do
            pin = Random.Shared.Next(1000, 10000).ToString();
        while (pins.Contains(pin));
        var user = new EntryUserViewModel { Name = $"Tổ trưởng {EntryUsers.Count + 1}", Pin = pin };
        EntryUsers.Add(user);
        SelectedEntryUser = user;
        AddLog($"Thêm người nhập liệu {user.Name}. Bấm Lưu để áp dụng.");
    }

    [RelayCommand]
    private void RemoveEntryUser(EntryUserViewModel? user)
    {
        user ??= SelectedEntryUser;
        if (user is null)
            return;
        EntryUsers.Remove(user);
        SelectedEntryUser = EntryUsers.FirstOrDefault();
        AddLog($"Xoá người nhập liệu {user.Name}. Bấm Lưu để áp dụng.");
    }

    private void RefreshEntry()
    {
        var status = _entry.Status;
        EntryError = _entry.UnavailableReason ?? status.WaitingReason ?? status.LastError;
        EntryStatusText = !_server.IsRunning ? "Máy chủ đang tắt nên chưa nhập liệu được."
            : !EntryEnabled ? "Đang tắt nhập liệu."
            : EntryUsers.Count == 0 ? "Chưa có ai được nhập liệu. Thêm người và mã PIN bên dưới."
            : status.Pending > 0 ? $"Đang chờ ghi {status.Pending} phiếu vào Excel"
            : status.LastWriteAt is { } at ? $"Sẵn sàng · ghi vào Excel lần cuối lúc {at.LocalDateTime:HH:mm dd/MM}"
            : "Sẵn sàng";

        EntryAddresses.Clear();
        if (_server.IsRunning)
            foreach (var url in BoardServer.EntryUrls(_server.Port))
                EntryAddresses.Add(url);

        EntryJobs.Clear();
        foreach (var job in _entry.Recent(null, 15))
        {
            var state = job.Status switch
            {
                EntryJobStatus.Done => "đã ghi",
                EntryJobStatus.Failed => "lỗi: " + job.Message,
                EntryJobStatus.Waiting => "đang chờ Excel",
                _ => "đang ghi"
            };
            EntryJobs.Add($"{job.CreatedAt.LocalDateTime:HH:mm}  {job.User} · {job.Line} · {job.Summary} · {state}");
        }
    }

    [RelayCommand]
    private void StartPresentation()
    {
        var config = BuildConfiguration();
        var assigned = config.Screens.Where(s => !string.IsNullOrWhiteSpace(s.MonitorId)).ToList();
        if (assigned.Count == 0)
        {
            MessageBox.Show("Chưa chọn màn hình cho TV1/TV2.", AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var primary = Monitors.FirstOrDefault(m => m.IsPrimary);
        if (primary is not null && assigned.Any(s => string.Equals(s.MonitorId, primary.Id, StringComparison.OrdinalIgnoreCase))
            && MessageBox.Show("Một TV đang được gán vào màn hình chính. Cửa sổ trình chiếu sẽ che màn hình làm việc. Tiếp tục?",
                AppTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
        _ = _server.ApplyAsync(config);
        AddLog("Bắt đầu trình chiếu");
    }

    [RelayCommand]
    private void StopPresentation()
    {
        _display.Stop();
        AddLog("Dừng trình chiếu");
    }

    private void RefreshServer()
    {
        ServerRunning = _server.IsRunning;
        ServerError = _server.LastError;
        var clients = _server.Clients;
        ServerStatusText = _server.IsRunning
            ? $"Đang chạy ở cổng {_server.Port} · {clients.Count} TV đang xem"
            : ServerEnabled && _server.LastError is not null ? "Không chạy được" : "Đang tắt";

        ServerAddresses.Clear();
        if (_server.IsRunning)
            foreach (var url in BoardServer.ScreenUrls(_server.Port, 0))
                ServerAddresses.Add(url[..url.IndexOf("/tv/", StringComparison.Ordinal)] + "/");
        UpdateTvLinks();
        RefreshEntry();

        ConnectedTvs.Clear();
        foreach (var client in clients)
        {
            var name = NetworkTvs.FirstOrDefault(t => t.Number == client.Screen)?.DisplayName ?? $"TV số {client.Screen} (đã xoá)";
            ConnectedTvs.Add($"{name}  ·  {client.Address}  ·  từ {client.ConnectedAt.LocalDateTime:HH:mm dd/MM}");
        }
    }

    /// <summary>Địa chỉ và số màn hình đang mở của từng TV.</summary>
    private void UpdateTvLinks()
    {
        var clients = _server.Clients;
        var key = _config.Current.Server.AccessKey;
        foreach (var tv in NetworkTvs)
        {
            tv.Url = _server.IsRunning ? BoardServer.ScreenUrls(_server.Port, tv.Number, key).FirstOrDefault() : null;
            tv.Viewers = clients.Count(c => c.Screen == tv.Number);
        }
    }

    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            Clipboard.SetText(url);
            AddLog($"Đã copy địa chỉ {url}");
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            _logger.LogWarning(ex, "Không copy được vào clipboard");
        }
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
