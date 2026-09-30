using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 7 — Bảng chi tiết, tự lật trang thay cho thanh cuộn.</summary>
public sealed partial class DetailViewModel : DisplayViewModelBase
{
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<EmployeeRow> _all = [];

    public DetailViewModel(ClockViewModel clock) : base(clock)
    {
        _timer = new DispatcherTimer { Interval = DetailViewDefinition.PageDuration };
        _timer.Tick += (_, _) => ShowPage(Page + 1);
    }

    public override string ViewId => ViewIds.Detail;
    public override string Title => "CHI TIẾT SẢN LƯỢNG";
    public override string IconKind => "list";

    [ObservableProperty] private IReadOnlyList<EmployeeRow> _rows = [];
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private string? _pageText;
    [ObservableProperty] private bool _hasShift;

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        // Bảng chi tiết sắp theo Mã NV để dễ tra cứu.
        _all = EmployeeRow.From(snapshot.Employees.OrderBy(e => e.EmployeeCode, StringComparer.OrdinalIgnoreCase), snapshot);
        HasShift = snapshot.Employees.Any(e => e.Shift is not null);
        PageCount = DetailViewDefinition.PageCount(snapshot);
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
