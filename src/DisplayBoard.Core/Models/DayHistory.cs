namespace DisplayBoard.Core.Models;

/// <summary>Kết quả cuối ngày của một sản phẩm, lưu lại để hôm sau hiện phần còn thiếu.</summary>
public sealed record ProductDayResult(string ProductCode, decimal Target, decimal Actual, string Line = "")
{
    public decimal Shortfall => Math.Max(0, Target - Actual);
}

public sealed record DayHistory(DateOnly Date, IReadOnlyList<ProductDayResult> Products);
