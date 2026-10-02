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
    /// <summary>Thiếu tháng trước của mã hàng (cột AL, chỉ để xem, không cộng vào mục tiêu).</summary>
    public decimal? PreviousMonthShortfall { get; init; }
    /// <summary>Số ngày làm việc còn lại trong tháng, tính cả ngày đang hiển thị (cột AM).</summary>
    public decimal? WorkDaysLeft { get; init; }
    /// <summary>Cần làm mỗi ngày để kịp mục tiêu tháng (cột AN).</summary>
    public decimal? NeededPerDay { get; init; }
    /// <summary>Số hàng lỗi trong ngày (V19, null = chưa nhập).</summary>
    public decimal? Defects { get; init; }
    /// <summary>Tỷ lệ lỗi = số lỗi / thực tế, Excel tính (V19), đơn vị %.</summary>
    public decimal? DefectRate { get; init; }
    /// <summary>Sản lượng giờ 1..12 (null = chưa nhập).</summary>
    public IReadOnlyList<decimal?> Hourly { get; init; } = [];
    public string? Status { get; init; }
    public string? Note { get; init; }
}

/// <summary>Một dòng của sheet HANG_LOI (V20) thuộc ngày đang hiển thị.</summary>
public sealed record DefectEntry
{
    public DateOnly? Date { get; init; }
    public TimeOnly? Time { get; init; }
    public required string Line { get; init; }
    public string? ProductCode { get; init; }
    public string? DefectType { get; init; }
    public decimal? Quantity { get; init; }
    /// <summary>Tên file ảnh QC chụp, nằm trong thư mục ảnh (images\hang_loi).</summary>
    public string? ImageFile { get; init; }
    public string? Note { get; init; }
}

/// <summary>Nội dung sheet HIEN_THI: ngày đang hiển thị, các chuyền và dòng TỔNG CỘNG.</summary>
public sealed record DisplaySheet(
    string SheetName,
    DateOnly? Date,
    IReadOnlyList<LineRecord> Lines,
    LineRecord? Total,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Hàng lỗi kèm ảnh của ngày đang hiển thị (sheet HANG_LOI, V20); file cũ thì rỗng.</summary>
    public IReadOnlyList<DefectEntry> DefectLog { get; init; } = [];
}
