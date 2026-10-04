using ClosedXML.Excel;
using DisplayBoard.Core.Data;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DisplayBoard.Tests.Data;

public sealed class DataMaintenanceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("maintenance-").FullName;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero)) { };
    private readonly DisplayConfiguration _settings;
    private readonly ProductionDatabase _database;
    private readonly DataMaintenance _maintenance;

    private sealed class FakeConfig(DisplayConfiguration config) : IConfigurationService
    {
        public DisplayConfiguration Current { get; } = config;
        public DisplayConfiguration Load() => Current;
        public void Save(DisplayConfiguration configuration) { }
    }

    public DataMaintenanceTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _settings = new DisplayConfiguration { DataFolder = _dir, BackupCount = 3 };
        var config = new FakeConfig(_settings);
        _database = new ProductionDatabase(config, _time, NullLogger<ProductionDatabase>.Instance);
        _maintenance = new DataMaintenance(_database, config, _time, NullLogger<DataMaintenance>.Instance);
    }

    [Fact]
    public void Nothing_happens_before_there_is_data()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 14, 23, 0, 0, TimeSpan.Zero));
        _maintenance.RunDue();
        Assert.False(Directory.Exists(_settings.ResolveBackupFolder()));
        Assert.False(Directory.Exists(_settings.ResolveExportFolder()));
    }

    [Fact]
    public void Backs_up_once_a_day_and_keeps_the_newest_copies()
    {
        _database.Store.ReplaceAll(SampleProduction.Create(), "test");
        for (var day = 0; day < 5; day++)
        {
            _maintenance.RunDue();
            _maintenance.RunDue();
            _time.Advance(TimeSpan.FromDays(1));
        }

        var backups = DataMaintenance.Backups(_settings.ResolveBackupFolder()).Select(Path.GetFileName).ToList();
        Assert.Equal(["sanluong_2026-10-18.db", "sanluong_2026-10-17.db", "sanluong_2026-10-16.db"], backups);
        using var copy = new ProductionStore(Path.Combine(_settings.ResolveBackupFolder(), backups[0]!));
        Assert.Equal(_database.Store.Load().Entries.Count, copy.Load().Entries.Count);
    }

    [Fact]
    public void Exports_the_day_file_after_the_export_time()
    {
        _database.Store.ReplaceAll(SampleProduction.Create(), "test");
        var file = Path.Combine(_settings.ResolveExportFolder(), "SanLuong_2026-10-14.xlsx");

        _time.SetUtcNow(new DateTimeOffset(2026, 10, 14, 21, 59, 0, TimeSpan.Zero));
        _maintenance.RunDue();
        Assert.False(File.Exists(file));

        _time.SetUtcNow(new DateTimeOffset(2026, 10, 14, 22, 0, 0, TimeSpan.Zero));
        _maintenance.RunDue();
        Assert.True(File.Exists(file));
        using var workbook = new XLWorkbook(file);
        Assert.Equal(new DateTime(2026, 10, 14), workbook.Worksheet("HIEN_THI").Cell("J1").GetDateTime());
    }

    [Fact]
    public void Month_export_stops_at_today()
    {
        _database.Store.ReplaceAll(SampleProduction.Create(), "test");
        var month = _maintenance.ExportMonth(new DateOnly(2026, 10, 1));
        var september = _maintenance.ExportMonth(new DateOnly(2026, 9, 1));

        Assert.Equal("SanLuong_2026-10.xlsx", month.FileName);
        using (var workbook = new XLWorkbook(new MemoryStream(month.Content)))
            Assert.Equal(new DateTime(2026, 10, 14), workbook.Worksheet("HIEN_THI").Cell("J1").GetDateTime());
        using (var workbook = new XLWorkbook(new MemoryStream(september.Content)))
            Assert.Equal(new DateTime(2026, 9, 30), workbook.Worksheet("HIEN_THI").Cell("J1").GetDateTime());
    }

    [Fact]
    public void Import_copies_images_and_backs_up_existing_data()
    {
        _database.Store.ReplaceAll(SampleProduction.Create(), "test");
        var source = Directory.CreateTempSubdirectory("old-excel-").FullName;
        var excel = Path.Combine(source, "Theo_doi.xlsx");
        File.Copy(Path.Combine(TestPaths.RepoRoot(), "samples", "Theo_doi_san_luong_V20_mau.xlsx"), excel);
        Directory.CreateDirectory(Path.Combine(source, "images", "hang_loi"));
        File.WriteAllText(Path.Combine(source, "images", "883.jpg"), "a");
        File.WriteAllText(Path.Combine(source, "images", "hang_loi", "x.jpg"), "b");

        var preview = DataMaintenance.PreviewImport(excel);
        Assert.Equal(0, preview.Check.Mismatches);
        _maintenance.ApplyImport(preview, "Quản lý");

        Assert.Single(DataMaintenance.Backups(_settings.ResolveBackupFolder()));
        Assert.Equal(preview.Result.Data.Entries.Count, _database.Store.Load().Entries.Count);
        Assert.True(File.Exists(Path.Combine(_settings.ResolveImagesFolder()!, "hang_loi", "x.jpg")));
        Assert.True(File.Exists(Path.Combine(_settings.ResolveImagesFolder()!, "883.jpg")));
        Directory.Delete(source, true);
    }

    public void Dispose()
    {
        _maintenance.Dispose();
        _database.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
