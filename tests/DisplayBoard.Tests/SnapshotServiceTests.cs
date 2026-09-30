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

    private sealed class FakeReader(Queue<Func<WorkbookData>> results) : IExcelDataReader
    {
        public int Calls { get; private set; }
        public Task<WorkbookData> ReadAsync(string filePath, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(results.Dequeue()());
        }
    }

    private static WorkbookData Data(decimal qty) => new(
        [new ProductionRecord(2, DateOnly.FromDateTime(DateTime.Today), null, null, "NV001", "A", "Ép", qty, 100, null)],
        [], [], [], [], new Dictionary<string, string>(), []);

    private static (SnapshotService Service, FakeReader Reader, string Path) Create(params Func<WorkbookData>[] results)
    {
        var path = System.IO.Path.GetTempFileName();
        var reader = new FakeReader(new Queue<Func<WorkbookData>>(results));
        var service = new SnapshotService(reader, new DataProcessor(), new FakeConfig(path), TimeProvider.System, NullLogger<SnapshotService>.Instance);
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
        Assert.Equal(10m, service.Current!.Summary.TotalQuantity);
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
        var results = new List<Func<WorkbookData>> { () => Data(5) };
        results.AddRange(Enumerable.Repeat<Func<WorkbookData>>(() => throw new IOException("locked"), SnapshotService.MaxAttempts));
        var (service, reader, path) = Create(results.ToArray());
        await service.ReloadAsync();

        await service.ReloadAsync();

        Assert.Equal(1 + SnapshotService.MaxAttempts, reader.Calls);
        Assert.Equal(LoadStatus.FileLocked, service.Status);
        Assert.Equal(5m, service.Current!.Summary.TotalQuantity);
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
        using (var source = WorkbookFactory.Create(wb => wb.AddTable("DATA", WorkbookFactory.DataHeaders,
                   [DateTime.Today, null, null, "NV001", "A", "Ép", 7, null, null])))
        await using (var file = File.Create(path))
            await source.CopyToAsync(file);

        // Giống Excel: giữ file mở và cho phép người khác đọc.
        await using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            var data = await new ExcelWorkbookReader().ReadAsync(path, CancellationToken.None);
            Assert.Single(data.Records);
        }
        File.Delete(path);
    }
}
