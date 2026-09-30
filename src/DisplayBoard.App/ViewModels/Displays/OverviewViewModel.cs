using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

public sealed record DepartmentBar(string Name, string Quantity, string Color, double BarHeight);

/// <summary>Template 1 — Sản lượng hôm nay.</summary>
public sealed partial class OverviewViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    private const double MaxBarHeight = 330;

    public override string ViewId => ViewIds.Overview;
    public override string Title => "SẢN LƯỢNG HÔM NAY";
    public override string IconKind => "clock";

    [ObservableProperty] private string _totalQuantity = "0";
    [ObservableProperty] private string _totalTarget = "0";
    [ObservableProperty] private double _completionRate;
    [ObservableProperty] private string _completionText = "0%";
    [ObservableProperty] private StatusKind _status;
    [ObservableProperty] private string _unit = "sản phẩm";
    [ObservableProperty] private string _metSummary = "";
    [ObservableProperty] private IReadOnlyList<DepartmentBar> _departments = [];

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        var summary = snapshot.Summary;
        TotalQuantity = Format.Number(summary.TotalQuantity);
        TotalTarget = Format.Number(summary.TotalTarget);
        CompletionRate = (double)summary.CompletionRate;
        CompletionText = $"{summary.CompletionRate:0}%";
        Status = summary.TotalTarget <= 0 ? StatusKind.None
            : summary.CompletionRate >= 100 ? StatusKind.Met
            : summary.CompletionRate >= 90 ? StatusKind.Near
            : StatusKind.NotMet;
        Unit = snapshot.Unit;
        MetSummary = $"{summary.EmployeeCount} nhân viên · {summary.MetCount} đạt · {summary.NotMetCount} chưa đạt";

        var max = snapshot.Departments.Select(d => d.Quantity).DefaultIfEmpty(0).Max();
        Departments = snapshot.Departments
            .Select(d => new DepartmentBar(d.Name, Format.Number(d.Quantity), d.Color,
                max > 0 ? Math.Max(6, (double)(d.Quantity / max) * MaxBarHeight) : 6))
            .ToList();
    }
}
