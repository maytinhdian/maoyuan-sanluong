using DisplayBoard.Core.Data;
using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;
using DisplayBoard.Core.Services;
using DisplayBoard.Tests.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisplayBoard.Tests;

public sealed class SnapshotServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("snapshot-").FullName;
    private readonly ProductionDatabase _database;
    private readonly SnapshotService _service;

    private sealed class FakeConfig(string folder) : IConfigurationService
    {
        public DisplayConfiguration Current { get; } = new() { DataFolder = folder };
        public DisplayConfiguration Load() => Current;
        public void Save(DisplayConfiguration configuration) { }
    }

    public SnapshotServiceTests()
    {
        var config = new FakeConfig(_dir);
        _database = new ProductionDatabase(config, TimeProvider.System, NullLogger<ProductionDatabase>.Instance);
        _service = new SnapshotService(_database, new ExcelDataReader(), new SnapshotBuilder(), config, TimeProvider.System,
            NullLogger<SnapshotService>.Instance) { ChangeDelay = TimeSpan.FromMilliseconds(20) };
    }

    [Fact]
    public async Task Empty_database_reports_no_data()
    {
        await _service.ReloadAsync();

        Assert.Equal(LoadStatus.NoData, _service.Status);
        Assert.Null(_service.Current);
    }

    [Fact]
    public async Task Snapshot_matches_calculator_and_uses_product_names()
    {
        var data = SampleProduction.Create();
        data = data with { Products = data.Products.Select(p => p with { Name = $"Tên {p.Code}" }).ToList() };
        _database.Store.ReplaceAll(data, "test");

        await _service.ReloadAsync();

        var sheet = DisplayCalculator.Compute(data);
        Assert.Equal(LoadStatus.Updated, _service.Status);
        Assert.Equal(sheet.Total!.DailyActual ?? 0, _service.Current!.Summary.DailyActual);
        Assert.Equal(sheet.Lines.Count, _service.Current.Products.Count);
        var first = _service.Current.Products[0];
        Assert.Equal($"Tên {first.ProductCode}", first.DisplayName);
    }

    [Fact]
    public async Task Write_to_database_refreshes_snapshot()
    {
        var data = SampleProduction.Create();
        _database.Store.ReplaceAll(data, "test");
        await _service.ReloadAsync();
        var before = _service.Current!.Summary.DailyActual;
        var updated = new TaskCompletionSource<DisplayDataSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Lần ghi ReplaceAll ở trên cũng hẹn một lần tính lại: chờ đúng snapshot có số mới.
        _service.SnapshotChanged += (_, s) =>
        {
            if (s.Summary.DailyActual != before)
                updated.TrySetResult(s);
        };

        var entry = DisplayCalculator.DisplayLines(data)
            .Select(l => DisplayCalculator.Entry(data, SampleProduction.LastDay, l.Code))
            .First(e => e is not null && e.HasHours)!;
        var hour = entry.Hours.ToList().FindIndex(h => h is not null) + 1;
        _database.Store.SetHour(SampleProduction.LastDay, entry.LineCode, hour, entry.Hours[hour - 1]! + 7, "Lan");

        var snapshot = await updated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(before + 7, snapshot.Summary.DailyActual);
    }

    [Fact]
    public async Task Real_reader_can_read_while_file_is_open_for_writing()
    {
        var path = Path.Combine(_dir, "lock.xlsx");
        File.Copy(Path.Combine(TestPaths.RepoRoot(), "samples", "Theo_doi_san_luong_V18_mau.xlsx"), path);

        // Giống Excel: giữ file mở và cho phép người khác đọc.
        await using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            var sheet = await new ExcelDataReader().ReadDisplayAsync(path, null, CancellationToken.None);
            Assert.Equal(6, sheet.Lines.Count);
        }
    }

    public void Dispose()
    {
        _service.Dispose();
        _database.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
