using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Một thẻ chuyền đã định dạng sẵn (số lấy từ sheet HIEN_THI).</summary>
public sealed class LineCard(ProductDaily p)
{
    public string Name { get; } = p.Line;
    public string Product { get; } = p.DisplayName;
    public string DailyActual { get; } = Format.Number(p.DailyActual);
    public string DailyTarget { get; } = Format.Number(p.DailyTarget);
    public string DailyRate { get; } = Format.Percent(p.DailyRate);
    public ProgressStatus DailyStatus { get; } = p.DailyStatus;
    public double DailyFraction { get; } = p.DailyTarget > 0 ? Math.Clamp((double)(p.DailyActual / p.DailyTarget), 0, 1) : 0;
    public string HourlyText { get; } = p.HourlyProgress is null ? "" : $"Theo giờ: {Format.Percent(p.HourlyProgress)}";
    public bool HasMonth { get; } = p.MonthTarget > 0;
    public string MonthRate { get; } = Format.Percent(p.MonthRate);
    public ProgressStatus MonthStatus { get; } = p.MonthStatus;
    public string? CarriedShortfall { get; } = p.CarriedShortfall > 0 ? Format.Number(p.CarriedShortfall) : null;
}

/// <summary>So sánh các chuyền (mỗi dòng trong sheet HIEN_THI là một chuyền).</summary>
public sealed partial class LinesViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    public override string ViewId => ViewIds.Lines;
    public override string Title => "SO SÁNH CÁC CHUYỀN";
    public override string IconKind => "factory";

    [ObservableProperty] private IReadOnlyList<LineCard> _cards = [];
    [ObservableProperty] private int _columns = 3;
    [ObservableProperty] private int _rows = 2;

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        Cards = snapshot.Products.Select(p => new LineCard(p)).ToList();
        Columns = Math.Clamp(Cards.Count, 1, 3);
        Rows = Math.Max(1, (int)Math.Ceiling(Cards.Count / (double)Columns));
    }
}
