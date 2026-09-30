using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.App.ViewModels.Displays;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels;

/// <summary>
/// Nội dung của một "màn hình": giữ playlist và view đang hiển thị.
/// Dùng chung cho cửa sổ TV và khung Xem trước. Ở chế độ Mirror, nhiều cửa sổ dùng chung một host.
/// </summary>
public sealed partial class DisplayHostViewModel : ObservableObject, IDisposable
{
    private readonly ISnapshotService _snapshots;
    private readonly DisplayViewFactory _factory;
    private readonly Dispatcher _dispatcher;
    private readonly PlaylistRotator _rotator;
    private readonly Dictionary<string, DisplayViewModelBase> _views = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private DisplayViewModelBase? _currentView;

    public DisplayHostViewModel(ISnapshotService snapshots, DisplayViewFactory factory, IEnumerable<IDisplayViewDefinition> definitions)
    {
        _snapshots = snapshots;
        _factory = factory;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _rotator = new PlaylistRotator(definitions, () => _snapshots.Current, TimeProvider.System);
        _rotator.ViewChanged += (_, id) => _dispatcher.BeginInvoke(() => Show(id));
        _snapshots.SnapshotChanged += OnSnapshotChanged;
    }

    public void Start(IReadOnlyList<PlaylistItem> playlist, DisplayConfiguration config)
    {
        if (playlist.Count == 0)
            playlist = [new PlaylistItem { ViewId = ViewIds.Overview }];
        _rotator.Start(playlist, TimeSpan.FromSeconds(Math.Max(3, config.DefaultViewSeconds)), TimeSpan.FromSeconds(Math.Max(5, config.MaxPagedViewSeconds)));
        Show(_rotator.CurrentViewId);
    }

    public void Next() => _rotator.Advance();

    private void Show(string viewId)
    {
        if (CurrentView is not null && string.Equals(CurrentView.ViewId, viewId, StringComparison.OrdinalIgnoreCase))
            return;
        if (!_views.TryGetValue(viewId, out var view))
        {
            view = _factory.Create(viewId);
            _views[viewId] = view;
        }
        view.Update(_snapshots.Current ?? DisplayDataSnapshot.Empty(DateTimeOffset.Now));
        CurrentView?.OnDeactivated();
        CurrentView = view;
        view.OnActivated();
    }

    /// <summary>Snapshot mới: cập nhật view hiện tại tại chỗ, không reset vòng xoay.</summary>
    private void OnSnapshotChanged(object? sender, DisplayDataSnapshot snapshot) =>
        _dispatcher.BeginInvoke(() => CurrentView?.Update(snapshot));

    public void Dispose()
    {
        _snapshots.SnapshotChanged -= OnSnapshotChanged;
        _rotator.Dispose();
        CurrentView?.OnDeactivated();
    }
}
