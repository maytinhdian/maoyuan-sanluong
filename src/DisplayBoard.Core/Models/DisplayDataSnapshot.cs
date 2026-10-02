namespace DisplayBoard.Core.Models;

public enum ProgressStatus
{
    None,
    Met,
    Near,
    Behind
}

/// <summary>Một chuyền (chạy một mã sản phẩm) đã sẵn sàng hiển thị. Số lấy nguyên từ Excel.</summary>
public sealed record ProductDaily
{
    public required string Line { get; init; }
    public required string ProductCode { get; init; }
    public required string DisplayName { get; init; }
    public required string Color { get; init; }
    public string? ImagePath { get; init; }
    public string? ShiftCode { get; init; }
    public decimal? ShiftHours { get; init; }
    public decimal? HourlyTarget { get; init; }
    public decimal DailyTarget { get; init; }
    public decimal DailyActual { get; init; }
    public bool HasActual { get; init; }
    public decimal? DailyVariance { get; init; }
    public decimal? DailyRate { get; init; }
    public ProgressStatus DailyStatus { get; init; }
    public decimal? Remaining { get; init; }
    public decimal? HoursEntered { get; init; }
    public decimal? TargetToNow { get; init; }
    public decimal? HourlyProgress { get; init; }
    public ProgressStatus HourlyStatus { get; init; }
    public DateOnly? PreviousDay { get; init; }
    public decimal? CarriedShortfall { get; init; }
    public decimal? MonthTarget { get; init; }
    public decimal? MonthCumulative { get; init; }
    public decimal? MonthRate { get; init; }
    public ProgressStatus MonthStatus { get; init; }
    public decimal? MonthRemaining { get; init; }
    public decimal? LineMonthCumulative { get; init; }
    /// <summary>Thiếu tháng trước của mã hàng (V19, chỉ hiển thị, không cộng vào mục tiêu).</summary>
    public decimal? PreviousMonthShortfall { get; init; }
    /// <summary>Ngày làm việc còn lại trong tháng, tính cả ngày đang hiển thị (V19).</summary>
    public decimal? WorkingDaysLeft { get; init; }
    /// <summary>Cần làm mỗi ngày để kịp mục tiêu tháng (V19).</summary>
    public decimal? NeededPerDay { get; init; }
    /// <summary>Số hàng lỗi trong ngày (V19, null = chưa nhập).</summary>
    public decimal? Defects { get; init; }
    /// <summary>Tỷ lệ lỗi % do Excel tính (V19).</summary>
    public decimal? DefectRate { get; init; }
    public ProgressStatus DefectStatus { get; init; }
    public IReadOnlyList<decimal?> Hourly { get; init; } = [];
    public string? StatusText { get; init; }
    public string? Note { get; init; }
    /// <summary>Thứ hạng theo % đạt trong ngày (1 = cao nhất).</summary>
    public int Rank { get; init; }
}

public sealed record Notice(
    string Title,
    string Content,
    string? BackgroundImagePath,
    int Order);

/// <summary>Dòng TỔNG CỘNG của sheet HIEN_THI.</summary>
public sealed record ProductionSummary
{
    public DateOnly Date { get; init; }
    public bool IsToday { get; init; }
    public int ProductCount { get; init; }
    public decimal DailyTarget { get; init; }
    public decimal DailyActual { get; init; }
    public decimal? DailyRate { get; init; }
    public ProgressStatus DailyStatus { get; init; }
    public decimal? DailyVariance { get; init; }
    public decimal? Remaining { get; init; }
    public decimal? TargetToNow { get; init; }
    public decimal? HourlyProgress { get; init; }
    public DateOnly? CarriedFromDate { get; init; }
    public decimal CarriedShortfall { get; init; }
    public int CarriedProductCount { get; init; }
    public decimal? LineMonthCumulative { get; init; }
    /// <summary>Ngày làm việc còn lại trong tháng (V19), lấy từ chuyền đầu tiên có số.</summary>
    public decimal? WorkingDaysLeft { get; init; }
    /// <summary>Tổng số lỗi và tỷ lệ lỗi chung ở dòng TỔNG CỘNG (V19).</summary>
    public decimal? Defects { get; init; }
    public decimal? DefectRate { get; init; }
    public ProgressStatus DefectStatus { get; init; }
    public int MetCount { get; init; }
    public int NotMetCount { get; init; }

    public static ProductionSummary Empty(DateOnly date, bool isToday) => new() { Date = date, IsToday = isToday };
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
    /// <summary>Tên chuyền theo thứ tự trong sheet.</summary>
    public IReadOnlyList<string> Lines => Products.Select(p => p.Line).Distinct().ToList();

    public bool HasMultipleLines => Products.Select(p => p.Line).Distinct().Skip(1).Any();

    public bool HasMonthData => Products.Any(p => p.MonthTarget > 0);

    public bool HasDefectData => Products.Any(p => p.Defects is not null);

    public bool HasHourlyData => Products.Any(p => p.Hourly.Any(h => h is not null));

    public static DisplayDataSnapshot Empty(DateTimeOffset now) => new(
        now, "",
        ProductionSummary.Empty(DateOnly.FromDateTime(now.LocalDateTime), true),
        [], [], [], "PCS", null, []);
}
