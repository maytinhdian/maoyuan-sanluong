using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 6 — Thông báo. Nhiều thông báo thì lần lượt đổi trong cùng view.</summary>
public sealed partial class NoticeViewModel : DisplayViewModelBase
{
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(8);
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<Notice> _notices = [];
    private int _index;

    public NoticeViewModel(ClockViewModel clock) : base(clock)
    {
        _timer = new DispatcherTimer { Interval = NoticeDuration };
        _timer.Tick += (_, _) => Show(_index + 1);
    }

    public override string ViewId => ViewIds.Notice;
    public override string Title => NoticeTitle;
    public override string IconKind => "megaphone";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] private string _noticeTitle = "THÔNG BÁO";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private string? _backgroundPath;
    [ObservableProperty] private IReadOnlyList<Slogan> _slogans = [];
    [ObservableProperty] private string? _companyName;
    [ObservableProperty] private bool _hasNotice;

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        _notices = snapshot.Notices;
        Slogans = snapshot.Slogans;
        CompanyName = snapshot.CompanyName;
        HasNotice = _notices.Count > 0;
        Show(_index);
    }

    private void Show(int index)
    {
        if (_notices.Count == 0)
        {
            NoticeTitle = "THÔNG BÁO";
            Content = CompanyName ?? "";
            BackgroundPath = null;
            return;
        }
        _index = index % _notices.Count;
        var notice = _notices[_index];
        NoticeTitle = notice.Title.ToUpperInvariant();
        Content = notice.Content;
        BackgroundPath = notice.BackgroundImagePath;
    }

    public override void OnActivated()
    {
        Show(0);
        _timer.Start();
    }

    public override void OnDeactivated() => _timer.Stop();
}
