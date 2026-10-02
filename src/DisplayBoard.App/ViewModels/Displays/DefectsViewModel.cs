using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>
/// Hàng lỗi hôm nay. Có ảnh (sheet HANG_LOI, V20): lưới ảnh 3×2, tự lật trang. Chưa có ảnh: bảng số lỗi và
/// tỷ lệ lỗi từng chuyền (HIEN_THI cột SỐ LỖI / TỶ LỆ LỖI).
/// </summary>
public sealed partial class DefectsViewModel : DisplayViewModelBase
{
    public const int MaxRows = 8;
    public const int MaxSideLines = 7;

    private readonly DispatcherTimer _timer;
    private IReadOnlyList<DefectPhotoCard> _allPhotos = [];

    public DefectsViewModel(ClockViewModel clock) : base(clock)
    {
        _timer = new DispatcherTimer { Interval = DefectsViewDefinition.PageDuration };
        _timer.Tick += (_, _) => ShowPage(Page + 1);
    }

    public override string ViewId => ViewIds.Defects;
    public override string Title => "HÀNG LỖI HÔM NAY";
    public override string IconKind => "search";

    [ObservableProperty] private string _totalDefects = "—";
    [ObservableProperty] private ProgressStatus _totalStatus;
    [ObservableProperty] private string _totalRate = "—";
    [ObservableProperty] private string _actualText = "";
    [ObservableProperty] private string _unit = "PCS";
    [ObservableProperty] private string _linesWithDefects = "";
    [ObservableProperty] private IReadOnlyList<DefectRow> _rows = [];
    [ObservableProperty] private string? _moreText;
    [ObservableProperty] private bool _hasPhotos;
    [ObservableProperty] private IReadOnlyList<DefectPhotoCard> _photos = [];
    [ObservableProperty] private IReadOnlyList<DefectRow> _sideLines = [];
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private string _footerText = "";
    [ObservableProperty] private string? _pageText;

    protected override void OnUpdate(DisplayDataSnapshot snapshot)
    {
        var m = snapshot.Summary;
        Unit = snapshot.Unit;
        TotalDefects = Format.Number(m.Defects);
        TotalStatus = m.Defects > 0 ? m.DefectStatus : ProgressStatus.None;
        TotalRate = DefectRow.Rate(m.DefectRate);
        ActualText = $"trên {Format.Number(m.DailyActual)} {snapshot.Unit} thực tế";

        // Chỉ các chuyền có kế hoạch; nhiều lỗi xếp trên, chưa nhập số lỗi xếp cuối.
        var list = snapshot.Products
            .Where(p => p.ProductCode.Length > 0)
            .OrderBy(p => p.Defects is null)
            .ThenByDescending(p => p.Defects)
            .ThenByDescending(p => p.DefectRate)
            .ToList();
        LinesWithDefects = $"{list.Count(p => p.Defects > 0)} chuyền có hàng lỗi";
        var maxRate = list.Max(p => p.DefectRate) ?? 0;
        var shown = list.Count > MaxRows ? MaxRows - 1 : list.Count;
        Rows = list.Take(shown).Select((p, i) => new DefectRow(p, i + 1, maxRate)).ToList();
        MoreText = list.Count > shown ? $"+{list.Count - shown} chuyền khác" : null;

        SideLines = list.Take(MaxSideLines).Select((p, i) => new DefectRow(p, i + 1, maxRate)).ToList();
        _allPhotos = snapshot.DefectPhotos.Select(d => new DefectPhotoCard(d)).ToList();
        HasPhotos = _allPhotos.Count > 0;
        FooterText = $"{_allPhotos.Count} ảnh hàng lỗi hôm nay · khung đỏ là loại lỗi, góc phải là số lượng";
        PageCount = DefectsViewDefinition.PageCount(snapshot);
        ShowPage(Page);
    }

    private void ShowPage(int page)
    {
        Page = page > PageCount || page < 1 ? 1 : page;
        Photos = _allPhotos.Skip((Page - 1) * DefectsViewDefinition.PhotosPerPage).Take(DefectsViewDefinition.PhotosPerPage).ToList();
        PageText = PageCount > 1 ? $"Trang {Page}/{PageCount}" : null;
    }

    public override void OnActivated()
    {
        ShowPage(1);
        _timer.Start();
    }

    public override void OnDeactivated() => _timer.Stop();
}

/// <summary>Một ô ảnh hàng lỗi.</summary>
public sealed class DefectPhotoCard(DefectPhoto d)
{
    public string Title { get; } = d.ProductCode.Length > 0 ? $"{d.Line} · {d.ProductCode}" : d.Line;
    public string Color { get; } = d.Color;
    public string? DefectType { get; } = d.DefectType;
    public string? Quantity { get; } = d.Quantity is null ? null : "×" + Format.Number(d.Quantity);
    public string Time { get; } = d.Time?.ToString("HH:mm") ?? "";
    public string? ImagePath { get; } = d.ImagePath;
    /// <summary>Hiện khi Excel ghi tên file nhưng không tìm thấy ảnh.</summary>
    public string? MissingText { get; } = d.ImagePath is null ? $"Không tìm thấy ảnh\n{d.ImageFile}" : null;
}

public sealed class DefectRow(ProductDaily p, int index, decimal maxRate)
{
    public bool IsAlternate { get; } = index % 2 == 0;
    public string Title { get; } = p.Line.Length > 0 ? $"{p.Line} · {p.DisplayName}" : p.DisplayName;
    public string Color { get; } = p.Color;
    public string DailyActual { get; } = Format.Number(p.DailyActual);
    public bool HasDefects { get; } = p.Defects is not null;
    public string Defects { get; } = p.Defects is null ? "chưa nhập" : Format.Number(p.Defects);
    public string DefectRate { get; } = Rate(p.DefectRate);
    public ProgressStatus DefectStatus { get; } = p.DefectStatus;
    public bool HasRate { get; } = p.DefectRate is not null;
    /// <summary>Độ dài thanh so với chuyền có tỷ lệ lỗi cao nhất (chỉ để nhìn).</summary>
    public double RateFraction { get; } = maxRate > 0 && p.DefectRate is not null ? Math.Clamp((double)(p.DefectRate.Value / maxRate), 0, 1) : 0;

    /// <summary>Tỷ lệ lỗi thường nhỏ nên giữ 2 số lẻ: "0,42%".</summary>
    public static string Rate(decimal? value) => value is null ? "—" : value.Value.ToString("0.##", Format.Vi) + "%";
}
