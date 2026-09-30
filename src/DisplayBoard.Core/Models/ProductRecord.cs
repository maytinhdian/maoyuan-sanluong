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
    decimal? MonthCumulative);

/// <summary>Kết quả đọc sheet sản lượng đã chọn.</summary>
public sealed record ProductionSheet(
    string SheetName,
    DateOnly? Date,
    IReadOnlyList<ProductRecord> Records,
    IReadOnlyList<string> Warnings);
