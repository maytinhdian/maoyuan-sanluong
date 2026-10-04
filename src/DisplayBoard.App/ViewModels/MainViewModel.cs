using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayBoard.Core.Data;
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
    private readonly DataMaintenance _maintenance;
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
        DataMaintenance maintenance,
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
        _maintenance = maintenance;
        _logger = logger;
        PreviewHost = previewHost;

        _views = views.ToList();
        Screens = [new ScreenSettingsViewModel("TV1", _views), new ScreenSettingsViewModel("TV2", _views)];

        _snapshots.StatusChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshStatus);
        _screens.MonitorsChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshMonitors);
        _display.RunningChanged += (_, _) => IsRunning = _display.IsRunning;
        _server.StateChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshServer);
        _entry.Changed += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshEntry);
        _maintenance.Changed += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshMaintenance);

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
    [ObservableProperty] private string? _dataFolder;
    [ObservableProperty] private string? _contentFile;
    [ObservableProperty] private DateTime? _exportDate = DateTime.Today;
    [ObservableProperty] private bool _exportEnabled = true;
    [ObservableProperty] private string _exportTime = "22:00";
    [ObservableProperty] private string? _exportFolder;
    [ObservableProperty] private string _backupCount = "30";
    [ObservableProperty] private string? _maintenanceText;

    /// <summary>Thư mục dữ liệu đang dùng (để trống là thư mục mặc định).</summary>
    public string DataFolderText => string.IsNullOrWhiteSpace(DataFolder) ? DisplayConfiguration.DefaultDataFolder : DataFolder;

    partial void OnDataFolderChanged(string? value) => OnPropertyChanged(nameof(DataFolderText));
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private LoadStatus _status;
    [ObservableProperty] private string? _lastLoadedText;
    [ObservableProperty] private string? _dataInfoText;
    [ObservableProperty] private string? _lastError;
    [ObservableProperty] private bool _isMirror = true;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _defaultViewSeconds = "15";
    [ObservableProperty] private string _maxPagedViewSeconds = "60";
    [ObservableProperty] private string? _imagesFolder;
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
        DataFolder = config.DataFolder;
        ContentFile = config.ContentFile;
        ExportEnabled = config.Export.Enabled;
        ExportTime = config.Export.Time;
        ExportFolder = config.Export.Folder;
        BackupCount = config.BackupCount.ToString();
        IsMirror = config.DisplayMode == DisplayMode.Mirror;
        DefaultViewSeconds = config.DefaultViewSeconds.ToString();
        MaxPagedViewSeconds = config.MaxPagedViewSeconds.ToString();
        ImagesFolder = config.ImagesFolder;
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
        DataFolder = string.IsNullOrWhiteSpace(DataFolder) ? null : DataFolder.Trim(),
        ExcelFile = _config.Current.ExcelFile,
        ContentFile = string.IsNullOrWhiteSpace(ContentFile) ? null : ContentFile,
        Export = new ExcelExportSettings
        {
            Enabled = ExportEnabled,
            Time = TimeOnly.TryParse(ExportTime, System.Globalization.CultureInfo.InvariantCulture, out var at) ? at.ToString("HH:mm") : "22:00",
            Folder = string.IsNullOrWhiteSpace(ExportFolder) ? null : ExportFolder.Trim()
        },
        BackupCount = ParseInt(BackupCount, 30, 1, 3650),
        DisplayMode = IsMirror ? DisplayMode.Mirror : DisplayMode.Independent,
        AutoReload = _config.Current.AutoReload,
        DebounceMilliseconds = _config.Current.DebounceMilliseconds,
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
    private async Task ChooseDataFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Chọn thư mục dữ liệu (chứa sanluong.db)", InitialDirectory = DataFolderText };
        if (dialog.ShowDialog() != true)
            return;
        if (MessageBox.Show($"Dùng thư mục dữ liệu {dialog.FolderName}?\n\nNếu thư mục chưa có sanluong.db thì app bắt đầu với dữ liệu trống. Dữ liệu ở thư mục cũ vẫn giữ nguyên.",
                AppTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        DataFolder = dialog.FolderName;
        _logger.LogInformation("Đổi thư mục dữ liệu {Path}", DataFolder);
        await ApplySettingsAsync();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(DataFolderText);
        OpenLink(DataFolderText);
    }

    [RelayCommand]
    private void OpenAdminPage()
    {
        if (!_server.IsRunning)
        {
            MessageBox.Show("Máy chủ đang tắt. Bật máy chủ ở tab Máy chủ trước.", AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!_config.Current.DataEntry.Users.Any(u => u.Manager))
            MessageBox.Show("Chưa có ai được đánh dấu Quản lý. Vào tab Nhập liệu, tích ô Quản lý cho người cần vào trang quản lý rồi bấm Lưu cài đặt.",
                AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        OpenLink($"http://localhost:{_server.Port}/quan-ly");
    }

    [RelayCommand]
    private void ExportDay()
    {
        var date = DateOnly.FromDateTime(ExportDate ?? DateTime.Today);
        SaveExport(() => _maintenance.ExportDay(date));
    }

    [RelayCommand]
    private void ExportMonth()
    {
        var date = ExportDate ?? DateTime.Today;
        SaveExport(() => _maintenance.ExportMonth(new DateOnly(date.Year, date.Month, 1)));
    }

    private void SaveExport(Func<ExportFile> export)
    {
        ExportFile file;
        try
        {
            file = export();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            MessageBox.Show($"Không xuất được file Excel: {ex.Message}", AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Lưu file Excel",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = file.FileName,
            InitialDirectory = _config.Current.ResolveExportFolder()
        };
        Directory.CreateDirectory(dialog.InitialDirectory);
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            File.WriteAllBytes(dialog.FileName, file.Content);
            AddLog($"Đã xuất {dialog.FileName}");
            if (MessageBox.Show($"Đã lưu {Path.GetFileName(dialog.FileName)}. Mở file ngay?", AppTitle, MessageBoxButton.YesNo, MessageBoxImage.Information)
                == MessageBoxResult.Yes)
                OpenLink(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Không lưu được file (có thể file đang mở trong Excel): {ex.Message}", AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Nhập dữ liệu từ file Excel V20 của bản 3.x: đọc, so số, hỏi lại rồi mới thay dữ liệu.</summary>
    [RelayCommand]
    private async Task ImportExcelAsync()
    {
        var old = _config.Current.ExcelFile;
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file Excel đang dùng ở bản 3.x (Theo_doi_san_luong V20)",
            Filter = "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm",
            FileName = old is not null && File.Exists(old) ? old : ""
        };
        if (dialog.ShowDialog() != true)
            return;
        ImportPreview preview;
        try
        {
            preview = await Task.Run(() => DataMaintenance.PreviewImport(dialog.FileName));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MessageBox.Show($"Không đọc được file: {ex.Message}", AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var data = preview.Result.Data;
        var check = preview.Check;
        var text = $"File {Path.GetFileName(dialog.FileName)}: {data.Lines.Count} chuyền, {data.Products.Count} mã sản phẩm, " +
                   $"{data.Entries.Count} dòng nhập liệu, {data.Defects.Count} phiếu hàng lỗi.\n\n" +
                   (!check.HasExcelValues ? "File chưa được lưu bằng Excel nên không có số để so.\n\n"
                    : check.Mismatches == 0 ? $"Đã so {check.Rows.Count} dòng: số app tính khớp với file Excel.\n\n"
                    : $"CÓ {check.Mismatches} CHỖ KHÁC NHAU giữa file và số app tính, ví dụ:\n" +
                      string.Join("\n", check.Rows.Where(r => !r.Matches).Take(5).Select(r =>
                          $"  {r.Date:dd/MM} {r.Line}: Excel {r.ExcelActual:N0}/{r.ExcelTarget:N0}, app {r.AppActual:N0}/{r.AppTarget:N0}")
                          .Concat(check.DisplayDifferences.Take(5).Select(d => "  " + d))) + "\n\n") +
                   (preview.Result.Warnings.Count > 0 ? "Lưu ý:\n" + string.Join("\n", preview.Result.Warnings.Take(8).Select(w => "  " + w)) + "\n\n" : "") +
                   "Thay toàn bộ dữ liệu trên máy này bằng dữ liệu trong file? (Dữ liệu hiện có được sao lưu trước. Ảnh và file nội dung phụ cạnh file Excel được chép sang.)";
        if (MessageBox.Show(text, AppTitle, MessageBoxButton.YesNo, check.Mismatches == 0 ? MessageBoxImage.Question : MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            await Task.Run(() => _maintenance.ApplyImport(preview, "App trên máy chủ"));
            AddLog($"Đã nhập dữ liệu từ {dialog.FileName}");
            MessageBox.Show("Đã nhập dữ liệu. TV đã cập nhật.", AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Không nhập được: {ex.Message}", AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void ChooseExportFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Chọn thư mục lưu file Excel tự xuất" };
        if (dialog.ShowDialog() == true)
            ExportFolder = dialog.FolderName;
    }

    private void RefreshMaintenance()
    {
        var parts = new List<string>();
        if (_maintenance.LastBackup is { } backup)
            parts.Add($"Sao lưu: {Path.GetFileName(backup)}");
        if (_maintenance.LastExport is { } export)
            parts.Add($"Tự xuất Excel: {Path.GetFileName(export)}");
        if (_maintenance.LastError is { } error)
            parts.Add($"Lỗi sao lưu/xuất Excel: {error}");
        MaintenanceText = parts.Count == 0 ? null : string.Join("   ·   ", parts);
        if (_maintenance.LastError is { } e)
            AddLog($"Lỗi sao lưu/xuất Excel: {e}");
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

        await _snapshots.ReloadAsync();
        _watcher.Watch(config.WatchedFiles(), config.DebounceMilliseconds);
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
            : status.LastWriteAt is { } at ? $"Sẵn sàng · lưu phiếu gần nhất lúc {at.LocalDateTime:HH:mm dd/MM}"
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
                EntryJobStatus.Waiting => "đang chờ",
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

        DataInfoText = _snapshots.Current is { } current
            ? $"{current.Lines.Count} chuyền · TV đang hiện ngày {current.Summary.Date:dd/MM/yyyy} · {current.Summary.ProductCount} sản phẩm"
            : Status == LoadStatus.NoData ? "Chưa có dữ liệu. Bấm \"Nhập từ file Excel cũ…\" để lấy số liệu từ file đang dùng ở bản 3.x, hoặc khai báo chuyền, sản phẩm, ca ở trang quản lý." : null;
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
