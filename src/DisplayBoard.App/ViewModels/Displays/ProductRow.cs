using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Một dòng sản phẩm đã định dạng sẵn để hiển thị.</summary>
public sealed class ProductRow(ProductDaily p, int index)
{
    public int Index { get; } = index;
    public bool IsAlternate => Index % 2 == 0;
    public string Rank { get; } = p.Rank.ToString();
    public bool IsTop3 => p.Rank <= 3;
    public string Code { get; } = p.ProductCode;
    public string Name { get; } = p.DisplayName;
    public string Line { get; } = p.Line;
    public bool ShowLine { get; } = p.Line.Length > 0;
    /// <summary>Tên hiển thị trong bảng: "Chuyền 1 · ĐAI LƯNG".</summary>
    public string Title => ShowLine ? $"{Line} · {Name}" : Name;
    public string Color { get; } = p.Color;
    public string? ImagePath { get; } = p.ImagePath;

    public string ShiftHours { get; } = Format.Number(p.ShiftHours);
    public string HourlyTarget { get; } = Format.Number(p.HourlyTarget);
    public string DailyTarget { get; } = Format.Number(p.DailyTarget);
    public string DailyActual { get; } = Format.Number(p.DailyActual);
    public string DailyRate { get; } = Format.Percent(p.DailyRate);
    public string DailyVariance { get; } = Format.Signed(p.DailyVariance);
    public bool DailyVarianceNegative => p.DailyTarget > 0 && p.DailyVariance < 0;
    public ProgressStatus DailyStatus { get; } = p.DailyStatus;
    public string DailyStatusText => StatusText(DailyStatus);
    public double DailyFraction { get; } = Fraction(p.DailyActual, p.DailyTarget);

    public string HoursEntered { get; } = Format.Number(p.HoursEntered);
    public string TargetToNow { get; } = Format.Number(p.TargetToNow);
    public string HourlyProgress { get; } = Format.Percent(p.HourlyProgress);
    public ProgressStatus HourlyStatus { get; } = p.HourlyStatus;
    public string Remaining { get; } = Format.Number(p.Remaining);

    /// <summary>Phần còn thiếu của ngày làm việc trước (cột Excel), rỗng khi không thiếu.</summary>
    public string? CarriedShortfall { get; } = p.CarriedShortfall is > 0 ? Format.Number(p.CarriedShortfall) : null;

    public bool HasMonth { get; } = p.MonthTarget > 0;
    public string MonthTarget { get; } = Format.Number(p.MonthTarget);
    public string MonthCumulative { get; } = Format.Number(p.MonthCumulative);
    public string MonthRate { get; } = Format.Percent(p.MonthRate);
    /// <summary>Còn thiếu so với mục tiêu tháng (cột Excel).</summary>
    public string MonthRemaining { get; } = Format.Number(p.MonthRemaining);
    public bool MonthBehind => p.MonthRemaining > 0;
    public ProgressStatus MonthStatus { get; } = p.MonthStatus;
    /// <summary>Cần làm mỗi ngày để kịp tháng và thiếu tháng trước (cột Excel AN, AL), rỗng khi file không có.</summary>
    public string? NeededPerDay { get; } = p.NeededPerDay is null ? null : Format.Number(p.NeededPerDay);
    public string? PreviousMonthShortfall { get; } = p.PreviousMonthShortfall is > 0 ? Format.Number(p.PreviousMonthShortfall) : null;
    public bool HasMonthPlan => NeededPerDay is not null || PreviousMonthShortfall is not null;
    public double MonthFraction { get; } = Fraction(p.MonthCumulative ?? 0, p.MonthTarget ?? 0);

    public static string StatusText(ProgressStatus status) => status switch
    {
        ProgressStatus.Met => "Đạt",
        ProgressStatus.Near => "Gần đạt",
        ProgressStatus.Behind => "Chậm",
        _ => "—"
    };

    private static double Fraction(decimal actual, decimal target) =>
        target > 0 ? Math.Clamp((double)(actual / target), 0, 1) : 0;

    public static IReadOnlyList<ProductRow> From(IEnumerable<ProductDaily> products) =>
        products.Select((p, i) => new ProductRow(p, i + 1)).ToList();
}
