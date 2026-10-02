using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 8 — Tiến độ tháng: lũy kế so với mục tiêu tháng (thay cho xu hướng theo giờ).</summary>
public sealed partial class MonthProgressViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    public const int MaxRows = 10;

    public override string ViewId => ViewIds.MonthProgress;
    public override string Title => "TIẾN ĐỘ THÁNG";
    public override string IconKind => "trend";

    [ObservableProperty] private string _cumulative = "0";
    [ObservableProperty] private string _unit = "PCS";
    [ObservableProperty] private IReadOnlyList<ProductRow> _rows = [];
    [ObservableProperty] private string? _moreText;
    /// <summary>Số ngày làm việc còn lại (V19); null với file V18.</summary>
    [ObservableProperty] private string? _daysLeft;
    /// <summary>Cột "Cần mỗi ngày": rộng 220 khi file có số (V19), ẩn với file V18.</summary>
    [ObservableProperty] private System.Windows.GridLength _neededWidth = new(0);

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        // Dòng TỔNG CỘNG không cộng mục tiêu tháng (mã hàng có thể trùng giữa các chuyền),
        // nên thẻ tổng chỉ hiện lũy kế tháng của các chuyền.
        Cumulative = Format.Number(snapshot.Summary.LineMonthCumulative);
        Unit = snapshot.Unit;

        // Sản phẩm đang thiếu nhiều nhất nằm trên.
        var withMonth = snapshot.Products.Where(p => p.MonthTarget > 0).OrderBy(p => p.MonthRate).ToList();
        var shown = withMonth.Count > MaxRows ? MaxRows - 1 : withMonth.Count;
        Rows = ProductRow.From(withMonth.Take(shown));
        MoreText = withMonth.Count > shown ? $"+{withMonth.Count - shown} chuyền khác" : null;
        DaysLeft = snapshot.Summary.WorkingDaysLeft is null ? null : Format.Number(snapshot.Summary.WorkingDaysLeft);
        NeededWidth = new System.Windows.GridLength(withMonth.Any(p => p.NeededPerDay is not null) ? 220 : 0);
    }
}
