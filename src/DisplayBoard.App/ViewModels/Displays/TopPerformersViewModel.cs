using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 4 — Top 5 nhân viên xuất sắc.</summary>
public sealed partial class TopPerformersViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    public override string ViewId => ViewIds.TopPerformers;
    public override string Title => "TOP 5 NHÂN VIÊN XUẤT SẮC";
    public override string IconKind => "star";

    [ObservableProperty] private IReadOnlyList<EmployeeRow> _top = [];

    protected override void OnUpdate(DisplayDataSnapshot snapshot) =>
        Top = EmployeeRow.From(snapshot.Employees.Where(e => e.Quantity > 0).Take(5), snapshot);
}
