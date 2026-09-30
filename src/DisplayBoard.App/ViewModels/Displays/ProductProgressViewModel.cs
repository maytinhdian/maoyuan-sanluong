using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 3 — Thẻ tiến độ theo sản phẩm (ngày + tháng), tối đa 9 thẻ/trang.</summary>
public sealed partial class ProductProgressViewModel : DisplayViewModelBase
{
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<ProductRow> _all = [];

    public ProductProgressViewModel(ClockViewModel clock) : base(clock)
    {
        _timer = new DispatcherTimer { Interval = ProductProgressViewDefinition.PageDuration };
        _timer.Tick += (_, _) => ShowPage(Page + 1);
    }

    public override string ViewId => ViewIds.ProductProgress;
    public override string Title => "TIẾN ĐỘ TỪNG CHUYỀN";
    public override string IconKind => "factory";

    [ObservableProperty] private IReadOnlyList<ProductRow> _cards = [];
    [ObservableProperty] private int _columns = 3;
    [ObservableProperty] private int _rows = 2;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private string? _pageText;
    [ObservableProperty] private string _unit = "PCS";

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        _all = ProductRow.From(snapshot.Products);
        Unit = snapshot.Unit;
        PageCount = ProductProgressViewDefinition.PageCount(snapshot);
        ShowPage(Page);
    }

    private void ShowPage(int page)
    {
        Page = page > PageCount || page < 1 ? 1 : page;
        Cards = _all.Skip((Page - 1) * ProductProgressViewDefinition.CardsPerPage).Take(ProductProgressViewDefinition.CardsPerPage).ToList();
        (Columns, Rows) = Cards.Count switch
        {
            <= 1 => (1, 1),
            2 => (2, 1),
            <= 4 => (2, 2),
            <= 6 => (3, 2),
            _ => (3, 3)
        };
        PageText = PageCount > 1 ? $"Trang {Page}/{PageCount}" : null;
    }

    public override void OnActivated()
    {
        ShowPage(1);
        _timer.Start();
    }

    public override void OnDeactivated() => _timer.Stop();
}
