using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Time.Testing;

namespace DisplayBoard.Tests;

public class PlaylistRotatorTests
{
    private static DisplayDataSnapshot Snapshot(int products = 3, bool notices = false, bool month = false)
    {
        var list = Enumerable.Range(1, products)
            .Select(i => new ProductDaily($"P{i}", $"P{i}", "#fff", null, 100, 100, 0, 100, ProgressStatus.Met,
                null, null, null, null, ProgressStatus.None, null, null, i))
            .ToList();
        var empty = DisplayDataSnapshot.Empty(DateTimeOffset.Now);
        return empty with
        {
            Products = list,
            Notices = notices ? [new Notice("T", "N", null, 1)] : [],
            Summary = empty.Summary with { MonthTarget = month ? 1000 : 0 },
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
        var snapshot = Snapshot(notices: false, month: false);
        using var rotator = new PlaylistRotator(ViewCatalog.CreateDefault(), () => snapshot, time);
        var seen = new List<string>();
        rotator.ViewChanged += (_, id) => seen.Add(id);

        rotator.Start(Playlist(ViewIds.Ranking, ViewIds.Notice, ViewIds.MonthProgress, ViewIds.Overview), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60));
        time.Advance(TimeSpan.FromSeconds(15));
        time.Advance(TimeSpan.FromSeconds(15));

        Assert.Equal([ViewIds.Ranking, ViewIds.Overview, ViewIds.Ranking], seen);
    }

    [Fact]
    public void Falls_back_to_overview_when_nothing_has_content()
    {
        var time = new FakeTimeProvider();
        var snapshot = Snapshot(products: 0);
        using var rotator = new PlaylistRotator(ViewCatalog.CreateDefault(), () => snapshot, time);

        rotator.Start(Playlist(ViewIds.Notice, ViewIds.MonthProgress), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60));

        Assert.Equal(ViewIds.Overview, rotator.CurrentViewId);
    }

    [Fact]
    public void Paged_view_stays_until_all_pages_shown_capped_by_max()
    {
        var time = new FakeTimeProvider();
        var snapshot = Snapshot(products: 25); // 3 trang × 8 giây = 24 giây
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
