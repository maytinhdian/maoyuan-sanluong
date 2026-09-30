namespace DisplayBoard.Core.Models;

/// <summary>Một dòng sản phẩm trong file sản lượng của khách (số liệu một ngày).</summary>
public sealed record ProductRecord(
    int RowNumber,
    string ProductCode,
    decimal? ShiftHours,
    decimal? HourlyTarget,
    decimal DailyTarget,
    decimal DailyActual,
    decimal? MonthTarget,
    decimal? MonthCumulative,
    string Line = "");

/// <summary>
/// Kết quả đọc file sản lượng. Mỗi sheet cùng ngày là một chuyền (<see cref="Lines"/>);
/// <see cref="SheetName"/> là tên các sheet đã đọc, nối bằng dấu phẩy.
/// </summary>
public sealed record ProductionSheet(
    string SheetName,
    DateOnly? Date,
    IReadOnlyList<ProductRecord> Records,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Tên chuyền theo thứ tự sheet. Rỗng khi dữ liệu không gắn chuyền.</summary>
    public IReadOnlyList<string> Lines { get; init; } = [];
}
