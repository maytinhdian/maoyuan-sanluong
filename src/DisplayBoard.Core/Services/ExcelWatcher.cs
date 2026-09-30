using DisplayBoard.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Services;

/// <summary>
/// Theo dõi các file Excel. Excel lưu bằng cách ghi file tạm rồi đổi tên,
/// nên phải nghe cả Changed, Created và Renamed. Mỗi sự kiện reset bộ đếm debounce chung.
/// </summary>
public sealed class ExcelWatcher(ILogger<ExcelWatcher> logger) : IExcelWatcher
{
    private readonly object _lock = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly HashSet<string> _fileNames = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _debounce;
    private int _debounceMs;

    public event EventHandler? FileChanged;

    public void Watch(IReadOnlyList<string> filePaths, int debounceMilliseconds)
    {
        Stop();
        lock (_lock)
        {
            _debounceMs = Math.Max(100, debounceMilliseconds);
            _debounce = new Timer(_ => FileChanged?.Invoke(this, EventArgs.Empty));
            foreach (var group in filePaths
                         .Where(p => !string.IsNullOrWhiteSpace(p))
                         .Select(Path.GetFullPath)
                         .GroupBy(p => Path.GetDirectoryName(p)!, StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(group.Key))
                {
                    logger.LogWarning("Không theo dõi được thư mục {Directory}: không tồn tại", group.Key);
                    continue;
                }
                foreach (var path in group)
                    _fileNames.Add(Path.GetFileName(path));
                var watcher = new FileSystemWatcher(group.Key)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false
                };
                watcher.Changed += OnEvent;
                watcher.Created += OnEvent;
                watcher.Deleted += OnEvent;
                watcher.Renamed += OnRenamed;
                watcher.Error += (_, e) => logger.LogError(e.GetException(), "FileSystemWatcher lỗi");
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }
        logger.LogInformation("Bắt đầu theo dõi {Files}", string.Join(", ", filePaths));
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (IsTarget(e.Name) || IsTarget(e.OldName))
            Trigger(e.ChangeType.ToString(), e.Name);
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (IsTarget(e.Name))
            Trigger(e.ChangeType.ToString(), e.Name);
    }

    private bool IsTarget(string? name)
    {
        if (name is null)
            return false;
        lock (_lock)
        {
            return _fileNames.Contains(Path.GetFileName(name));
        }
    }

    private void Trigger(string kind, string? file)
    {
        logger.LogDebug("Watcher: {Kind} {File}", kind, file);
        lock (_lock)
        {
            _debounce?.Change(_debounceMs, Timeout.Infinite);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers)
                watcher.Dispose();
            _watchers.Clear();
            _fileNames.Clear();
            _debounce?.Dispose();
            _debounce = null;
        }
    }

    public void Dispose() => Stop();
}
