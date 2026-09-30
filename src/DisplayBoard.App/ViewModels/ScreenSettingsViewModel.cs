using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels;

public sealed partial class PlaylistEntryViewModel(string viewId, string name) : ObservableObject
{
    public string ViewId { get; } = viewId;
    public string Name { get; } = name;

    [ObservableProperty] private bool _isEnabled;

    /// <summary>Số giây; trống = dùng thời gian mặc định.</summary>
    [ObservableProperty] private string _seconds = "";
}

/// <summary>Cấu hình của một TV: màn hình vật lý + playlist.</summary>
public sealed partial class ScreenSettingsViewModel : ObservableObject
{
    public ScreenSettingsViewModel(string name, IEnumerable<IDisplayViewDefinition> views)
    {
        Name = name;
        foreach (var view in views)
            Entries.Add(new PlaylistEntryViewModel(view.Id, view.Name));
    }

    public string Name { get; }
    public ObservableCollection<PlaylistEntryViewModel> Entries { get; } = [];

    [ObservableProperty] private MonitorInfo? _selectedMonitor;

    /// <summary>Id màn hình đã lưu, giữ lại cả khi màn hình đó đang bị rút.</summary>
    public string? MonitorId { get; set; }

    partial void OnSelectedMonitorChanged(MonitorInfo? value)
    {
        if (value is not null)
            MonitorId = value.Id;
    }

    [RelayCommand]
    private void MoveUp(PlaylistEntryViewModel? entry) => Move(entry, -1);

    [RelayCommand]
    private void MoveDown(PlaylistEntryViewModel? entry) => Move(entry, +1);

    private void Move(PlaylistEntryViewModel? entry, int delta)
    {
        if (entry is null)
            return;
        var index = Entries.IndexOf(entry);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Entries.Count)
            return;
        Entries.Move(index, target);
    }

    public void Load(ScreenAssignment? assignment, IReadOnlyList<MonitorInfo> monitors, string defaultViewId)
    {
        MonitorId = assignment?.MonitorId;
        SelectedMonitor = monitors.FirstOrDefault(m => string.Equals(m.Id, MonitorId, StringComparison.OrdinalIgnoreCase));

        var playlist = assignment?.Playlist is { Count: > 0 } p ? p : [new PlaylistItem { ViewId = defaultViewId }];
        // Các view trong playlist lên đầu theo đúng thứ tự, còn lại giữ thứ tự mặc định.
        var ordered = playlist
            .Select(item => (item, entry: Entries.FirstOrDefault(e => string.Equals(e.ViewId, item.ViewId, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.entry is not null)
            .DistinctBy(x => x.entry)
            .ToList();
        var rest = Entries.Except(ordered.Select(x => x.entry!)).ToList();
        Entries.Clear();
        foreach (var (item, entry) in ordered)
        {
            entry!.IsEnabled = true;
            entry.Seconds = item.Seconds?.ToString() ?? "";
            Entries.Add(entry);
        }
        foreach (var entry in rest)
        {
            entry.IsEnabled = false;
            Entries.Add(entry);
        }
    }

    public ScreenAssignment ToAssignment() => new()
    {
        MonitorId = MonitorId ?? "",
        Playlist = Entries
            .Where(e => e.IsEnabled)
            .Select(e => new PlaylistItem { ViewId = e.ViewId, Seconds = int.TryParse(e.Seconds, out var s) && s > 0 ? s : null })
            .ToList()
    };
}
