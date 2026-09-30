using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>Một dòng sản phẩm đã định dạng sẵn để hiển thị.</summary>
public sealed class ProductRow(ProductDaily p, int index, bool showLine = false)
{
    public int Index { get; } = index;
    public bool IsAlternate => Index % 2 == 0;
    public string Rank { get; } = p.Rank.ToString();
    public bool IsTop3 => p.Rank <= 3;
    public string Code { get; } = p.ProductCode;
    public string Name { get; } = p.DisplayName;
    public string Line { get; } = p.Line;
    public bool ShowLine { get; } = showLine && p.Line.Length > 0;
    /// <summary>Tên hiển thị trong bảng: có nhiều chuyền thì kèm tên chuyền để phân biệt cùng mã ở 2 chuyền.</summary>
    public string Title => ShowLine ? $"{Line} · {Name}" : Name;
    public string Color { get; } = p.Color;
    public string? ImagePath { get; } = p.ImagePath;

    public string ShiftHours { get; } = Format.Number(p.ShiftHours);
    public string HourlyTarget { get; } = Format.Number(p.HourlyTarget);
    public string DailyTarget { get; } = Format.Number(p.DailyTarget);
    public string DailyActual { get; } = Format.Number(p.DailyActual);
    public string DailyRate { get; } = Format.Percent(p.DailyRate);
    public string DailyVariance { get; } = Format.Signed(p.DailyTarget > 0 ? p.DailyVariance : null);
    public bool DailyVarianceNegative => p.DailyTarget > 0 && p.DailyVariance < 0;
    public ProgressStatus DailyStatus { get; } = p.DailyStatus;
    public string DailyStatusText => StatusText(DailyStatus);
    public double DailyFraction { get; } = Fraction(p.DailyActual, p.DailyTarget);

    /// <summary>Phần còn thiếu của ngày làm việc trước (app tự lưu), rỗng khi không thiếu.</summary>
    public string? CarriedShortfall { get; } = p.CarriedShortfall is > 0 ? Format.Number(p.CarriedShortfall) : null;

    public bool HasMonth { get; } = p.MonthTarget > 0;
    public string MonthTarget { get; } = Format.Number(p.MonthTarget);
    public string MonthCumulative { get; } = Format.Number(p.MonthCumulative);
    public string MonthRate { get; } = Format.Percent(p.MonthRate);
    public string MonthVariance { get; } = Format.Signed(p.MonthVariance);
    public bool MonthVarianceNegative => p.MonthVariance < 0;
    public ProgressStatus MonthStatus { get; } = p.MonthStatus;
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

    public static IReadOnlyList<ProductRow> From(IEnumerable<ProductDaily> products, bool showLine = false) =>
        products.Select((p, i) => new ProductRow(p, i + 1, showLine)).ToList();
}
