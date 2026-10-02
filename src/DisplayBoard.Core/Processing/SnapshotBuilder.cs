using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Processing;

/// <summary>
/// Ghép sheet HIEN_THI với nội dung phụ thành snapshot hiển thị. Không tính lại số liệu: mọi con số lấy từ Excel,
/// app chỉ gắn tên/màu/ảnh, xếp hạng theo % Excel đã tính và đổi % sang màu trạng thái.
/// </summary>
public sealed class SnapshotBuilder
{
    private static readonly string[] Palette = ["#2E86DE", "#F39C12", "#27AE60", "#8E44AD", "#E74C3C", "#16A085", "#D35400", "#C2185B", "#5D6D7E"];

    public DisplayDataSnapshot Build(DisplaySheet sheet, ContentData content, DateTimeOffset now, string? imagesFolder)
    {
        var images = new ImageResolver(imagesFolder);
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var date = sheet.Date ?? today;
        var warnings = sheet.Warnings.Concat(content.Warnings).ToList();

        var info = content.Products
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var products = sheet.Lines
            .Select((l, index) =>
            {
                var code = l.ProductCode ?? "";
                info.TryGetValue(code, out var p);
                return new ProductDaily
                {
                    Line = l.Line,
                    ProductCode = code,
                    DisplayName = code.Length == 0 ? "Chưa có kế hoạch"
                        : string.IsNullOrWhiteSpace(p?.DisplayName) ? code : p!.DisplayName!,
                    Color = string.IsNullOrWhiteSpace(p?.Color) ? Palette[index % Palette.Length] : p!.Color!,
                    ImagePath = code.Length == 0 ? null : images.ResolveProductImage(code, p?.ImageFile),
                    ShiftCode = l.ShiftCode,
                    ShiftHours = l.ShiftHours,
                    HourlyTarget = l.HourlyTarget,
                    DailyTarget = l.DailyTarget ?? 0,
                    DailyActual = l.DailyActual ?? 0,
                    HasActual = l.DailyActual is not null,
                    DailyVariance = l.DailyVariance,
                    DailyRate = l.DailyRate,
                    DailyStatus = Status(l.DailyRate),
                    Remaining = l.Remaining,
                    HoursEntered = l.HoursEntered,
                    TargetToNow = l.TargetToNow,
                    HourlyProgress = l.HourlyProgress,
                    HourlyStatus = Status(l.HourlyProgress),
                    PreviousDay = l.PreviousDay,
                    CarriedShortfall = l.CarriedShortfall,
                    MonthTarget = l.MonthTarget,
                    MonthCumulative = l.MonthCumulative,
                    MonthRate = l.MonthRate,
                    MonthStatus = Status(l.MonthRate),
                    MonthRemaining = l.MonthRemaining,
                    LineMonthCumulative = l.LineMonthCumulative,
                    PreviousMonthShortfall = l.PreviousMonthShortfall,
                    WorkDaysLeft = l.WorkDaysLeft,
                    NeededPerDay = l.NeededPerDay,
                    Hourly = l.Hourly,
                    StatusText = l.Status,
                    Note = l.Note,
                };
            })
            .ToList();

        // Hạng theo % ngày (Excel đã tính) giảm dần, rồi thực tế giảm dần, rồi thứ tự chuyền.
        var ranks = products
            .Select((p, i) => (p, i))
            .OrderByDescending(x => x.p.DailyRate ?? -1)
            .ThenByDescending(x => x.p.DailyActual)
            .ThenBy(x => x.i)
            .Select((x, rank) => (x.i, Rank: rank + 1))
            .ToDictionary(x => x.i, x => x.Rank);
        products = products.Select((p, i) => p with { Rank = ranks[i] }).ToList();

        var total = sheet.Total;
        var summary = new ProductionSummary
        {
            Date = date,
            IsToday = date == today,
            ProductCount = products.Count,
            DailyTarget = total?.DailyTarget ?? 0,
            DailyActual = total?.DailyActual ?? 0,
            DailyRate = total?.DailyRate,
            DailyStatus = Status(total?.DailyRate),
            DailyVariance = total?.DailyVariance,
            Remaining = total?.Remaining,
            TargetToNow = total?.TargetToNow,
            HourlyProgress = total?.HourlyProgress,
            CarriedFromDate = products.Select(p => p.PreviousDay).FirstOrDefault(d => d is not null),
            CarriedShortfall = total?.CarriedShortfall ?? 0,
            CarriedProductCount = products.Count(p => p.CarriedShortfall > 0),
            LineMonthCumulative = total?.LineMonthCumulative,
            MetCount = products.Count(p => p.DailyStatus == ProgressStatus.Met),
            NotMetCount = products.Count(p => p.DailyStatus is ProgressStatus.Near or ProgressStatus.Behind),
        };

        var notices = content.Notices
            .Where(n => n.Enabled && (n.FromDate is null || n.FromDate <= today) && (n.ToDate is null || n.ToDate >= today))
            .OrderBy(n => n.Order)
            .Select(n => new Notice(n.Title, n.Content, images.ResolveImage(n.BackgroundImage), n.Order))
            .ToList();

        return new DisplayDataSnapshot(
            now,
            sheet.SheetName,
            summary,
            products,
            notices,
            content.Slogans,
            content.Settings.GetValueOrDefault("donvi") ?? "PCS",
            content.Settings.GetValueOrDefault("tencongty"),
            warnings);
    }

    /// <summary>Đổi % (Excel đã tính) sang màu: Đạt ≥ 100, Gần đạt 90–99, Chậm &lt; 90.</summary>
    public static ProgressStatus Status(decimal? rate) => rate switch
    {
        null => ProgressStatus.None,
        >= 100 => ProgressStatus.Met,
        >= 90 => ProgressStatus.Near,
        _ => ProgressStatus.Behind
    };
}
