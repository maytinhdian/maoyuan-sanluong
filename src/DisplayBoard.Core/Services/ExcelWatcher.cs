using DisplayBoard.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Services;

/// <summary>
/// Theo dõi thư mục chứa file Excel. Excel lưu bằng cách ghi file tạm rồi đổi tên,
/// nên phải nghe cả Changed, Created và Renamed. Mỗi sự kiện reset bộ đếm debounce.
/// </summary>
public sealed class ExcelWatcher(ILogger<ExcelWatcher> logger) : IExcelWatcher
{
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private int _debounceMs;
    private string? _fileName;

    public event EventHandler? FileChanged;

    public void Watch(string filePath, int debounceMilliseconds)
    {
        Stop();
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (directory is null || !Directory.Exists(directory))
        {
            logger.LogWarning("Không theo dõi được {Path}: thư mục không tồn tại", filePath);
            return;
        }

        lock (_lock)
        {
            _fileName = Path.GetFileName(filePath);
            _debounceMs = Math.Max(100, debounceMilliseconds);
            _debounce = new Timer(_ => FileChanged?.Invoke(this, EventArgs.Empty));
            _watcher = new FileSystemWatcher(directory)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                IncludeSubdirectories = false
            };
            _watcher.Changed += OnEvent;
            _watcher.Created += OnEvent;
            _watcher.Deleted += OnEvent;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += (_, e) => logger.LogError(e.GetException(), "FileSystemWatcher lỗi");
            _watcher.EnableRaisingEvents = true;
        }
        logger.LogInformation("Bắt đầu theo dõi {Path}", filePath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (IsTarget(e.Name) || IsTarget(e.OldName))
            Trigger(e.ChangeType.ToString());
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (IsTarget(e.Name))
            Trigger(e.ChangeType.ToString());
    }

    private bool IsTarget(string? name) =>
        name is not null && string.Equals(Path.GetFileName(name), _fileName, StringComparison.OrdinalIgnoreCase);

    private void Trigger(string kind)
    {
        logger.LogDebug("Watcher: {Kind} {File}", kind, _fileName);
        lock (_lock)
        {
            _debounce?.Change(_debounceMs, Timeout.Infinite);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _watcher?.Dispose();
            _watcher = null;
            _debounce?.Dispose();
            _debounce = null;
        }
    }

    public void Dispose() => Stop();
}
