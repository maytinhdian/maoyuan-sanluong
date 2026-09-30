using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Cột sản lượng một sản phẩm; <see cref="TargetOffset"/> là vị trí vạch mục tiêu tính từ đáy.</summary>
public sealed record ProductBar(string Name, string Actual, string Color, double BarHeight, double TargetOffset, bool HasTarget);

/// <summary>Template 1 — Sản lượng hôm nay.</summary>
public sealed partial class OverviewViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    private const double MaxBarHeight = 300;

    public override string ViewId => ViewIds.Overview;
    public override string Title => "SẢN LƯỢNG HÔM NAY";
    public override string IconKind => "clock";

    [ObservableProperty] private string _dailyActual = "0";
    [ObservableProperty] private string _dailyTarget = "0";
    [ObservableProperty] private double _dailyRate;
    [ObservableProperty] private string _dailyRateText = "0%";
    [ObservableProperty] private ProgressStatus _dailyStatus;
    [ObservableProperty] private string _unit = "PCS";
    [ObservableProperty] private string? _monthText;
    [ObservableProperty] private ProgressStatus _monthStatus;
    [ObservableProperty] private string _metSummary = "";
    [ObservableProperty] private string? _carriedText;
    [ObservableProperty] private string _chartTitle = "SẢN LƯỢNG THEO SẢN PHẨM";
    [ObservableProperty] private IReadOnlyList<ProductBar> _bars = [];

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        var s = snapshot.Summary;
        DailyActual = Format.Number(s.DailyActual);
        DailyTarget = Format.Number(s.DailyTarget);
        DailyRate = (double)s.DailyRate;
        DailyRateText = $"{s.DailyRate:0}%";
        DailyStatus = s.DailyStatus;
        Unit = snapshot.Unit;
        MonthStatus = s.MonthStatus;
        MonthText = s.HasMonthData
            ? $"Tháng: lũy kế {Format.Number(s.MonthCumulative)} / {Format.Number(s.MonthTarget)} {snapshot.Unit} · {s.MonthRate:0}% · chênh lệch {Format.Signed(s.MonthVariance)}"
            : null;
        CarriedText = s.CarriedProductCount > 0
            ? $"Ngày {s.CarriedFromDate:dd/MM} còn thiếu {Format.Number(s.CarriedShortfall)} {snapshot.Unit} ({s.CarriedProductCount} sản phẩm), cần bù"
            : null;
        MetSummary = $"{s.ProductCount} sản phẩm · {s.MetCount} đạt · {s.NotMetCount} chưa đạt";

        // Nhiều chuyền: mỗi cột là một chuyền (6 cột dễ đọc hơn vài chục cột sản phẩm).
        MetSummary = snapshot.HasMultipleLines
            ? $"{snapshot.Lines.Count} chuyền · {s.ProductCount} sản phẩm · {s.MetCount} đạt · {s.NotMetCount} chưa đạt"
            : MetSummary;
        ChartTitle = snapshot.HasMultipleLines ? "SẢN LƯỢNG THEO CHUYỀN" : "SẢN LƯỢNG THEO SẢN PHẨM";
        var items = snapshot.HasMultipleLines
            ? snapshot.Lines.Select(l => (l.Name, l.DailyActual, l.DailyTarget, l.DailyStatus, Color: "#2E86DE")).ToList()
            : snapshot.Products.Select(p => (Name: p.DisplayName, p.DailyActual, p.DailyTarget, p.DailyStatus, p.Color)).ToList();
        var max = items.Select(i => Math.Max(i.DailyActual, i.DailyTarget)).DefaultIfEmpty(0).Max();
        Bars = items
            .Select(i => new ProductBar(
                i.Name,
                Format.Number(i.DailyActual),
                StatusColor(i.DailyStatus, i.Color),
                max > 0 ? Math.Max(6, (double)(i.DailyActual / max) * MaxBarHeight) : 6,
                max > 0 ? (double)(i.DailyTarget / max) * MaxBarHeight : 0,
                i.DailyTarget > 0))
            .ToList();
    }

    private static string StatusColor(ProgressStatus status, string fallback) => status switch
    {
        ProgressStatus.Met => "#22C55E",
        ProgressStatus.Near => "#F5B301",
        ProgressStatus.Behind => "#EF4444",
        _ => fallback
    };
}
