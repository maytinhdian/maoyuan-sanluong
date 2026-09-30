using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Processing;

/// <summary>Tổng hợp dữ liệu thô thành snapshot hiển thị. Hàm thuần, không I/O ngoài việc kiểm tra file ảnh tồn tại.</summary>
public sealed class DataProcessor
{
    public static readonly TimeOnly DefaultStart = new(7, 0);
    public static readonly TimeOnly DefaultEnd = new(17, 0);

    private static readonly string[] Palette = ["#2E86DE", "#F39C12", "#27AE60", "#8E44AD", "#E74C3C", "#16A085", "#D35400", "#2C3E50"];
    private static readonly string[] Icons = ["factory", "paint", "box", "search", "gear", "truck", "tool", "star"];

    public DisplayDataSnapshot Build(WorkbookData data, DateTimeOffset now, string? imagesFolder)
    {
        var images = new ImageResolver(imagesFolder);
        var warnings = data.Warnings.ToList();
        var today = DateOnly.FromDateTime(now.LocalDateTime);

        // Hôm nay chưa có dữ liệu thì lấy ngày gần nhất có dữ liệu.
        var date = data.Records.Any(r => r.Date == today)
            ? today
            : data.Records.Count > 0 ? data.Records.Max(r => r.Date) : today;
        var dayRecords = data.Records.Where(r => r.Date == date).ToList();

        var employees = BuildEmployees(dayRecords, data.Employees, images);
        var departments = BuildDepartments(employees, data.Departments);

        var totalQuantity = employees.Sum(e => e.Quantity);
        var totalTarget = employees.Sum(e => e.Target ?? 0);
        var summary = new ProductionSummary(
            date,
            date == today,
            employees.Count,
            totalQuantity,
            totalTarget,
            Rate(totalQuantity, totalTarget),
            employees.Count(e => e.IsMet == true),
            employees.Count(e => e.IsMet == false));

        var (start, end) = ReadWorkingHours(data.Settings, warnings);
        var hourly = BuildHourly(dayRecords, totalTarget, start, end, date == today ? TimeOnly.FromDateTime(now.LocalDateTime) : null);

        var notices = data.Notices
            .Where(n => n.Enabled && (n.FromDate is null || n.FromDate <= today) && (n.ToDate is null || n.ToDate >= today))
            .OrderBy(n => n.Order)
            .Select(n => new Notice(n.Title, n.Content, images.ResolveImage(n.BackgroundImage), n.Order))
            .ToList();

        return new DisplayDataSnapshot(
            now,
            summary,
            data.Records,
            employees,
            departments,
            hourly,
            notices,
            data.Slogans,
            data.Settings.GetValueOrDefault("donvi") ?? "sản phẩm",
            data.Settings.GetValueOrDefault("tencongty"),
            warnings);
    }

    public static decimal Rate(decimal quantity, decimal target) =>
        target > 0 ? Math.Round(quantity / target * 100, 0, MidpointRounding.AwayFromZero) : 0;

    private static List<EmployeeDaily> BuildEmployees(List<ProductionRecord> records, IReadOnlyList<EmployeeInfo> directory, ImageResolver images)
    {
        var info = directory
            .GroupBy(e => e.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var aggregated = records
            .GroupBy(r => r.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var last = g.OrderBy(r => r.RowNumber).Last();
                var quantity = g.Sum(r => r.Quantity);
                // Mục tiêu là mục tiêu cả ngày: lấy giá trị lớn nhất, không cộng dồn giữa các lần nhập.
                var target = g.Where(r => r.Target is not null).Select(r => r.Target).DefaultIfEmpty(null).Max();
                var shifts = g.Select(r => r.Shift).Where(s => s is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                decimal? rate = target > 0 ? Rate(quantity, target.Value) : null;
                bool? met = target > 0 ? quantity >= target : null;
                decimal? shortfall = target > 0 ? quantity - target : null;
                info.TryGetValue(g.Key, out var employee);
                return new EmployeeDaily(
                    last.EmployeeCode,
                    last.EmployeeName,
                    last.Department,
                    shifts.Count == 0 ? null : string.Join("/", shifts),
                    quantity, target, rate, met, shortfall,
                    0,
                    images.ResolveEmployeePhoto(last.EmployeeCode, employee?.PhotoFile));
            })
            .OrderByDescending(e => e.Quantity)
            .ThenByDescending(e => e.CompletionRate ?? 0)
            .ThenBy(e => e.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return aggregated.Select((e, i) => e with { Rank = i + 1 }).ToList();
    }

    private static List<DepartmentSummary> BuildDepartments(List<EmployeeDaily> employees, IReadOnlyList<DepartmentInfo> config)
    {
        var configByName = config
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        // Thứ tự mặc định: theo thứ tự xuất hiện trong BO_PHAN, sau đó theo thứ tự xuất hiện trong dữ liệu.
        var names = config.Select(d => d.Name)
            .Concat(employees.Select(e => e.Department))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => employees.Any(e => string.Equals(e.Department, name, StringComparison.OrdinalIgnoreCase)) || configByName[name].Target > 0)
            .ToList();

        return names
            .Select((name, index) =>
            {
                configByName.TryGetValue(name, out var cfg);
                var members = employees.Where(e => string.Equals(e.Department, name, StringComparison.OrdinalIgnoreCase)).ToList();
                var quantity = members.Sum(e => e.Quantity);
                var target = cfg?.Target ?? members.Sum(e => e.Target ?? 0);
                return new DepartmentSummary(
                    cfg?.Name ?? name,
                    quantity,
                    target,
                    Rate(quantity, target),
                    string.IsNullOrWhiteSpace(cfg?.Color) ? Palette[index % Palette.Length] : cfg!.Color!,
                    string.IsNullOrWhiteSpace(cfg?.Icon) ? Icons[index % Icons.Length] : cfg!.Icon!,
                    cfg?.Order ?? 1000 + index);
            })
            .OrderBy(d => d.Order)
            .ToList();
    }

    private static (TimeOnly Start, TimeOnly End) ReadWorkingHours(IReadOnlyDictionary<string, string> settings, List<string> warnings)
    {
        var start = ParseTime(settings.GetValueOrDefault("giobatdau"), DefaultStart, "GioBatDau", warnings);
        var end = ParseTime(settings.GetValueOrDefault("gioketthuc"), DefaultEnd, "GioKetThuc", warnings);
        if (end <= start)
        {
            warnings.Add("CAU_HINH: GioKetThuc phải sau GioBatDau, dùng mặc định 07:00–17:00.");
            return (DefaultStart, DefaultEnd);
        }
        return (start, end);
    }

    private static TimeOnly ParseTime(string? text, TimeOnly fallback, string key, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        if (TimeOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var time))
            return time;
        warnings.Add($"CAU_HINH: {key} = \"{text}\" không hợp lệ, dùng {fallback:HH:mm}.");
        return fallback;
    }

    /// <summary>Lũy kế theo mốc giờ tròn. Bản ghi 08:20 được tính vào mốc 09:00.</summary>
    public static List<HourlyPoint> BuildHourly(IReadOnlyList<ProductionRecord> dayRecords, decimal totalTarget, TimeOnly start, TimeOnly end, TimeOnly? now)
    {
        var timed = dayRecords.Where(r => r.Time is not null).ToList();
        var points = new List<HourlyPoint>();
        var totalMinutes = (decimal)(end - start).TotalMinutes;

        var startHour = new TimeOnly(start.Hour, 0);
        var endHour = end.Minute == 0 ? end : new TimeOnly(Math.Min(end.Hour + 1, 23), 0);
        for (var hour = startHour; hour <= endHour; hour = hour.AddHours(1))
        {
            var elapsed = Math.Clamp((decimal)(hour - start).TotalMinutes, 0, totalMinutes);
            if (hour < start)
                elapsed = 0;
            var target = totalMinutes > 0 ? Math.Round(totalTarget * elapsed / totalMinutes, 0) : 0;

            // Mốc giờ tương lai (so với giờ hiện tại, làm tròn lên) chưa có số thực hiện.
            decimal? cumulative = now is { } current && hour > RoundUpToHour(current)
                ? null
                : timed.Where(r => RoundUpToHour(r.Time!.Value) <= hour).Sum(r => r.Quantity);
            points.Add(new HourlyPoint(hour, cumulative, target));

            if (hour.Hour == 23)
                break;
        }
        return points;
    }

    private static TimeOnly RoundUpToHour(TimeOnly time)
    {
        if (time.Minute == 0 && time.Second == 0)
            return time;
        return time.Hour == 23 ? new TimeOnly(23, 59) : new TimeOnly(time.Hour + 1, 0);
    }
}
