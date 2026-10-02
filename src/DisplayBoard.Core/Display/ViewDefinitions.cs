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
        new ViewDefinition(ViewIds.Defects, "Hàng lỗi", s => s.HasDefectData),
    ];
}
