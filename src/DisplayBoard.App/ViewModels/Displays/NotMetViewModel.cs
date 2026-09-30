using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 5 — Nhân viên chưa đạt, sắp theo % tăng dần.</summary>
public sealed partial class NotMetViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    public const int MaxRows = 7;

    public override string ViewId => ViewIds.NotMet;
    public override string Title => "DANH SÁCH NHÂN VIÊN CHƯA ĐẠT";
    public override string IconKind => "warning";

    [ObservableProperty] private IReadOnlyList<EmployeeRow> _rows = [];
    [ObservableProperty] private string? _moreText;
    [ObservableProperty] private string _alertText = "";
    [ObservableProperty] private bool _allMet;

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        var notMet = snapshot.Employees
            .Where(e => e.IsMet == false)
            .OrderBy(e => e.CompletionRate)
            .ThenBy(e => e.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Chừa 1 dòng cho "+N nhân viên khác" khi danh sách dài.
        var shown = notMet.Count > MaxRows ? MaxRows - 1 : notMet.Count;
        Rows = EmployeeRow.From(notMet.Take(shown), snapshot);
        MoreText = notMet.Count > shown ? $"+{notMet.Count - shown} nhân viên khác" : null;
        AllMet = notMet.Count == 0;
        AlertText = $"Còn {notMet.Count} nhân viên chưa đạt mục tiêu";
    }
}
