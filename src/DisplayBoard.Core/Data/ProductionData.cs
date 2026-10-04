namespace DisplayBoard.Core.Data;

/// <summary>Một ca làm việc (CAU_HINH_CA): tối đa 3 đợt làm, giờ ca = tổng thời gian các đợt.</summary>
public sealed record ShiftDef(string Code, string? Name, IReadOnlyList<WorkPeriod> Periods, bool Active = true)
{
    /// <summary>TỔNG GIỜ, làm tròn 2 số lẻ như công thức Excel.</summary>
    public decimal Hours => Math.Round((decimal)Periods.Sum(p => (p.End - p.Start).TotalHours), 2, MidpointRounding.AwayFromZero);
}

public sealed record WorkPeriod(TimeOnly Start, TimeOnly End);

/// <summary>Một chuyền (DANH_SACH_CHUYEN). Mã không đổi; tên là chữ hiện trên TV và trong file Excel.</summary>
public sealed record LineDef(string Code, string Name, string? Leader = null, bool Active = true);

/// <summary>Một mã sản phẩm (DANH_SACH_SAN_PHAM). Mã luôn là chữ, kể cả mã toàn số như "883".</summary>
public sealed record ProductDef(string Code, string? Name = null, string? Note = null, bool Active = true);

/// <summary>Một dòng của các danh sách chọn: lý do không đạt, loại lỗi.</summary>
public sealed record ListItem(string Name, string? Note = null);

public static class CalendarKinds
{
    /// <summary>Ngày thường (T2–T7) được nghỉ.</summary>
    public const string Off = "Nghỉ";

    /// <summary>Chủ nhật đi làm.</summary>
    public const string Extra = "Làm bù";
}

/// <summary>Một dòng LICH_LAM_VIEC: Chủ nhật mặc định nghỉ, các ngày khác mặc định làm.</summary>
public sealed record CalendarDay(DateOnly Date, string Kind, string? Note = null);

/// <summary>Mục tiêu tháng của một mã sản phẩm (MUC_TIEU_THANG). Month luôn là ngày 1 của tháng.</summary>
public sealed record MonthTarget(DateOnly Month, string ProductCode, decimal Target, string? Note = null);

/// <summary>
/// Một dòng NHAP_LIEU: kế hoạch và sản lượng từng giờ của một chuyền trong một ngày.
/// Chỉ chứa số người nhập; các cột tính (mục tiêu ngày, % đạt...) do <see cref="DisplayCalculator"/> tính.
/// </summary>
public sealed record DayEntry
{
    public const int MaxHours = 12;

    public required DateOnly Date { get; init; }
    public required string LineCode { get; init; }
    public string? ProductCode { get; init; }
    public string? ShiftCode { get; init; }
    public decimal? HourlyTarget { get; init; }

    /// <summary>Sản lượng GIỜ 1..12, luôn đủ 12 phần tử (null = chưa nhập).</summary>
    public IReadOnlyList<decimal?> Hours { get; init; } = new decimal?[MaxHours];

    public string? Status { get; init; }
    public string? Note { get; init; }
    public decimal? Workers { get; init; }
    public string? Reason { get; init; }
    public decimal? DowntimeMinutes { get; init; }

    public bool HasHours => Hours.Any(h => h is not null);
    public decimal HoursTotal => Hours.Sum(h => h ?? 0);
}

/// <summary>Một lần ghi hàng lỗi (HANG_LOI). ImageFiles: các tên file ảnh cách nhau bởi "; ".</summary>
public sealed record DefectRow
{
    public long Id { get; init; }
    public required DateOnly Date { get; init; }
    public TimeOnly? Time { get; init; }
    public required string LineCode { get; init; }
    public string? DefectType { get; init; }
    public decimal? Quantity { get; init; }
    public string? ImageFiles { get; init; }
    public string? Note { get; init; }
}

/// <summary>Toàn bộ dữ liệu sản xuất tại một thời điểm. Dữ liệu nhỏ (vài nghìn dòng/năm) nên giữ hết trong bộ nhớ.</summary>
public sealed record ProductionData
{
    public IReadOnlyList<ShiftDef> Shifts { get; init; } = [];
    public IReadOnlyList<LineDef> Lines { get; init; } = [];
    public IReadOnlyList<ProductDef> Products { get; init; } = [];
    public IReadOnlyList<ListItem> Reasons { get; init; } = [];
    public IReadOnlyList<ListItem> DefectTypes { get; init; } = [];
    public IReadOnlyList<CalendarDay> Calendar { get; init; } = [];
    public IReadOnlyList<MonthTarget> MonthTargets { get; init; } = [];
    public IReadOnlyList<DayEntry> Entries { get; init; } = [];
    public IReadOnlyList<DefectRow> Defects { get; init; } = [];

    /// <summary>Ngày chọn tay để TV hiện (ô J1 của HIEN_THI). Null = ngày mới nhất có dữ liệu.</summary>
    public DateOnly? DisplayDateOverride { get; init; }

    public static ProductionData Empty { get; } = new();

    public LineDef? FindLine(string? codeOrName) =>
        codeOrName is null ? null
        : Lines.FirstOrDefault(l => Same(l.Code, codeOrName)) ?? Lines.FirstOrDefault(l => Same(l.Name, codeOrName));

    public string LineName(string code) => Lines.FirstOrDefault(l => Same(l.Code, code))?.Name ?? code;

    /// <summary>So sánh mã/tên như Excel: không phân biệt hoa thường, bỏ khoảng trắng hai đầu.</summary>
    public static bool Same(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
