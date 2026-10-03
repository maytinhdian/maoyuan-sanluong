namespace DisplayBoard.Core.Entry;

/// <summary>Một khung giờ trong ca, tính từ các đợt làm việc ở CAU_HINH_CA (giờ cuối có thể không đủ 60 phút).</summary>
public sealed record HourSlot(int Number, TimeOnly Start, TimeOnly End)
{
    public double Hours => (End - Start).TotalHours;
}

public sealed record ShiftInfo(string Code, string? Name, decimal Hours, IReadOnlyList<HourSlot> Slots);

public sealed record ProductOption(string Code, string? Name);

/// <summary>Kế hoạch của một chuyền trong ngày: cột MÃ SẢN PHẨM, MÃ CA, MỤC TIÊU MỖI GIỜ của NHAP_LIEU.</summary>
public sealed record DayPlan(string ProductCode, string ShiftCode, decimal HourlyTarget);

public sealed record HourValue(int Number, TimeOnly? Start, TimeOnly? End, decimal? Quantity, decimal? Target);

/// <summary>Tình hình một chuyền trong ngày, để trang nhập liệu hiện các giờ đã nhập.</summary>
public sealed record LineDay(
    string Line,
    DateOnly Date,
    bool HasRow,
    DayPlan? Plan,
    DateOnly? PlanCopiedFrom,
    IReadOnlyList<HourValue> Hours,
    decimal Actual,
    decimal? DailyTarget,
    int? SuggestedHour);

public sealed record EntryContext(
    DateOnly Date,
    IReadOnlyList<string> Lines,
    IReadOnlyList<ProductOption> Products,
    IReadOnlyList<ShiftInfo> Shifts,
    IReadOnlyList<string> DefectTypes,
    LineDay? Line);

/// <summary>Một dòng HANG_LOI. ImageFiles: các tên file ảnh cách nhau bởi "; ".</summary>
public sealed record DefectRecord(DateOnly Date, TimeOnly Time, string Line, string DefectType, decimal Quantity, string? ImageFiles, string? Note);
