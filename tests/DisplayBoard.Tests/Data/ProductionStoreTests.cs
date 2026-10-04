using ClosedXML.Excel;
using DisplayBoard.Core.Data;

namespace DisplayBoard.Tests.Data;

public sealed class ProductionStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("db-store-").FullName;

    private ProductionStore Open() => new(Path.Combine(_dir, ProductionStore.DefaultFileName));

    [Fact]
    public void Replace_all_then_load_gives_same_display()
    {
        var data = SampleProduction.Create();
        using (var store = Open())
            store.ReplaceAll(data, "test");

        using var reopened = Open();
        var loaded = reopened.Load();
        Assert.Equal(data.Entries.Count, loaded.Entries.Count);
        Assert.Equal(data.Defects.Count, loaded.Defects.Count);
        Assert.Equal(data.Lines.Select(l => l.Code), loaded.Lines.Select(l => l.Code));
        Assert.Equal(data.Shifts.Select(s => s.Hours), loaded.Shifts.Select(s => s.Hours));
        foreach (var day in new[] { new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1), SampleProduction.LastDay })
            Assert.Empty(DisplayComparer.Compare(DisplayCalculator.Compute(data, day), DisplayCalculator.Compute(loaded, day)));
    }

    [Fact]
    public void Writes_update_data_and_audit_log()
    {
        using var store = Open();
        store.SaveLines([new LineDef("CH01", "Chuyền 1")], "Quản lý");
        store.SaveShifts([new ShiftDef("8H", null, [new(new(7, 30), new(11, 30)), new(new(12, 30), new(16, 30))])], "Quản lý");
        var changes = 0;
        store.Changed += (_, _) => changes++;
        var day = new DateOnly(2026, 10, 5);

        Assert.Throws<InvalidOperationException>(() => store.SetHour(day, "CH01", 1, 90, "Lan"));   // chưa có kế hoạch
        store.SavePlan(day, "CH01", "883", "8H", 100, "Lan");
        store.SetHour(day, "CH01", 1, 90, "Lan");
        store.SetHour(day, "CH01", 2, 105.5m, "Lan");
        store.SetHour(day, "CH01", 2, null, "Lan");
        var id = store.AddDefect(new DefectRow { Date = day, Time = new(9, 15), LineCode = "CH01", DefectType = "Bung chỉ", Quantity = 3 }, "QC");

        var line = DisplayCalculator.Compute(store.Load(), day).Lines.Single();
        Assert.Equal(800, line.DailyTarget);
        Assert.Equal(90, line.DailyActual);
        Assert.Equal(3, line.Defects);
        Assert.Equal(5, changes);
        var audit = store.Audit();
        Assert.Equal("QC", audit[0].User);
        Assert.Contains(audit, a => a.Action == "Giờ 2 CH01 2026-10-05" && a.Before == "105.5" && a.After is null);

        store.DeleteDefect(id, "QC");
        Assert.Empty(store.Load().Defects);
    }

    [Fact]
    public void Line_with_data_cannot_be_removed()
    {
        using var store = Open();
        store.SaveLines([new LineDef("CH01", "Chuyền 1"), new LineDef("CH02", "Chuyền 2")], "Quản lý");
        store.SavePlan(new DateOnly(2026, 10, 5), "CH02", "883", "8H", 100, "Lan");
        Assert.Throws<InvalidOperationException>(() => store.SaveLines([new LineDef("CH01", "Chuyền 1")], "Quản lý"));
        store.SaveLines([new LineDef("CH01", "Chuyền 1"), new LineDef("CH02", "Chuyền B", Active: false)], "Quản lý");
        Assert.Equal("Chuyền B", store.Load().LineName("CH02"));
    }

    [Fact]
    public void Backup_copies_database()
    {
        using var store = Open();
        store.ReplaceAll(SampleProduction.Create(), "test");
        var backup = Path.Combine(_dir, "backup.db");
        store.Backup(backup);
        using var copy = new ProductionStore(backup);
        Assert.Equal(store.Load().Entries.Count, copy.Load().Entries.Count);
    }

    [Fact]
    public void Export_then_import_round_trips_input_columns()
    {
        var data = SampleProduction.Create(3);
        using var stream = new MemoryStream();
        ExcelExporter.Export(data, SampleProduction.LastDay, stream);
        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        var imported = V20Importer.Read(workbook);

        Assert.Empty(imported.Warnings);
        var back = imported.Data;
        Assert.Equal(data.Shifts.Select(s => (s.Code, s.Hours, s.Active)), back.Shifts.Select(s => (s.Code, s.Hours, s.Active)));
        Assert.Equal(data.Lines.OrderBy(l => l.Code).Select(l => (l.Code, l.Name, l.Active)), back.Lines.OrderBy(l => l.Code).Select(l => (l.Code, l.Name, l.Active)));
        Assert.Equal(data.Products.Select(p => p.Code), back.Products.Select(p => p.Code));
        Assert.Equal(data.Calendar, back.Calendar);
        Assert.Equal(data.MonthTargets, back.MonthTargets);
        Assert.Equal(data.Entries.Count, back.Entries.Count);
        foreach (var e in data.Entries)
        {
            var b = DisplayCalculator.Entry(back, e.Date, e.LineCode)!;
            Assert.Equal(e.Hours, b.Hours);
            Assert.Equal((e.ProductCode, e.ShiftCode, e.HourlyTarget, e.Workers, e.Reason, e.Note), (b.ProductCode, b.ShiftCode, b.HourlyTarget, b.Workers, b.Reason, b.Note));
        }
        Assert.Equal(data.Defects.Select(d => (d.Date, d.Time, d.LineCode, d.Quantity, d.ImageFiles)).OrderBy(d => d.ToString()),
            back.Defects.Select(d => (d.Date, d.Time, d.LineCode, d.Quantity, d.ImageFiles)).OrderBy(d => d.ToString()));
        Assert.Equal(SampleProduction.LastDay, back.DisplayDateOverride);   // ô J1 của file xuất
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }
}
