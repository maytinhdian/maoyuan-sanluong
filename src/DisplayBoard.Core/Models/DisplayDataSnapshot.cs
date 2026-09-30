namespace DisplayBoard.Core.Models;

public sealed record EmployeeDaily(
    string EmployeeCode,
    string EmployeeName,
    string Department,
    string? Shift,
    decimal Quantity,
    decimal? Target,
    decimal? CompletionRate,
    bool? IsMet,
    decimal? Shortfall,
    int Rank,
    string? PhotoPath);

public sealed record DepartmentSummary(
    string Name,
    decimal Quantity,
    decimal Target,
    decimal CompletionRate,
    string Color,
    string Icon,
    int Order);

/// <summary>Một mốc giờ trên biểu đồ xu hướng. <see cref="CumulativeQuantity"/> null cho mốc giờ tương lai.</summary>
public sealed record HourlyPoint(
    TimeOnly Hour,
    decimal? CumulativeQuantity,
    decimal CumulativeTarget);

public sealed record Notice(
    string Title,
    string Content,
    string? BackgroundImagePath,
    int Order);

public sealed record ProductionSummary(
    DateOnly Date,
    bool IsToday,
    int EmployeeCount,
    decimal TotalQuantity,
    decimal TotalTarget,
    decimal CompletionRate,
    int MetCount,
    int NotMetCount);

public sealed record DisplayDataSnapshot(
    DateTimeOffset GeneratedAt,
    ProductionSummary Summary,
    IReadOnlyList<ProductionRecord> Records,
    IReadOnlyList<EmployeeDaily> Employees,
    IReadOnlyList<DepartmentSummary> Departments,
    IReadOnlyList<HourlyPoint> Hourly,
    IReadOnlyList<Notice> Notices,
    IReadOnlyList<Slogan> Slogans,
    string Unit,
    string? CompanyName,
    IReadOnlyList<string> Warnings)
{
    public bool HasHourlyData => Hourly.Any(p => p.CumulativeQuantity is > 0);

    public static DisplayDataSnapshot Empty(DateTimeOffset now) => new(
        now,
        new ProductionSummary(DateOnly.FromDateTime(now.LocalDateTime), true, 0, 0, 0, 0, 0, 0),
        [], [], [], [], [], [], "sản phẩm", null, []);
}
