namespace DisplayBoard.Core.Models;

/// <summary>
/// Một dòng của bảng tblHienThi (sheet HIEN_THI): một chuyền trong ngày đang hiển thị.
/// Mọi số là giá trị Excel đã tính sẵn; app không tự tính lại. Tỷ lệ đã đổi sang phần trăm (0.375 → 37.5).
/// </summary>
public sealed record LineRecord
{
    public int RowNumber { get; init; }
    public string Line { get; init; } = "";
    public string? ProductCode { get; init; }
    public string? ShiftCode { get; init; }
    public decimal? ShiftHours { get; init; }
    public decimal? HourlyTarget { get; init; }
    public decimal? DailyTarget { get; init; }
    public decimal? DailyActual { get; init; }
    public decimal? DailyRate { get; init; }
    public decimal? DailyVariance { get; init; }
    public decimal? Remaining { get; init; }
    public decimal? HoursEntered { get; init; }
    public decimal? TargetToNow { get; init; }
    public decimal? HourlyProgress { get; init; }
    public DateOnly? PreviousDay { get; init; }
    public decimal? CarriedShortfall { get; init; }
    public decimal? MonthTarget { get; init; }
    public decimal? MonthCumulative { get; init; }
    public decimal? MonthRate { get; init; }
    public decimal? MonthRemaining { get; init; }
    public decimal? LineMonthCumulative { get; init; }
    /// <summary>Thiếu tháng trước của mã hàng (V19, chỉ hiển thị).</summary>
    public decimal? PreviousMonthShortfall { get; init; }
    /// <summary>Số ngày làm việc còn lại trong tháng, tính cả ngày đang hiển thị (V19).</summary>
    public decimal? WorkingDaysLeft { get; init; }
    /// <summary>Cần làm mỗi ngày để kịp mục tiêu tháng (V19).</summary>
    public decimal? NeededPerDay { get; init; }
    /// <summary>Sản lượng giờ 1..12 (null = chưa nhập).</summary>
    public IReadOnlyList<decimal?> Hourly { get; init; } = [];
    public string? Status { get; init; }
    public string? Note { get; init; }
}

/// <summary>Nội dung sheet HIEN_THI: ngày đang hiển thị, các chuyền và dòng TỔNG CỘNG.</summary>
public sealed record DisplaySheet(
    string SheetName,
    DateOnly? Date,
    IReadOnlyList<LineRecord> Lines,
    LineRecord? Total,
    IReadOnlyList<string> Warnings);
