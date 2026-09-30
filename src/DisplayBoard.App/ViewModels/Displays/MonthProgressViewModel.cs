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
    [ObservableProperty] private string _target = "0";
    [ObservableProperty] private string _rateText = "0%";
    [ObservableProperty] private double _rate;
    [ObservableProperty] private ProgressStatus _status;
    [ObservableProperty] private string _variance = "0";
    [ObservableProperty] private bool _varianceNegative;
    [ObservableProperty] private string _unit = "PCS";
    [ObservableProperty] private IReadOnlyList<ProductRow> _rows = [];
    [ObservableProperty] private string? _moreText;

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        var s = snapshot.Summary;
        Cumulative = Format.Number(s.MonthCumulative);
        Target = Format.Number(s.MonthTarget);
        Rate = (double)s.MonthRate;
        RateText = $"{s.MonthRate:0}%";
        Status = s.MonthStatus;
        Variance = Format.Signed(s.MonthVariance);
        VarianceNegative = s.MonthVariance < 0;
        Unit = snapshot.Unit;

        // Sản phẩm đang thiếu nhiều nhất nằm trên.
        var withMonth = snapshot.Products.Where(p => p.MonthTarget > 0).OrderBy(p => p.MonthRate).ToList();
        var shown = withMonth.Count > MaxRows ? MaxRows - 1 : withMonth.Count;
        Rows = ProductRow.From(withMonth.Take(shown));
        MoreText = withMonth.Count > shown ? $"+{withMonth.Count - shown} sản phẩm khác" : null;
    }
}
