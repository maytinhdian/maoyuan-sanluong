using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

public sealed record DepartmentCard(
    string Name, string Icon, string Color,
    string Quantity, string Target, string Rate,
    double ProgressFraction, bool IsMet);

/// <summary>Template 3 — Tiến độ theo bộ phận.</summary>
public sealed partial class DepartmentProgressViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    public override string ViewId => ViewIds.DepartmentProgress;
    public override string Title => "TIẾN ĐỘ THỰC HIỆN";
    public override string IconKind => "factory";

    [ObservableProperty] private IReadOnlyList<DepartmentCard> _cards = [];
    [ObservableProperty] private int _columns = 2;
    [ObservableProperty] private int _rows = 2;

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        var departments = snapshot.Departments.Take(6).ToList();
        Cards = departments
            .Select(d => new DepartmentCard(
                d.Name.ToUpperInvariant(), d.Icon, d.Color,
                Format.Number(d.Quantity), Format.Number(d.Target), $"{d.CompletionRate:0}%",
                d.Target > 0 ? Math.Clamp((double)(d.Quantity / d.Target), 0, 1) : 0,
                d.Target > 0 && d.Quantity >= d.Target))
            .ToList();
        (Columns, Rows) = departments.Count switch
        {
            <= 1 => (1, 1),
            2 => (2, 1),
            <= 4 => (2, 2),
            _ => (3, 2)
        };
    }
}
