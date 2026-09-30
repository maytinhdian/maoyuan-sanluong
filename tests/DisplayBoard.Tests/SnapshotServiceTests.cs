using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;
using DisplayBoard.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisplayBoard.Tests;

public class SnapshotServiceTests
{
    private sealed class FakeConfig(string? path) : IConfigurationService
    {
        public DisplayConfiguration Current { get; } = new() { ExcelFile = path };
        public DisplayConfiguration Load() => Current;
        public void Save(DisplayConfiguration configuration) { }
    }

    private sealed class FakeReader(Queue<Func<ProductionSheet>> results) : IExcelDataReader
    {
        public int Calls { get; private set; }
        public Task<ProductionSheet> ReadProductionAsync(string filePath, string? sheetName, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(results.Dequeue()());
        }

        public Task<ContentData> ReadContentAsync(string filePath, CancellationToken ct) => Task.FromResult(ContentData.Empty);
    }

    private sealed class MemoryHistory : IDailyHistoryStore
    {
        public List<DayHistory> Saved { get; } = [];
        public void Save(DayHistory day) => Saved.Add(day);
        public DayHistory? GetPreviousDay(DateOnly date) => Saved.Where(d => d.Date < date).MaxBy(d => d.Date);
    }

    private static ProductionSheet Data(decimal qty) => new(
        "Sheet2", DateOnly.FromDateTime(DateTime.Today),
        [new ProductRecord(3, "A", 10, 10, 100, qty, null, null)], []);

    private static (SnapshotService Service, FakeReader Reader, string Path) Create(params Func<ProductionSheet>[] results)
    {
        var path = System.IO.Path.GetTempFileName();
        var reader = new FakeReader(new Queue<Func<ProductionSheet>>(results));
        var service = new SnapshotService(reader, new ProductProcessor(), new MemoryHistory(), new FakeConfig(path), TimeProvider.System, NullLogger<SnapshotService>.Instance);
        return (service, reader, path);
    }

    [Fact]
    public async Task Successful_reload_replaces_snapshot_and_notifies()
    {
        var (service, _, path) = Create(() => Data(10));
        DisplayDataSnapshot? notified = null;
        service.SnapshotChanged += (_, s) => notified = s;

        await service.ReloadAsync();

        Assert.Equal(LoadStatus.Updated, service.Status);
        Assert.Same(service.Current, notified);
        Assert.Equal(10m, service.Current!.Summary.DailyActual);
        File.Delete(path);
    }

    [Fact]
    public async Task Invalid_data_keeps_last_good_snapshot()
    {
        var (service, _, path) = Create(() => Data(10), () => throw new ExcelValidationException("thiếu cột"));
        await service.ReloadAsync();
        var good = service.Current;

        await service.ReloadAsync();

        Assert.Equal(LoadStatus.InvalidData, service.Status);
        Assert.Equal("thiếu cột", service.LastError);
        Assert.Same(good, service.Current);
        File.Delete(path);
    }

    [Fact]
    public async Task Locked_file_is_retried_then_succeeds()
    {
        var (service, reader, path) = Create(
            () => throw new IOException("locked"),
            () => throw new IOException("locked"),
            () => Data(20));

        await service.ReloadAsync();

        Assert.Equal(3, reader.Calls);
        Assert.Equal(LoadStatus.Updated, service.Status);
        File.Delete(path);
    }

    [Fact]
    public async Task Locked_file_gives_up_after_max_attempts_and_keeps_snapshot()
    {
        var results = new List<Func<ProductionSheet>> { () => Data(5) };
        results.AddRange(Enumerable.Repeat<Func<ProductionSheet>>(() => throw new IOException("locked"), SnapshotService.MaxAttempts));
        var (service, reader, path) = Create(results.ToArray());
        await service.ReloadAsync();

        await service.ReloadAsync();

        Assert.Equal(1 + SnapshotService.MaxAttempts, reader.Calls);
        Assert.Equal(LoadStatus.FileLocked, service.Status);
        Assert.Equal(5m, service.Current!.Summary.DailyActual);
        File.Delete(path);
    }

    [Fact]
    public async Task Missing_file_reports_not_found_and_keeps_snapshot()
    {
        var (service, _, path) = Create(() => Data(5));
        await service.ReloadAsync();
        File.Delete(path);

        await service.ReloadAsync();

        Assert.Equal(LoadStatus.FileNotFound, service.Status);
        Assert.NotNull(service.Current);
    }

    [Fact]
    public async Task Real_reader_can_read_while_file_is_open_for_writing()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lock-{Guid.NewGuid():N}.xlsx");
        File.Copy(System.IO.Path.Combine(TestPaths.RepoRoot(), "samples", "SanLuong-khach-mau.xlsx"), path);

        // Giống Excel: giữ file mở và cho phép người khác đọc.
        await using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            var sheet = await new ExcelDataReader().ReadProductionAsync(path, null, CancellationToken.None);
            Assert.Equal(11, sheet.Records.Count);
        }
        File.Delete(path);
    }
}
