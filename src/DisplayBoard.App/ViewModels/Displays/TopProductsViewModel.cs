using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 4 — Sản phẩm vượt mục tiêu (thay cho Top 5 nhân viên vì file khách không có nhân viên).</summary>
public sealed partial class TopProductsViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    public override string ViewId => ViewIds.TopProducts;
    public override string Title => AnyMet ? "SẢN PHẨM VƯỢT MỤC TIÊU" : "SẢN PHẨM DẪN ĐẦU";
    public override string IconKind => "star";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] private bool _anyMet;
    [ObservableProperty] private IReadOnlyList<ProductRow> _top = [];

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        AnyMet = snapshot.Products.Any(p => p.DailyStatus == ProgressStatus.Met);
        Top = ProductRow.From(snapshot.Products.Where(p => p.DailyRate is not null).OrderBy(p => p.Rank).Take(5));
    }
}
