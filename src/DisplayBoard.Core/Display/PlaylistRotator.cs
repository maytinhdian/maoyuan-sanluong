using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Display;

/// <summary>
/// Xoay các view trong playlist. Không phụ thuộc UI: thời gian lấy từ <see cref="TimeProvider"/>,
/// sự kiện <see cref="ViewChanged"/> được phát trên thread của timer.
/// </summary>
public sealed class PlaylistRotator : IDisposable
{
    private readonly IReadOnlyDictionary<string, IDisplayViewDefinition> _views;
    private readonly Func<DisplayDataSnapshot?> _snapshot;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private IReadOnlyList<PlaylistItem> _playlist = [];
    private TimeSpan _defaultDuration;
    private TimeSpan _maxPagedDuration;
    private ITimer? _timer;
    private int _index = -1;

    public PlaylistRotator(IEnumerable<IDisplayViewDefinition> views, Func<DisplayDataSnapshot?> snapshot, TimeProvider time)
    {
        _views = views.ToDictionary(v => v.Id, StringComparer.OrdinalIgnoreCase);
        _snapshot = snapshot;
        _time = time;
    }

    public string CurrentViewId { get; private set; } = ViewIds.Overview;

    public event EventHandler<string>? ViewChanged;

    public void Start(IReadOnlyList<PlaylistItem> playlist, TimeSpan defaultDuration, TimeSpan maxPagedDuration)
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _playlist = playlist.Where(p => _views.ContainsKey(p.ViewId)).ToList();
            _defaultDuration = defaultDuration;
            _maxPagedDuration = maxPagedDuration;
            _index = -1;
            _timer = _time.CreateTimer(_ => Advance(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        Advance();
    }

    /// <summary>Chuyển sang view kế tiếp có dữ liệu. Toàn bộ playlist không có dữ liệu thì về Tổng quan.</summary>
    public void Advance()
    {
        string viewId;
        TimeSpan duration;
        lock (_lock)
        {
            if (_timer is null)
                return;
            var snapshot = _snapshot();
            var next = FindNext(snapshot);
            if (next is null)
            {
                _index = -1;
                viewId = ViewIds.Overview;
                duration = _defaultDuration;
            }
            else
            {
                _index = next.Value;
                var item = _playlist[_index];
                viewId = item.ViewId;
                duration = GetDuration(item, snapshot);
            }
            // Playlist 1 phần tử (hoặc chỉ 1 view có dữ liệu) vẫn đặt timer để tự phục hồi khi dữ liệu thay đổi.
            _timer.Change(duration, Timeout.InfiniteTimeSpan);
        }

        var changed = !string.Equals(CurrentViewId, viewId, StringComparison.OrdinalIgnoreCase);
        CurrentViewId = viewId;
        if (changed)
            ViewChanged?.Invoke(this, viewId);
    }

    private int? FindNext(DisplayDataSnapshot? snapshot)
    {
        for (var step = 1; step <= _playlist.Count; step++)
        {
            var candidate = (_index + step) % _playlist.Count;
            var definition = _views[_playlist[candidate].ViewId];
            if (snapshot is null ? definition.Id == ViewIds.Overview : definition.HasContent(snapshot))
                return candidate;
        }
        return null;
    }

    private TimeSpan GetDuration(PlaylistItem item, DisplayDataSnapshot? snapshot)
    {
        var configured = item.Seconds is > 0 ? TimeSpan.FromSeconds(item.Seconds.Value) : _defaultDuration;
        var required = snapshot is null ? null : _views[item.ViewId].GetRequiredDuration(snapshot);
        if (required is null || required <= configured)
            return configured;
        return required.Value < _maxPagedDuration ? required.Value : TimeSpan.FromTicks(Math.Max(_maxPagedDuration.Ticks, configured.Ticks));
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose() => Stop();
}
