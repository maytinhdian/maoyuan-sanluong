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
    public bool HasContent(DisplayDataSnapshot snapshot) => snapshot.Employees.Count > 0;

    public static int PageCount(DisplayDataSnapshot snapshot) =>
        Math.Max(1, (int)Math.Ceiling(snapshot.Employees.Count / (double)RowsPerPage));

    public TimeSpan? GetRequiredDuration(DisplayDataSnapshot snapshot) => PageDuration * PageCount(snapshot);
}

public static class ViewCatalog
{
    public static IReadOnlyList<IDisplayViewDefinition> CreateDefault() =>
    [
        new ViewDefinition(ViewIds.Overview, "Sản lượng hôm nay", _ => true),
        new ViewDefinition(ViewIds.Ranking, "Bảng xếp hạng nhân viên", s => s.Employees.Count > 0),
        new ViewDefinition(ViewIds.DepartmentProgress, "Tiến độ thực hiện", s => s.Departments.Count > 0),
        new ViewDefinition(ViewIds.TopPerformers, "Top 5 nhân viên xuất sắc", s => s.Employees.Any(e => e.Quantity > 0)),
        // Không ai chưa đạt vẫn hiển thị màn hình chúc mừng.
        new ViewDefinition(ViewIds.NotMet, "Nhân viên chưa đạt", s => s.Employees.Any(e => e.Target > 0)),
        new ViewDefinition(ViewIds.Notice, "Thông báo / Thông điệp", s => s.Notices.Count > 0),
        new DetailViewDefinition(),
        new ViewDefinition(ViewIds.Trend, "Xu hướng sản lượng trong ngày", s => s.HasHourlyData),
    ];
}
