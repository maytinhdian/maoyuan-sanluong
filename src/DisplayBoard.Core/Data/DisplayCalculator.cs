using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Data;

/// <summary>
/// Tính bảng hiển thị TV từ dữ liệu SQLite, đúng như sheet HIEN_THI của file V20 tính bằng công thức.
/// Mỗi cột ghi chú công thức Excel tương ứng; bài test so từng ô với file Excel xuất ra rồi cho LibreOffice tính lại.
/// Quy ước giống Excel: ô "" (trống) là null, N(ô trống) = 0, tỷ lệ đổi sang phần trăm (0.375 → 37.5).
/// </summary>
public static class DisplayCalculator
{
    public const string SheetName = "HIEN_THI";
    public const string TotalLabel = "TỔNG CỘNG";

    /// <summary>Số chuyền sheet HIEN_THI của mẫu V20 hiển thị (dòng 5–10).</summary>
    public const int ExcelLineRows = 6;

    /// <summary>Ngày đang hiển thị (ô M1): ngày chọn tay, không có thì ngày mới nhất có dòng nhập liệu.</summary>
    public static DateOnly? DisplayDate(ProductionData data) =>
        data.DisplayDateOverride ?? (data.Entries.Count == 0 ? null : data.Entries.Max(e => e.Date));

    /// <summary>Các chuyền hiện trên TV: chuyền đang sử dụng, theo thứ tự danh sách.</summary>
    public static IReadOnlyList<LineDef> DisplayLines(ProductionData data) => data.Lines.Where(l => l.Active).ToList();

    public static DisplaySheet Compute(ProductionData data, DateOnly? date = null)
    {
        var day = date ?? DisplayDate(data);
        if (day is null)
            return new DisplaySheet(SheetName, null, [], null, ["Chưa có dữ liệu nhập liệu."]);
        var d = day.Value;

        var lines = DisplayLines(data).Select((line, i) => ComputeLine(data, line, d, 5 + i)).ToList();
        return new DisplaySheet(SheetName, d, lines, Total(lines), [])
        {
            DefectLog = DefectLog(data, d)
        };
    }

    // ---------- Một chuyền (HIEN_THI dòng 5..10) ----------

    private static LineRecord ComputeLine(ProductionData data, LineDef line, DateOnly d, int rowNumber)
    {
        // AJ: dòng NHAP_LIEU của chuyền trong ngày.
        var row = Entry(data, d, line.Code);
        var hours = row?.Hours ?? new decimal?[DayEntry.MaxHours];

        var product = Blank(row?.ProductCode);
        var shiftCode = Blank(row?.ShiftCode);
        var shiftHours = ShiftHours(data, shiftCode);                                   // E = INDEX(CAU_HINH_CA!C, MATCH(D))
        var hourlyTarget = row?.HourlyTarget;                                           // F
        decimal? dailyTarget = shiftHours is null || hourlyTarget is null
            ? null : Round(shiftHours.Value * hourlyTarget.Value);                      // G = ROUND(E*F,0)
        var entered = hours.Count(h => h is not null);
        decimal? actual = entered == 0 ? null : hours.Sum(h => h ?? 0);                 // H = IF(COUNT=0,"",SUM)
        decimal? hoursEntered = row is null ? null : entered;                           // L = IF(AJ="","",COUNT)
        decimal? targetToNow = hoursEntered is null || hourlyTarget is null || dailyTarget is null
            ? null : Math.Min(dailyTarget.Value, hoursEntered.Value * hourlyTarget.Value); // M = MIN(G, L*F)

        // O: ngày làm trước của chuyền; AK: dòng của ngày đó; P: thiếu hôm trước.
        var previousDay = data.Entries.Where(e => Same(e.LineCode, line.Code) && e.Date < d).Select(e => (DateOnly?)e.Date).Max();
        decimal? carried = null;
        if (previousDay is not null && Entry(data, previousDay.Value, line.Code) is { } prev)
        {
            // IF(COUNT(giờ)=0, 0, MAX(0, IFERROR(ROUND(giờ ca × mục tiêu giờ, 0), 0) − SUM(giờ)))
            var prevHours = ShiftHours(data, Blank(prev.ShiftCode));
            var prevTarget = prevHours is null || prev.HourlyTarget is null ? 0 : Round(prevHours.Value * prev.HourlyTarget.Value);
            carried = prev.HasHours ? Math.Max(0, prevTarget - prev.HoursTotal) : 0;
        }

        var monthStart = new DateOnly(d.Year, d.Month, 1);
        decimal? monthTarget = null, monthCumulative = null, previousMonthShortfall = null, neededPerDay = null;
        var workingDaysLeft = WorkingDays(data.Calendar, d, monthStart.AddMonths(1).AddDays(-1));       // AM
        if (product is not null)
        {
            monthTarget = MonthTargetSum(data, product, monthStart);                                  // Q = SUMIFS
            monthCumulative = ProductOutput(data, product, monthStart, d);                            // R = SUMPRODUCT(... ≤ ngày)
            previousMonthShortfall = data.MonthTargets
                .Where(t => Same(t.ProductCode, product) && SameMonth(t.Month, monthStart))
                .Sum(t => PreviousMonthShortfall(data, t));                                           // AL = SUMIFS(MUC_TIEU_THANG!K)
            if (monthTarget != 0 && workingDaysLeft != 0)                                             // AN = ROUNDUP(MAX(0,Q−lũy kế trước ngày)/AM,0)
                neededPerDay = Math.Ceiling(Math.Max(0, monthTarget.Value - ProductOutput(data, product, monthStart, d.AddDays(-1))) / workingDaysLeft);
        }

        var defects = data.Defects.Where(x => x.Date == d && Same(x.LineCode, line.Code)).ToList();
        decimal? defectCount = defects.Count == 0 ? null : defects.Sum(x => x.Quantity ?? 0);         // AQ

        return new LineRecord
        {
            RowNumber = rowNumber,
            Line = line.Name,
            ProductCode = product,
            ShiftCode = shiftCode,
            ShiftHours = shiftHours,
            HourlyTarget = hourlyTarget,
            DailyTarget = dailyTarget,
            DailyActual = actual,
            DailyRate = Percent(actual, dailyTarget),                                                // I = H/G
            DailyVariance = actual is null || dailyTarget is null ? null : actual - dailyTarget,     // J
            Remaining = actual is null || dailyTarget is null ? null : Math.Max(0, dailyTarget.Value - actual.Value), // K
            HoursEntered = hoursEntered,
            TargetToNow = targetToNow,
            HourlyProgress = Percent(actual, targetToNow),                                           // N = H/M
            PreviousDay = previousDay,
            CarriedShortfall = carried,
            MonthTarget = monthTarget,
            MonthCumulative = monthCumulative,
            MonthRate = monthCumulative is null ? null : Percent(monthCumulative, monthTarget),       // S = R/Q
            MonthRemaining = monthCumulative is null || (monthTarget ?? 0) == 0 ? null : Math.Max(0, monthTarget!.Value - monthCumulative.Value), // T
            LineMonthCumulative = data.Entries
                .Where(e => Same(e.LineCode, line.Code) && e.Date >= monthStart && e.Date <= d).Sum(e => e.HoursTotal), // U
            PreviousMonthShortfall = previousMonthShortfall,
            WorkingDaysLeft = workingDaysLeft,
            NeededPerDay = neededPerDay,
            Defects = defectCount,
            DefectRate = Percent(defectCount, actual),                                                // AR = AQ/H
            Hourly = hours.ToList(),
            Status = Blank(row?.Status),
            Note = Blank(row?.Note),
        };
    }

    /// <summary>Dòng TỔNG CỘNG (HIEN_THI dòng 12): cộng các cột số, tỷ lệ tính lại từ tổng.</summary>
    private static LineRecord Total(IReadOnlyList<LineRecord> lines)
    {
        decimal? Sum(Func<LineRecord, decimal?> pick) =>
            lines.Any(l => pick(l) is not null) ? lines.Sum(l => pick(l) ?? 0) : null;   // IF(COUNT=0,"",SUM)

        var target = Sum(l => l.DailyTarget);
        var actual = Sum(l => l.DailyActual);
        var toNow = Sum(l => l.TargetToNow);
        var defects = Sum(l => l.Defects);
        return new LineRecord
        {
            RowNumber = 5 + Math.Max(lines.Count, ExcelLineRows) + 1,
            Line = TotalLabel,
            DailyTarget = target,
            DailyActual = actual,
            DailyRate = Percent(actual, target),
            DailyVariance = Sum(l => l.DailyVariance),
            Remaining = Sum(l => l.Remaining),
            TargetToNow = toNow,
            HourlyProgress = Percent(actual, toNow),
            CarriedShortfall = Sum(l => l.CarriedShortfall),
            LineMonthCumulative = Sum(l => l.LineMonthCumulative),
            Defects = defects,
            DefectRate = Percent(defects, actual),
            Hourly = new decimal?[DayEntry.MaxHours],
        };
    }

    /// <summary>Các dòng HANG_LOI có HIỆN TRÊN TV = "CÓ" (ngày = ngày đang hiển thị), theo thứ tự nhập.</summary>
    private static IReadOnlyList<DefectEntry> DefectLog(ProductionData data, DateOnly d) =>
        data.Defects.Where(x => x.Date == d).OrderBy(x => x.Id).Select(x => new DefectEntry
        {
            Date = x.Date,
            Time = x.Time,
            Line = data.LineName(x.LineCode),
            ProductCode = Blank(Entry(data, x.Date, x.LineCode)?.ProductCode),   // D = mã sản phẩm của dòng NHAP_LIEU cùng ngày + chuyền
            DefectType = Blank(x.DefectType),
            Quantity = x.Quantity,
            ImageFile = Blank(x.ImageFiles),
            Note = Blank(x.Note),
        }).ToList();

    // ---------- Các phép tính dùng chung (cũng là công thức của NHAP_LIEU / MUC_TIEU_THANG) ----------

    public static DayEntry? Entry(ProductionData data, DateOnly date, string lineCode) =>
        data.Entries.FirstOrDefault(e => e.Date == date && Same(e.LineCode, lineCode));

    /// <summary>GIỜ CA của mã ca (CAU_HINH_CA cột TỔNG GIỜ). Không có mã hoặc mã không có trong bảng thì null.</summary>
    public static decimal? ShiftHours(ProductionData data, string? shiftCode) =>
        shiftCode is null ? null : data.Shifts.FirstOrDefault(s => Same(s.Code, shiftCode))?.Hours;

    /// <summary>Mục tiêu trong ngày của một dòng nhập liệu (NHAP_LIEU cột G).</summary>
    public static decimal? DailyTarget(ProductionData data, DayEntry entry) =>
        ShiftHours(data, Blank(entry.ShiftCode)) is { } h && entry.HourlyTarget is { } t ? Round(h * t) : null;

    /// <summary>Tổng sản lượng của mã hàng từ ngày <paramref name="from"/> đến hết ngày <paramref name="to"/>.</summary>
    public static decimal ProductOutput(ProductionData data, string product, DateOnly from, DateOnly to) =>
        data.Entries.Where(e => Same(e.ProductCode, product) && e.Date >= from && e.Date <= to).Sum(e => e.HoursTotal);

    public static decimal MonthTargetSum(ProductionData data, string product, DateOnly month) =>
        data.MonthTargets.Where(t => Same(t.ProductCode, product) && SameMonth(t.Month, month)).Sum(t => t.Target);

    /// <summary>MUC_TIEU_THANG cột H (CÒN THIẾU TRONG THÁNG) = MAX(0, mục tiêu − sản lượng cả tháng của mã hàng).</summary>
    public static decimal MonthShortfall(ProductionData data, MonthTarget target)
    {
        var start = new DateOnly(target.Month.Year, target.Month.Month, 1);
        return Math.Max(0, target.Target - ProductOutput(data, target.ProductCode, start, start.AddMonths(1).AddDays(-1)));
    }

    /// <summary>MUC_TIEU_THANG cột K (THIẾU THÁNG TRƯỚC) = tổng cột H của mã hàng ở tháng trước.</summary>
    public static decimal PreviousMonthShortfall(ProductionData data, MonthTarget target)
    {
        var previous = new DateOnly(target.Month.Year, target.Month.Month, 1).AddMonths(-1);
        return data.MonthTargets
            .Where(t => Same(t.ProductCode, target.ProductCode) && SameMonth(t.Month, previous))
            .Sum(t => MonthShortfall(data, t));
    }

    /// <summary>
    /// Số ngày làm việc từ <paramref name="from"/> đến <paramref name="to"/> (tính cả hai đầu): T2–T7,
    /// trừ ngày thường ghi "Nghỉ", cộng Chủ nhật ghi "Làm bù" trong LICH_LAM_VIEC. Như NETWORKDAYS.INTL(...,11).
    /// </summary>
    public static int WorkingDays(IReadOnlyList<CalendarDay> calendar, DateOnly from, DateOnly to)
    {
        if (to < from)
            return 0;
        var days = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
            if (day.DayOfWeek != DayOfWeek.Sunday)
                days++;
        foreach (var c in calendar)
        {
            if (c.Date < from || c.Date > to)
                continue;
            var sunday = c.Date.DayOfWeek == DayOfWeek.Sunday;
            if (!sunday && string.Equals(c.Kind, CalendarKinds.Off, StringComparison.OrdinalIgnoreCase))
                days--;
            else if (sunday && string.Equals(c.Kind, CalendarKinds.Extra, StringComparison.OrdinalIgnoreCase))
                days++;
        }
        return Math.Max(0, days);
    }

    private static bool SameMonth(DateOnly a, DateOnly b) => a.Year == b.Year && a.Month == b.Month;

    private static bool Same(string? a, string? b) => ProductionData.Same(a, b);

    /// <summary>ROUND(x, 0) của Excel: làm tròn nửa ra xa số 0.</summary>
    public static decimal Round(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    /// <summary>IF(OR(a="",N(b)=0),"",a/b) đổi sang phần trăm.</summary>
    private static decimal? Percent(decimal? value, decimal? of) =>
        value is null || of is null or 0 ? null : value.Value / of.Value * 100;

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
