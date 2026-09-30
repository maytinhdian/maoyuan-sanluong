using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 5 — Sản phẩm chưa đạt mục tiêu ngày, sắp theo % tăng dần.</summary>
public sealed partial class NotMetViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    public const int MaxRows = 7;

    public override string ViewId => ViewIds.NotMet;
    public override string Title => "SẢN PHẨM CHƯA ĐẠT";
    public override string IconKind => "warning";

    [ObservableProperty] private IReadOnlyList<ProductRow> _rows = [];
    [ObservableProperty] private string? _moreText;
    [ObservableProperty] private string _alertText = "";
    [ObservableProperty] private string? _monthNote;
    [ObservableProperty] private bool _allMet;

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        var notMet = snapshot.Products
            .Where(p => p.DailyStatus is ProgressStatus.Near or ProgressStatus.Behind)
            .OrderBy(p => p.DailyRate)
            .ToList();
        // Chừa 1 dòng cho "+N sản phẩm khác" khi danh sách dài.
        var shown = notMet.Count > MaxRows ? MaxRows - 1 : notMet.Count;
        Rows = ProductRow.From(notMet.Take(shown));
        MoreText = notMet.Count > shown ? $"+{notMet.Count - shown} sản phẩm khác" : null;
        AllMet = notMet.Count == 0;
        AlertText = $"Còn {notMet.Count} sản phẩm chưa đạt mục tiêu hôm nay";

        var behindMonth = snapshot.Products.Count(p => p.MonthVariance < 0);
        MonthNote = behindMonth > 0 ? $"{behindMonth} sản phẩm đang thiếu so với mục tiêu tháng" : null;
    }
}
