using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Template 2 — Bảng xếp hạng sản phẩm theo % ngày (tối đa 10 dòng).</summary>
public sealed partial class RankingViewModel(ClockViewModel clock) : DisplayViewModelBase(clock)
{
    public const int MaxRows = 10;

    public override string ViewId => ViewIds.Ranking;
    public override string Title => "BẢNG XẾP HẠNG SẢN PHẨM";
    public override string IconKind => "chart";

    [ObservableProperty] private IReadOnlyList<ProductRow> _rows = [];

    protected override void OnUpdate(DisplayDataSnapshot snapshot) =>
        Rows = ProductRow.From(snapshot.Products.OrderBy(p => p.Rank).Take(MaxRows), snapshot.HasMultipleLines);
}
