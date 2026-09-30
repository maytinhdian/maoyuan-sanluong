using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Một ô giờ: số nhập ở cột GIỜ n, màu so với mục tiêu mỗi giờ của chuyền.</summary>
public sealed record HourCell(string Text, ProgressStatus Status);

/// <summary>Một dòng chuyền trong bảng theo giờ.</summary>
public sealed record HourlyRow(string Line, string Product, string HourlyTarget, IReadOnlyList<HourCell> Cells,
    string Progress, ProgressStatus ProgressStatus, bool IsAlternate);

/// <summary>Sản lượng từng giờ (cột GIỜ 1–12 của HIEN_THI) cho mỗi chuyền, kèm % tiến độ theo giờ Excel tính.</summary>
public sealed partial class HourlyViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    public const int MaxHours = 12;

    public override string ViewId => ViewIds.Hourly;
    public override string Title => "SẢN LƯỢNG THEO GIỜ";
    public override string IconKind => "clock";

    [ObservableProperty] private IReadOnlyList<string> _hours = [];
    [ObservableProperty] private int _hourCount = 8;
    [ObservableProperty] private IReadOnlyList<HourlyRow> _rows = [];

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        // Số cột giờ: đủ cho ca dài nhất và giờ cuối cùng đã nhập.
        var count = snapshot.Products
            .Select(p => Math.Max((int)Math.Ceiling(p.ShiftHours ?? 0), LastEntered(p.Hourly)))
            .DefaultIfEmpty(0).Max();
        HourCount = Math.Clamp(count == 0 ? 8 : count, 1, MaxHours);
        Hours = Enumerable.Range(1, HourCount).Select(h => $"Giờ {h}").ToList();
        Rows = snapshot.Products
            .Select((p, i) => new HourlyRow(
                p.Line,
                p.DisplayName,
                p.HourlyTarget is > 0 ? $"MT {Format.Number(p.HourlyTarget)}/giờ" : "",
                Enumerable.Range(0, HourCount).Select(h => Cell(p, h)).ToList(),
                Format.Percent(p.HourlyProgress),
                p.HourlyStatus,
                i % 2 == 1))
            .ToList();
    }

    private static int LastEntered(IReadOnlyList<decimal?> hourly)
    {
        for (var i = hourly.Count - 1; i >= 0; i--)
            if (hourly[i] is not null)
                return i + 1;
        return 0;
    }

    // Chỉ tô màu để dễ nhìn; con số vẫn là số nhập trong Excel.
    private static HourCell Cell(ProductDaily p, int hour)
    {
        var value = hour < p.Hourly.Count ? p.Hourly[hour] : null;
        if (value is null)
            return new HourCell("·", ProgressStatus.None);
        var status = p.HourlyTarget is > 0 ? SnapshotBuilder.Status(value / p.HourlyTarget * 100) : ProgressStatus.None;
        return new HourCell(Format.Number(value), status);
    }
}
