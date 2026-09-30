using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 2 — Bảng xếp hạng nhân viên (tối đa 10 dòng).</summary>
public sealed partial class RankingViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    public const int MaxRows = 10;

    public override string ViewId => ViewIds.Ranking;
    public override string Title => "BẢNG XẾP HẠNG NHÂN VIÊN";
    public override string IconKind => "chart";

    [ObservableProperty] private IReadOnlyList<EmployeeRow> _rows = [];

    protected override void OnUpdate(DisplayDataSnapshot snapshot) =>
        Rows = EmployeeRow.From(snapshot.Employees.Take(MaxRows), snapshot);
}
