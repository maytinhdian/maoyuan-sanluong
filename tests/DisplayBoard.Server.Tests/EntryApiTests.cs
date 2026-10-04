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
                    new EntryUser { Name = "Quản lý", Pin = "9999" }
                ]
            }
        };
        var config = new FakeConfig(_config);
        var clock = new FixedClock();
        _database = new ProductionDatabase(config, clock, NullLogger<ProductionDatabase>.Instance);
        _database.Store.ReplaceAll(V20Importer.Read(Path.Combine(RepoRoot(), "samples", "Theo_doi_san_luong_V20_mau.xlsx")).Data, "test", "V20");
        _entry = new EntryService(config, _database, clock, NullLogger<EntryService>.Instance);
        _server = new BoardServer(new FakeSnapshots(), config, clock, NullLogger<BoardServer>.Instance, _entry);
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
