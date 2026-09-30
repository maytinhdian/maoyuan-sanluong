using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 7 — Bảng chi tiết giống file khách, tự lật trang thay cho thanh cuộn.</summary>
public sealed partial class DetailViewModel : DisplayViewModelBase
{
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<ProductRow> _all = [];

    public DetailViewModel(ClockViewModel clock) : base(clock)
    {
        _timer = new DispatcherTimer { Interval = DetailViewDefinition.PageDuration };
        _timer.Tick += (_, _) => ShowPage(Page + 1);
    }

    public override string ViewId => ViewIds.Detail;
    public override string Title => "CHI TIẾT SẢN LƯỢNG";
    public override string IconKind => "list";

    [ObservableProperty] private IReadOnlyList<ProductRow> _rows = [];
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private string? _pageText;

    // Dòng tổng
    [ObservableProperty] private string _totalDailyTarget = "";
    [ObservableProperty] private string _totalDailyActual = "";
    [ObservableProperty] private string _totalDailyRate = "";
    [ObservableProperty] private string? _totalCarried;
    [ObservableProperty] private string _totalMonthTarget = "";
    [ObservableProperty] private string _totalMonthCumulative = "";
    [ObservableProperty] private string _totalMonthRemaining = "";
    [ObservableProperty] private string _totalMonthRate = "";

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        _all = ProductRow.From(snapshot.Products);
        PageCount = DetailViewDefinition.PageCount(snapshot);
        var s = snapshot.Summary;
        TotalDailyTarget = Format.Number(s.DailyTarget);
        TotalDailyActual = Format.Number(s.DailyActual);
        TotalDailyRate = Format.Percent(s.DailyRate);
        TotalCarried = s.CarriedShortfall > 0 ? Format.Number(s.CarriedShortfall) : null;
        // Dòng TỔNG CỘNG của Excel không cộng số tháng theo mã hàng, chỉ có lũy kế tháng của các chuyền.
        TotalMonthTarget = "—";
        TotalMonthCumulative = Format.Number(s.LineMonthCumulative);
        TotalMonthRemaining = "—";
        TotalMonthRate = "—";
        ShowPage(Page);
    }

    private void ShowPage(int page)
    {
        Page = page > PageCount || page < 1 ? 1 : page;
        Rows = _all.Skip((Page - 1) * DetailViewDefinition.RowsPerPage).Take(DetailViewDefinition.RowsPerPage).ToList();
        PageText = PageCount > 1 ? $"Trang {Page}/{PageCount}" : null;
    }

    public override void OnActivated()
    {
        ShowPage(1);
        _timer.Start();
    }

    public override void OnDeactivated() => _timer.Stop();
}
