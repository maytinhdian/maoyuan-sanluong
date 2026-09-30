using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;
using DisplayBoard.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisplayBoard.Tests;

public class DailyHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 30)));

    private static ProductionSheet Sheet(DateOnly date, params (string Code, decimal Target, decimal Actual)[] rows) =>
        new("Sheet1", date, rows.Select((r, i) => new ProductRecord(i + 3, r.Code, 10, r.Target / 10, r.Target, r.Actual, null, null)).ToList(), []);

    private static JsonDailyHistoryStore Store(string path) => new(NullLogger<JsonDailyHistoryStore>.Instance, path);

    [Fact]
    public void Yesterday_shortfall_is_shown_today()
    {
        var yesterday = new DayHistory(new DateOnly(2026, 9, 29),
            [new ProductDayResult("883", 1850, 1700), new ProductDayResult("972", 1850, 2000)]);

        var snapshot = new ProductProcessor().Build(
            Sheet(new DateOnly(2026, 9, 30), ("883", 1850, 0), ("972", 1850, 0), ("959", 1850, 0)),
            ContentData.Empty, Now, null, yesterday);

        Assert.Equal(150m, snapshot.Products.Single(p => p.ProductCode == "883").CarriedShortfall);
        Assert.Equal(0m, snapshot.Products.Single(p => p.ProductCode == "972").CarriedShortfall);   // vượt thì không nợ
        Assert.Null(snapshot.Products.Single(p => p.ProductCode == "959").CarriedShortfall);       // hôm qua không có
        Assert.Equal(new DateOnly(2026, 9, 29), snapshot.Summary.CarriedFromDate);
        Assert.Equal(150m, snapshot.Summary.CarriedShortfall);
        Assert.Equal(1, snapshot.Summary.CarriedProductCount);
    }

    [Fact]
    public void Store_overwrites_same_day_and_returns_latest_earlier_day()
    {
        var path = Path.Combine(Path.GetTempPath(), $"history-{Guid.NewGuid():N}.json");
        try
        {
            var store = Store(path);
            store.Save(new DayHistory(new DateOnly(2026, 9, 26), [new ProductDayResult("A", 100, 10)]));
            store.Save(new DayHistory(new DateOnly(2026, 9, 29), [new ProductDayResult("A", 100, 50)]));
            // Cùng ngày đọc lại lần nữa (khách cập nhật số) → ghi đè.
            store.Save(new DayHistory(new DateOnly(2026, 9, 29), [new ProductDayResult("A", 100, 80)]));

            // Mở lại từ file như khi khởi động lại app; 27–28 nghỉ nên lấy 29.
            var previous = Store(path).GetPreviousDay(new DateOnly(2026, 9, 30));
            Assert.Equal(new DateOnly(2026, 9, 29), previous!.Date);
            Assert.Equal(20m, previous.Products.Single().Shortfall);
            Assert.Null(Store(path).GetPreviousDay(new DateOnly(2026, 9, 26)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_history_file_starts_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"history-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.Null(Store(path).GetPreviousDay(new DateOnly(2026, 9, 30)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
