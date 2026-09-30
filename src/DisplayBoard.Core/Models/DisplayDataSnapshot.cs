namespace DisplayBoard.Core.Models;

public enum ProgressStatus
{
    None,
    Met,
    Near,
    Behind
}

public sealed record ProductDaily(
    string ProductCode,
    string DisplayName,
    string Color,
    string? ImagePath,
    decimal DailyTarget,
    decimal DailyActual,
    decimal DailyVariance,
    decimal? DailyRate,
    ProgressStatus DailyStatus,
    decimal? MonthTarget,
    decimal? MonthCumulative,
    decimal? MonthVariance,
    decimal? MonthRate,
    ProgressStatus MonthStatus,
    decimal? ShiftHours,
    decimal? HourlyTarget,
    int Rank,
    decimal? CarriedShortfall = null);

public sealed record Notice(
    string Title,
    string Content,
    string? BackgroundImagePath,
    int Order);

public sealed record ProductionSummary(
    DateOnly Date,
    bool IsToday,
    int ProductCount,
    decimal DailyTarget,
    decimal DailyActual,
    decimal DailyRate,
    ProgressStatus DailyStatus,
    decimal MonthTarget,
    decimal MonthCumulative,
    decimal MonthRate,
    ProgressStatus MonthStatus,
    int MetCount,
    int NotMetCount,
    DateOnly? CarriedFromDate = null,
    decimal CarriedShortfall = 0,
    int CarriedProductCount = 0)
{
    public bool HasMonthData => MonthTarget > 0;
    public decimal MonthVariance => MonthCumulative - MonthTarget;
}

public sealed record DisplayDataSnapshot(
    DateTimeOffset GeneratedAt,
    string SheetName,
    ProductionSummary Summary,
    IReadOnlyList<ProductDaily> Products,
    IReadOnlyList<Notice> Notices,
    IReadOnlyList<Slogan> Slogans,
    string Unit,
    string? CompanyName,
    IReadOnlyList<string> Warnings)
{
    public static DisplayDataSnapshot Empty(DateTimeOffset now) => new(
        now, "",
        new ProductionSummary(DateOnly.FromDateTime(now.LocalDateTime), true, 0, 0, 0, 0, ProgressStatus.None, 0, 0, 0, ProgressStatus.None, 0, 0),
        [], [], [], "PCS", null, []);
}
