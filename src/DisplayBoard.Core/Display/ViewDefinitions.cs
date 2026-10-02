using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Display;

/// <summary>Định nghĩa view dùng chung. App chỉ cần đăng ký thêm cặp ViewModel/UserControl theo Id.</summary>
public sealed class ViewDefinition(string id, string name, Func<DisplayDataSnapshot, bool> hasContent) : IDisplayViewDefinition
{
    public string Id => id;
    public string Name => name;
    public bool HasContent(DisplayDataSnapshot snapshot) => hasContent(snapshot);
}

public sealed class DetailViewDefinition : IDisplayViewDefinition
{
    public const int RowsPerPage = 10;
    public static readonly TimeSpan PageDuration = TimeSpan.FromSeconds(8);

    public string Id => ViewIds.Detail;
    public string Name => "Chi tiết sản lượng";
    public bool HasContent(DisplayDataSnapshot snapshot) => snapshot.Products.Count > 0;

    public static int PageCount(DisplayDataSnapshot snapshot) =>
        Math.Max(1, (int)Math.Ceiling(snapshot.Products.Count / (double)RowsPerPage));

    public TimeSpan? GetRequiredDuration(DisplayDataSnapshot snapshot) => PageDuration * PageCount(snapshot);
}

/// <summary>Lưới thẻ sản phẩm: tối đa 9 thẻ/trang, nhiều hơn thì lật trang.</summary>
public sealed class ProductProgressViewDefinition : IDisplayViewDefinition
{
    public const int CardsPerPage = 9;
    public static readonly TimeSpan PageDuration = TimeSpan.FromSeconds(10);

    public string Id => ViewIds.ProductProgress;
    public string Name => "Tiến độ từng chuyền";
    public bool HasContent(DisplayDataSnapshot snapshot) => snapshot.Products.Count > 0;

    public static int PageCount(DisplayDataSnapshot snapshot) =>
        Math.Max(1, (int)Math.Ceiling(snapshot.Products.Count / (double)CardsPerPage));

    public TimeSpan? GetRequiredDuration(DisplayDataSnapshot snapshot) => PageDuration * PageCount(snapshot);
}

/// <summary>
/// Hàng lỗi: lưới ảnh QC chụp, 6 / 4 / 2 ảnh mỗi trang (ít ảnh thì ảnh to hơn), tự lật trang.
/// File chưa có ảnh thì chỉ một trang bảng số lỗi.
/// </summary>
public sealed class DefectsViewDefinition(string id, string name, int photosPerPage) : IDisplayViewDefinition
{
    public static readonly TimeSpan PageDuration = TimeSpan.FromSeconds(10);

    public string Id => id;
    public string Name => name;
    public int PhotosPerPage => photosPerPage;
    public bool HasContent(DisplayDataSnapshot snapshot) => snapshot.HasDefectData;

    public static int PhotosPerPageOf(string viewId) => viewId switch
    {
        ViewIds.Defects2 => 2,
        ViewIds.Defects4 => 4,
        _ => 6
    };

    public static int PageCount(DisplayDataSnapshot snapshot, int photosPerPage) =>
        Math.Max(1, (int)Math.Ceiling(snapshot.DefectPhotos.Count / (double)photosPerPage));

    public TimeSpan? GetRequiredDuration(DisplayDataSnapshot snapshot) => PageDuration * PageCount(snapshot, photosPerPage);
}

public static class ViewCatalog
{
    public static IReadOnlyList<IDisplayViewDefinition> CreateDefault() =>
    [
        new ViewDefinition(ViewIds.Overview, "Sản lượng hôm nay", _ => true),
        new ViewDefinition(ViewIds.Ranking, "Bảng xếp hạng chuyền", s => s.Products.Count > 0),
        new ProductProgressViewDefinition(),
        new ViewDefinition(ViewIds.TopProducts, "Chuyền dẫn đầu", s => s.Products.Any(p => p.DailyRate is not null)),
        // Tất cả đạt vẫn hiển thị màn hình chúc mừng.
        new ViewDefinition(ViewIds.NotMet, "Chuyền chưa đạt", s => s.Products.Any(p => p.DailyRate is not null)),
        new ViewDefinition(ViewIds.Notice, "Thông báo / Thông điệp", s => s.Notices.Count > 0),
        new DetailViewDefinition(),
        new ViewDefinition(ViewIds.MonthProgress, "Tiến độ tháng", s => s.HasMonthData),
        new ViewDefinition(ViewIds.Lines, "So sánh các chuyền", s => s.HasMultipleLines),
        new ViewDefinition(ViewIds.Hourly, "Sản lượng theo giờ", s => s.HasHourlyData),
        new DefectsViewDefinition(ViewIds.Defects, "Hàng lỗi (6 ảnh/trang)", 6),
        new DefectsViewDefinition(ViewIds.Defects4, "Hàng lỗi (4 ảnh lớn/trang)", 4),
        new DefectsViewDefinition(ViewIds.Defects2, "Hàng lỗi (2 ảnh rất lớn/trang)", 2),
    ];
}
