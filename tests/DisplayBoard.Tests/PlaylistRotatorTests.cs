using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Time.Testing;

namespace DisplayBoard.Tests;

public class PlaylistRotatorTests
{
    private static DisplayDataSnapshot Snapshot(int employees = 3, bool notices = false, bool hourly = false)
    {
        var list = Enumerable.Range(1, employees)
            .Select(i => new EmployeeDaily($"NV{i:000}", "A", "Ép", null, 100, 100, 100, true, 0, i, null))
            .ToList();
        var empty = DisplayDataSnapshot.Empty(DateTimeOffset.Now);
        return empty with
        {
            Employees = list,
            Notices = notices ? [new Notice("T", "N", null, 1)] : [],
            Hourly = hourly ? [new HourlyPoint(new TimeOnly(8, 0), 10, 10)] : [],
        };
    }

    private static List<PlaylistItem> Playlist(params string[] ids) => ids.Select(id => new PlaylistItem { ViewId = id }).ToList();

    [Fact]
    public void Rotates_in_order_with_configured_duration()
    {
        var time = new FakeTimeProvider();
        var snapshot = Snapshot();
        using var rotator = new PlaylistRotator(ViewCatalog.CreateDefault(), () => snapshot, time);
        var playlist = Playlist(ViewIds.Overview, ViewIds.Ranking);
        playlist[1].Seconds = 5;

        rotator.Start(playlist, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60));
        Assert.Equal(ViewIds.Overview, rotator.CurrentViewId);

        time.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(ViewIds.Overview, rotator.CurrentViewId);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(ViewIds.Ranking, rotator.CurrentViewId);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(ViewIds.Overview, rotator.CurrentViewId);
    }

    [Fact]
    public void Skips_views_without_content()
    {
        var time = new FakeTimeProvider();
        var snapshot = Snapshot(notices: false, hourly: false);
        using var rotator = new PlaylistRotator(ViewCatalog.CreateDefault(), () => snapshot, time);
        var seen = new List<string>();
        rotator.ViewChanged += (_, id) => seen.Add(id);

        rotator.Start(Playlist(ViewIds.Ranking, ViewIds.Notice, ViewIds.Trend, ViewIds.Overview), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60));
        time.Advance(TimeSpan.FromSeconds(15));
        time.Advance(TimeSpan.FromSeconds(15));

        Assert.Equal([ViewIds.Ranking, ViewIds.Overview, ViewIds.Ranking], seen);
    }

    [Fact]
    public void Falls_back_to_overview_when_nothing_has_content()
    {
        var time = new FakeTimeProvider();
        var snapshot = Snapshot(employees: 0);
        using var rotator = new PlaylistRotator(ViewCatalog.CreateDefault(), () => snapshot, time);

        rotator.Start(Playlist(ViewIds.Notice, ViewIds.Trend), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60));

        Assert.Equal(ViewIds.Overview, rotator.CurrentViewId);
    }

    [Fact]
    public void Paged_view_stays_until_all_pages_shown_capped_by_max()
    {
        var time = new FakeTimeProvider();
        var snapshot = Snapshot(employees: 25); // 3 trang × 8 giây = 24 giây
        using var rotator = new PlaylistRotator(ViewCatalog.CreateDefault(), () => snapshot, time);

        rotator.Start(Playlist(ViewIds.Detail, ViewIds.Overview), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));

        time.Advance(TimeSpan.FromSeconds(19));
        Assert.Equal(ViewIds.Detail, rotator.CurrentViewId);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(ViewIds.Overview, rotator.CurrentViewId);
    }

    [Fact]
    public void Single_item_playlist_recovers_when_data_appears()
    {
        var time = new FakeTimeProvider();
        var snapshot = Snapshot(notices: false);
        using var rotator = new PlaylistRotator(ViewCatalog.CreateDefault(), () => snapshot, time);

        rotator.Start(Playlist(ViewIds.Notice), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60));
        Assert.Equal(ViewIds.Overview, rotator.CurrentViewId);

        snapshot = Snapshot(notices: true);
        time.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal(ViewIds.Notice, rotator.CurrentViewId);
    }
}
