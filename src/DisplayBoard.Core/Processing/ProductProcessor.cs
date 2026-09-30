using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Processing;

/// <summary>Tổng hợp sheet sản lượng + nội dung phụ thành snapshot hiển thị. Tự tính %, chênh lệch và trạng thái.</summary>
public sealed class ProductProcessor
{
    private static readonly string[] Palette = ["#2E86DE", "#F39C12", "#27AE60", "#8E44AD", "#E74C3C", "#16A085", "#D35400", "#C2185B", "#5D6D7E"];

    /// <param name="previousDay">Kết quả ngày làm việc trước (từ lịch sử app lưu). Phần còn thiếu của ngày đó được hiện lại hôm nay.</param>
    public DisplayDataSnapshot Build(ProductionSheet sheet, ContentData content, DateTimeOffset now, string? imagesFolder, DayHistory? previousDay = null)
    {
        var images = new ImageResolver(imagesFolder);
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var date = sheet.Date ?? today;
        var warnings = sheet.Warnings.Concat(content.Warnings).ToList();

        var carried = previousDay?.Products
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Shortfall), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        var info = content.Products
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        // Mã trùng trong file: cộng dồn để không mất số liệu, và cảnh báo.
        var grouped = sheet.Records
            .GroupBy(r => r.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                if (g.Count() > 1)
                    warnings.Add($"Mã sản phẩm {g.Key} xuất hiện {g.Count()} lần (dòng {string.Join(", ", g.Select(r => r.RowNumber))}), đã cộng dồn.");
                var first = g.First();
                return first with
                {
                    DailyTarget = g.Sum(r => r.DailyTarget),
                    DailyActual = g.Sum(r => r.DailyActual),
                    MonthTarget = SumNullable(g.Select(r => r.MonthTarget)),
                    MonthCumulative = SumNullable(g.Select(r => r.MonthCumulative)),
                };
            })
            .ToList();

        var products = grouped
            .Select((r, index) =>
            {
                info.TryGetValue(r.ProductCode, out var p);
                var dailyRate = Rate(r.DailyActual, r.DailyTarget);
                var monthRate = r.MonthTarget is > 0 ? Rate(r.MonthCumulative ?? 0, r.MonthTarget.Value) : null;
                return (Order: p?.Order ?? 1000 + index, Product: new ProductDaily(
                    r.ProductCode,
                    string.IsNullOrWhiteSpace(p?.DisplayName) ? r.ProductCode : p!.DisplayName!,
                    string.IsNullOrWhiteSpace(p?.Color) ? Palette[index % Palette.Length] : p!.Color!,
                    images.ResolveProductImage(r.ProductCode, p?.ImageFile),
                    r.DailyTarget,
                    r.DailyActual,
                    r.DailyActual - r.DailyTarget,
                    dailyRate,
                    Status(dailyRate),
                    r.MonthTarget,
                    r.MonthCumulative,
                    r.MonthTarget is null || r.MonthCumulative is null ? null : r.MonthCumulative - r.MonthTarget,
                    monthRate,
                    Status(monthRate),
                    r.ShiftHours,
                    r.HourlyTarget,
                    0,
                    carried.TryGetValue(r.ProductCode, out var shortfall) ? shortfall : null));
            })
            .OrderBy(x => x.Order)
            .Select(x => x.Product)
            .ToList();

        // Hạng theo % ngày giảm dần, rồi thực tế giảm dần, rồi thứ tự trong file.
        var ranks = products
            .Select((p, i) => (p, i))
            .OrderByDescending(x => x.p.DailyRate ?? -1)
            .ThenByDescending(x => x.p.DailyActual)
            .ThenBy(x => x.i)
            .Select((x, rank) => (x.p.ProductCode, Rank: rank + 1))
            .ToDictionary(x => x.ProductCode, x => x.Rank, StringComparer.OrdinalIgnoreCase);
        products = products.Select(p => p with { Rank = ranks[p.ProductCode] }).ToList();

        var withDailyTarget = products.Where(p => p.DailyTarget > 0).ToList();
        var withMonth = products.Where(p => p.MonthTarget > 0).ToList();
        var dailyTarget = withDailyTarget.Sum(p => p.DailyTarget);
        var dailyActual = products.Sum(p => p.DailyActual);
        var monthTarget = withMonth.Sum(p => p.MonthTarget!.Value);
        var monthCumulative = withMonth.Sum(p => p.MonthCumulative ?? 0);
        var dailyRateTotal = Rate(withDailyTarget.Sum(p => p.DailyActual), dailyTarget) ?? 0;
        var monthRateTotal = Rate(monthCumulative, monthTarget) ?? 0;

        var summary = new ProductionSummary(
            date,
            date == today,
            products.Count,
            dailyTarget,
            dailyActual,
            dailyRateTotal,
            dailyTarget > 0 ? Status(dailyRateTotal) : ProgressStatus.None,
            monthTarget,
            monthCumulative,
            monthRateTotal,
            monthTarget > 0 ? Status(monthRateTotal) : ProgressStatus.None,
            products.Count(p => p.DailyStatus == ProgressStatus.Met),
            products.Count(p => p.DailyStatus is ProgressStatus.Near or ProgressStatus.Behind),
            previousDay?.Date,
            carried.Values.Sum(),
            carried.Values.Count(v => v > 0));

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

    /// <summary>Kết quả hôm nay để lưu lịch sử (chỉ sản phẩm có mục tiêu).</summary>
    public static DayHistory ToHistory(DisplayDataSnapshot snapshot) => new(
        snapshot.Summary.Date,
        snapshot.Products
            .Where(p => p.DailyTarget > 0)
            .Select(p => new ProductDayResult(p.ProductCode, p.DailyTarget, p.DailyActual))
            .ToList());

    public static decimal? Rate(decimal actual, decimal target) =>
        target > 0 ? Math.Round(actual / target * 100, 0, MidpointRounding.AwayFromZero) : null;

    public static ProgressStatus Status(decimal? rate) => rate switch
    {
        null => ProgressStatus.None,
        >= 100 => ProgressStatus.Met,
        >= 90 => ProgressStatus.Near,
        _ => ProgressStatus.Behind
    };

    private static decimal? SumNullable(IEnumerable<decimal?> values)
    {
        var list = values.Where(v => v is not null).ToList();
        return list.Count == 0 ? null : list.Sum();
    }
}
