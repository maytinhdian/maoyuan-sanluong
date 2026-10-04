using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using DisplayBoard.Core.Data;
using DisplayBoard.Core.Entry;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisplayBoard.Server.Tests;

/// <summary>Trang nhập liệu /nhap: đăng nhập PIN, ghi giờ, kế hoạch, hàng lỗi kèm ảnh vào SQLite (dữ liệu nhập từ file mẫu V20).</summary>
public sealed class EntryApiTests : IAsyncLifetime
{
    /// <summary>Giờ máy chủ cố định 30/09/2026 13:42 (ngày có dữ liệu trong file mẫu V20), hẹn giờ vẫn chạy thật.</summary>
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 30, 13, 42, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeConfig(DisplayConfiguration config) : IConfigurationService
    {
        public DisplayConfiguration Current { get; } = config;
        public DisplayConfiguration Load() => Current;
        public void Save(DisplayConfiguration configuration) { }
    }

    private sealed class FakeSnapshots : ISnapshotService
    {
        public DisplayDataSnapshot? Current => null;
        public LoadStatus Status => LoadStatus.NoFileSelected;
        public string? LastError => null;
        public DateTimeOffset? LastLoadedAt => null;
        public event EventHandler<DisplayDataSnapshot>? SnapshotChanged { add { } remove { } }
        public event EventHandler? StatusChanged { add { } remove { } }
        public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"nhap-{Guid.NewGuid():N}");
    private readonly HttpClient _http = new();
    private static readonly DateOnly Today = new(2026, 9, 30);
    private DisplayConfiguration _config = null!;
    private ProductionDatabase _database = null!;
    private EntryService _entry = null!;
    private DataMaintenance _maintenance = null!;
    private BoardServer _server = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        var port = FreePort();
        _config = new DisplayConfiguration
        {
            DataFolder = _folder,
            Server = new LanServerSettings { Port = port },
            DataEntry = new DataEntrySettings
            {
                Users =
                [
                    new EntryUser { Name = "Tổ trưởng C3", Pin = "1234", Lines = ["Chuyền 3"] },
                    new EntryUser { Name = "Quản lý", Pin = "9999", Manager = true }
                ]
            }
        };
        var config = new FakeConfig(_config);
        var clock = new FixedClock();
        _database = new ProductionDatabase(config, clock, NullLogger<ProductionDatabase>.Instance);
        _database.Store.ReplaceAll(V20Importer.Read(Path.Combine(RepoRoot(), "samples", "Theo_doi_san_luong_V20_mau.xlsx")).Data, "test", "V20");
        _entry = new EntryService(config, _database, clock, NullLogger<EntryService>.Instance);
        _maintenance = new DataMaintenance(_database, config, clock, NullLogger<DataMaintenance>.Instance);
        _server = new BoardServer(new FakeSnapshots(), config, clock, NullLogger<BoardServer>.Instance, _entry, _maintenance);
        await _server.ApplyAsync(_config);
        _http.BaseAddress = new Uri($"http://127.0.0.1:{port}");
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _database.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _http.Dispose();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Page_and_info_are_served()
    {
        var page = await _http.GetStringAsync("/nhap");
        Assert.Contains("nhap.js", page);
        Assert.Contains(">Lưu<", page);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/assets/nhap.js")).StatusCode);
        var info = await _http.GetFromJsonAsync<JsonElement>("/api/nhap/info");
        Assert.True(info.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Wrong_pin_is_rejected_and_locks_after_five_tries()
    {
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await _http.PostAsJsonAsync("/api/nhap/login", new { pin = "0000" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await _http.PostAsJsonAsync("/api/nhap/login", new { pin = "1234" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync("/api/nhap/context")).StatusCode);
    }

    [Fact]
    public async Task Line_leader_sees_only_their_line()
    {
        await LoginAsync("1234");
        var context = await _http.GetFromJsonAsync<JsonElement>("/api/nhap/context?line=Chuyền 3");
        Assert.Equal(["Chuyền 3"], context.GetProperty("lines").EnumerateArray().Select(l => l.GetString()));
        var line = context.GetProperty("line");
        Assert.Equal("883", line.GetProperty("plan").GetProperty("productCode").GetString());
        Assert.Equal(6, line.GetProperty("suggestedHour").GetInt32());
        Assert.Equal("13:30:00", line.GetProperty("hours")[5].GetProperty("start").GetString());

        var other = await _http.PostAsJsonAsync("/api/nhap/hourly", new { line = "Chuyền 1", hour = 1, quantity = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        Assert.Contains("không được nhập cho Chuyền 1", await ErrorAsync(other));
    }

    [Fact]
    public async Task Hourly_output_is_written_to_the_database()
    {
        await LoginAsync("1234");
        var job = await PostAsync("/api/nhap/hourly", new { line = "Chuyền 3", hour = 6, quantity = 182 });
        Assert.Equal("Done", (await WaitAsync(job)).GetProperty("status").GetString());

        Assert.Equal(182, Entry("Chuyền 3")!.Hours[5]);
        var jobs = await _http.GetFromJsonAsync<JsonElement>("/api/nhap/jobs");
        Assert.Equal("Sản lượng giờ 6 · 182", jobs[0].GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Unusually_large_number_is_rejected()
    {
        await LoginAsync("1234");
        var response = await _http.PostAsJsonAsync("/api/nhap/hourly", new { line = "Chuyền 3", hour = 6, quantity = 1900 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("lớn hơn 3 lần", await ErrorAsync(response));
    }

    [Fact]
    public async Task Plan_creates_the_row_for_a_line_without_one()
    {
        await LoginAsync("9999");
        var job = await PostAsync("/api/nhap/plan", new { line = "Chuyền 5", productCode = "883", shiftCode = "8H", hourlyTarget = 50 });
        Assert.Equal("Done", (await WaitAsync(job)).GetProperty("status").GetString());

        var entry = Entry("Chuyền 5")!;
        Assert.Equal("883", entry.ProductCode);
        Assert.Equal("8H", entry.ShiftCode);
        Assert.Equal(50, entry.HourlyTarget);
    }

    [Fact]
    public async Task Defect_with_photos_saves_images_and_row()
    {
        await LoginAsync("1234");
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Chuyền 3"), "line" },
            { new StringContent("Bung chỉ"), "defectType" },
            { new StringContent("4"), "quantity" },
            { new StringContent("vai trái"), "note" },
            { Jpeg(), "photos", "anh1.jpg" },
            { Jpeg(), "photos", "anh2.jpg" },
        };
        var response = await _http.PostAsync("/api/nhap/defect", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Done", (await WaitAsync(job)).GetProperty("status").GetString());

        var photos = Directory.GetFiles(Path.Combine(_folder, "images", "hang_loi")).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["chuyen3_20260930_134200_1.jpg", "chuyen3_20260930_134200_2.jpg"], photos);
        var defect = _database.Store.Load().Defects.MaxBy(d => d.Id)!;
        Assert.Equal("Bung chỉ", defect.DefectType);
        Assert.Equal(new TimeOnly(13, 42), defect.Time);
        Assert.Equal("chuyen3_20260930_134200_1.jpg; chuyen3_20260930_134200_2.jpg", defect.ImageFiles);
        Assert.Equal("vai trái", defect.Note);
        Assert.Contains(_database.Store.Audit(), a => a.User == "Tổ trưởng C3");
    }

    [Fact]
    public async Task Non_image_upload_is_rejected()
    {
        await LoginAsync("1234");
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Chuyền 3"), "line" },
            { new StringContent("Bung chỉ"), "defectType" },
            { new StringContent("1"), "quantity" },
            { new ByteArrayContent("<script>"u8.ToArray()), "photos", "x.jpg" },
        };
        var response = await _http.PostAsync("/api/nhap/defect", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("không phải ảnh", await ErrorAsync(response));
    }

    [Fact]
    public async Task Hour_without_plan_today_uses_the_last_plan()
    {
        var line = _database.Store.Load().FindLine("Chuyền 3")!;
        // Hôm qua chạy 883 ca 11H30, hôm nay chưa có dòng.
        _database.Store.SavePlan(Today.AddDays(-1), line.Code, "883", "11H30", 185, "test");
        _database.Store.DeleteEntry(Today, line.Code, "test");
        await LoginAsync("1234");
        var context = await _http.GetFromJsonAsync<JsonElement>("/api/nhap/context?line=Chuyền 3");
        Assert.False(context.GetProperty("line").GetProperty("hasRow").GetBoolean());

        var job = await PostAsync("/api/nhap/hourly", new { line = "Chuyền 3", hour = 1, quantity = 150 });

        Assert.Equal("Done", job.GetProperty("status").GetString());
        var entry = Entry("Chuyền 3")!;
        Assert.Equal(150, entry.Hours[0]);
        Assert.Equal(("883", "11H30", 185m), (entry.ProductCode, entry.ShiftCode, entry.HourlyTarget));
    }

    [Fact]
    public async Task Removing_a_user_ends_their_session()
    {
        await LoginAsync("1234");
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/api/nhap/jobs")).StatusCode);
        _config.DataEntry.Users.RemoveAt(0);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync("/api/nhap/jobs")).StatusCode);
    }

    // ---------- Trang quản lý /quan-ly ----------

    [Fact]
    public async Task Admin_page_is_for_managers_only()
    {
        Assert.Contains("quanly.js", await _http.GetStringAsync("/quan-ly"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync("/api/quanly/catalog")).StatusCode);
        await LoginAsync("1234");
        var response = await _http.GetAsync("/api/quanly/catalog");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("không phải quản lý", await ErrorAsync(response));
    }

    [Fact]
    public async Task Manager_edits_an_earlier_day_and_it_is_audited()
    {
        await LoginAsync("9999");
        var day = Today.AddDays(-1);
        var hours = new decimal?[12];
        hours[0] = 80;
        hours[1] = 95;
        var result = await PostAsync("/api/quanly/entry", new
        {
            line = "Chuyền 1", date = day, productCode = "883", shiftCode = "8H", hourlyTarget = 100, hours,
            workers = 12, reason = (string?)null, downtimeMinutes = 15, status = (string?)null, note = "sửa sau",
        });

        var row = result.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("name").GetString() == "Chuyền 1");
        Assert.Equal(175, row.GetProperty("entry").GetProperty("actual").GetDecimal());
        Assert.Equal(800, row.GetProperty("entry").GetProperty("dailyTarget").GetDecimal());
        var data = _database.Store.Load();
        var entry = DisplayCalculator.Entry(data, day, data.FindLine("Chuyền 1")!.Code)!;
        Assert.Equal((12m, 15m, "sửa sau"), (entry.Workers!.Value, entry.DowntimeMinutes!.Value, entry.Note));
        var audit = await _http.GetFromJsonAsync<JsonElement>("/api/quanly/audit");
        Assert.Equal("Quản lý", audit[0].GetProperty("user").GetString());
    }

    [Fact]
    public async Task Copy_plan_fills_lines_without_a_row()
    {
        await LoginAsync("9999");
        var result = await PostAsync("/api/quanly/copy-plan?date=2026-10-01", new { });
        var planned = result.GetProperty("lines").EnumerateArray().Count(l => l.TryGetProperty("entry", out _));
        Assert.Equal(_database.Store.Load().Entries.Count(e => e.Date == Today), planned);
    }

    [Fact]
    public async Task Manager_saves_catalogs_and_month_targets()
    {
        await LoginAsync("9999");
        var catalog = await PostAsync("/api/quanly/shifts", new[]
        {
            new { code = "8H", name = "Ca ngày", periods = new[] { "07:30-11:30", "12:30-16:30" }, active = true },
            new { code = "4H", name = (string?)null, periods = new[] { "07:30-11:30" }, active = true },
        });
        Assert.Equal(["8H", "4H"], catalog.GetProperty("shifts").EnumerateArray().Select(s => s.GetProperty("code").GetString()));

        var bad = await _http.PostAsJsonAsync("/api/quanly/shifts", new[] { new { code = "X", periods = new[] { "11:30-07:30" }, active = true } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var targets = await PostAsync("/api/quanly/targets", new { month = "2026-09-01", targets = new[] { new { productCode = "883", target = 50000, note = "" } } });
        Assert.Equal(50000, targets.GetProperty("targets")[0].GetProperty("target").GetDecimal());
        Assert.Single(_database.Store.Load().MonthTargets, t => t.Month == new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task Manager_downloads_excel_for_a_day()
    {
        await LoginAsync("9999");
        var response = await _http.GetAsync("/api/quanly/export?date=2026-09-30");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SanLuong_2026-09-30.xlsx", response.Content.Headers.ContentDisposition!.FileName?.Trim('"'));
        using var workbook = new ClosedXML.Excel.XLWorkbook(await response.Content.ReadAsStreamAsync());
        Assert.True(workbook.Worksheet("NHAP_LIEU").Cell("A5").GetDateTime() == new DateTime(2026, 9, 30));
    }

    [Fact]
    public async Task Import_reads_then_replaces_after_confirmation()
    {
        await LoginAsync("9999");
        _database.Store.SaveLines([.. _database.Store.Load().Lines, new LineDef("X1", "Chuyền thử", Active: false)], "test");
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(RepoRoot(), "samples", "Theo_doi_san_luong_V20_mau.xlsx"))), "file", "V20.xlsx" },
        };
        var response = await _http.PostAsync("/api/quanly/import", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, preview.GetProperty("mismatches").GetInt32());
        Assert.NotNull(_database.Store.Load().FindLine("X1"));   // chưa ghi

        await PostAsync("/api/quanly/import/" + preview.GetProperty("id").GetString(), new { });
        Assert.Null(_database.Store.Load().FindLine("X1"));
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync("/api/quanly/import/" + preview.GetProperty("id").GetString(), new { })).StatusCode);
    }

    private DayEntry? Entry(string line)
    {
        var data = _database.Store.Load();
        return DisplayCalculator.Entry(data, Today, data.FindLine(line)!.Code);
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()!;

    private async Task LoginAsync(string pin)
    {
        var response = await _http.PostAsJsonAsync("/api/nhap/login", new { pin });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        _http.DefaultRequestHeaders.Remove("X-Entry-Token");
        _http.DefaultRequestHeaders.Add("X-Entry-Token", body.GetProperty("token").GetString());
    }

    private async Task<JsonElement> PostAsync(string path, object body)
    {
        var response = await _http.PostAsJsonAsync(path, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> WaitAsync(JsonElement job, Func<string, bool>? until = null)
    {
        until ??= s => s is "Done" or "Failed";
        var id = job.GetProperty("id").GetString();
        for (var i = 0; i < 100; i++)
        {
            var jobs = await _http.GetFromJsonAsync<JsonElement>("/api/nhap/jobs");
            var found = jobs.EnumerateArray().First(j => j.GetProperty("id").GetString() == id);
            if (until(found.GetProperty("status").GetString()!))
                return found;
            await Task.Delay(50);
        }
        throw new TimeoutException("Phiếu chưa ghi xong");
    }

    private static ByteArrayContent Jpeg()
    {
        var content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return content;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DisplayBoard.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy thư mục gốc repo");
    }
}
