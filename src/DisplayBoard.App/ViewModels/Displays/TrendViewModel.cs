using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 8 — Xu hướng sản lượng lũy kế trong ngày.</summary>
public sealed partial class TrendViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    public override string ViewId => ViewIds.Trend;
    public override string Title => "XU HƯỚNG SẢN LƯỢNG TRONG NGÀY";
    public override string IconKind => "trend";

    [ObservableProperty] private IReadOnlyList<HourlyPoint> _points = [];
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _totalQuantity = "0";
    [ObservableProperty] private string _totalTarget = "0";
    [ObservableProperty] private string _completionText = "0%";

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        Points = snapshot.Hourly;
        HasData = snapshot.HasHourlyData;
        TotalQuantity = Format.Number(snapshot.Summary.TotalQuantity);
        TotalTarget = Format.Number(snapshot.Summary.TotalTarget);
        CompletionText = $"{snapshot.Summary.CompletionRate:0}%";
    }
}
