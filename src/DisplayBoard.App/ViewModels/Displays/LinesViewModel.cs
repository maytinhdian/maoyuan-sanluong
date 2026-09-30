using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Một thẻ chuyền đã định dạng sẵn.</summary>
public sealed class LineCard(LineSummary l)
{
    public string Name { get; } = l.Name;
    public string DailyActual { get; } = Format.Number(l.DailyActual);
    public string DailyTarget { get; } = Format.Number(l.DailyTarget);
    public string DailyRate { get; } = Format.Percent(l.DailyRate);
    public ProgressStatus DailyStatus { get; } = l.DailyStatus;
    public double DailyFraction { get; } = l.DailyTarget > 0 ? Math.Clamp((double)(l.DailyActual / l.DailyTarget), 0, 1) : 0;
    public string MetText { get; } = $"{l.MetCount}/{l.ProductCount} sản phẩm đạt";
    public bool HasMonth { get; } = l.MonthTarget > 0;
    public string MonthRate { get; } = Format.Percent(l.MonthRate);
    public ProgressStatus MonthStatus { get; } = l.MonthStatus;
    public string? CarriedShortfall { get; } = l.CarriedShortfall > 0 ? Format.Number(l.CarriedShortfall) : null;
}

/// <summary>So sánh các chuyền (mỗi sheet trong file khách là một chuyền).</summary>
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
        Cards = snapshot.Lines.Select(l => new LineCard(l)).ToList();
        Columns = Math.Clamp(Cards.Count, 1, 3);
        Rows = Math.Max(1, (int)Math.Ceiling(Cards.Count / (double)Columns));
    }
}
