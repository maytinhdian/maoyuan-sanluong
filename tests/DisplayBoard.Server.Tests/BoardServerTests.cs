using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisplayBoard.Server.Tests;

public sealed class BoardServerTests : IAsyncLifetime
{
    private sealed class FakeConfig(DisplayConfiguration config) : IConfigurationService
    {
        public DisplayConfiguration Current { get; } = config;
        public DisplayConfiguration Load() => Current;
        public void Save(DisplayConfiguration configuration) { }
    }

    private sealed class FakeSnapshots : ISnapshotService
    {
        public DisplayDataSnapshot? Current { get; private set; }
        public LoadStatus Status { get; private set; } = LoadStatus.NoFileSelected;
        public string? LastError => null;
        public DateTimeOffset? LastLoadedAt => null;
        public event EventHandler<DisplayDataSnapshot>? SnapshotChanged;
        public event EventHandler? StatusChanged;
        public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void Publish(DisplayDataSnapshot snapshot)
        {
            Current = snapshot;
            Status = LoadStatus.Updated;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private readonly string _image = Path.Combine(Path.GetTempPath(), $"board-{Guid.NewGuid():N}.png");
    private readonly FakeSnapshots _snapshots = new();
    private readonly HttpClient _http = new();
    private BoardServer _server = null!;
    private DisplayConfiguration _config = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        await File.WriteAllBytesAsync(_image, [0x89, 0x50, 0x4E, 0x47]);
        _port = FreePort();
        _config = new DisplayConfiguration
        {
            AppName = "Xưởng A",
            DisplayMode = DisplayMode.Independent,
            NetworkScreens =
            [
                new NetworkScreen { Number = 1, Name = "Cổng xưởng", Playlist = [new PlaylistItem { ViewId = "overview" }, new PlaylistItem { ViewId = "ranking", Seconds = 20 }] },
                new NetworkScreen { Number = 3, Playlist = [new PlaylistItem { ViewId = "detail" }] }
            ],
            Server = new LanServerSettings { Port = _port }
        };
        _snapshots.Publish(Snapshot(120));
        _server = new BoardServer(_snapshots, new FakeConfig(_config), TimeProvider.System, NullLogger<BoardServer>.Instance);
        await _server.ApplyAsync(_config);
        _http.BaseAddress = new Uri($"http://127.0.0.1:{_port}");
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _http.Dispose();
        File.Delete(_image);
    }

    private DisplayDataSnapshot Snapshot(decimal actual) => new(
        DateTimeOffset.Now, "HIEN_THI",
        new ProductionSummary { Date = DateOnly.FromDateTime(DateTime.Today), IsToday = true, ProductCount = 1, DailyTarget = 100, DailyActual = actual },
        [new ProductDaily { Line = "Chuyền 1", ProductCode = "883", DisplayName = "ĐAI LƯNG", Color = "#3B82F6", ImagePath = _image, DailyTarget = 100, DailyActual = actual, DailyStatus = ProgressStatus.Met }],
        [], [], "PCS", null, []);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task<JsonElement> GetStateAsync(int tv, string? key = null)
    {
        var json = await _http.GetStringAsync($"/api/state?tv={tv}" + (key is null ? "" : "&key=" + key));
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task Serves_tv_page_and_assets()
    {
        Assert.True(_server.IsRunning);
        Assert.Contains("board.js", await _http.GetStringAsync("/tv/1"));
        var js = await _http.GetAsync("/assets/board.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Equal("text/javascript", js.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/assets/..%2FBoardServer.cs")).StatusCode);
    }

    [Fact]
    public async Task State_carries_each_tv_playlist_and_the_excel_numbers()
    {
        var tv1 = await GetStateAsync(1);
        Assert.Equal("Xưởng A", tv1.GetProperty("appName").GetString());
        Assert.Equal(120m, tv1.GetProperty("data").GetProperty("summary").GetProperty("dailyActual").GetDecimal());
        Assert.Equal("Met", tv1.GetProperty("data").GetProperty("products")[0].GetProperty("dailyStatus").GetString());
        var playlist = tv1.GetProperty("screen").GetProperty("playlist");
        Assert.Equal(["overview", "ranking"], playlist.EnumerateArray().Select(p => p.GetProperty("viewId").GetString()));
        Assert.Equal(20, playlist[1].GetProperty("seconds").GetInt32());

        Assert.Equal("Cổng xưởng", tv1.GetProperty("screen").GetProperty("name").GetString());

        var tv3 = await GetStateAsync(3);
        Assert.Equal("TV3", tv3.GetProperty("screen").GetProperty("name").GetString());
        Assert.Equal("detail", tv3.GetProperty("screen").GetProperty("playlist")[0].GetProperty("viewId").GetString());
    }

    [Fact]
    public async Task Unknown_tv_gets_no_screen_and_info_lists_the_tvs()
    {
        var tv2 = await GetStateAsync(2);
        Assert.False(tv2.TryGetProperty("screen", out var screen) && screen.ValueKind != JsonValueKind.Null);

        using var info = JsonDocument.Parse(await _http.GetStringAsync("/api/info"));
        var screens = info.RootElement.GetProperty("screens").EnumerateArray()
            .Select(e => (e.GetProperty("number").GetInt32(), e.GetProperty("name").GetString())).ToArray();
        Assert.Equal([(1, "Cổng xưởng"), (3, "TV3")], screens);
    }

    [Fact]
    public void Without_network_tvs_the_wired_screens_are_used()
    {
        var config = new DisplayConfiguration
        {
            Screens = [new ScreenAssignment { Playlist = [new PlaylistItem { ViewId = "detail" }] }]
        };
        var tvs = config.ResolveNetworkScreens();
        Assert.Equal([1, 2], tvs.Select(t => t.Number));
        Assert.Equal("detail", tvs[0].Playlist[0].ViewId);
        Assert.Equal("ranking", tvs[1].Playlist[0].ViewId);
    }

    [Fact]
    public async Task Image_paths_become_urls_and_only_known_images_are_served()
    {
        var url = (await GetStateAsync(1)).GetProperty("data").GetProperty("products")[0].GetProperty("imagePath").GetString()!;
        Assert.StartsWith("/img/", url);
        Assert.DoesNotContain(Path.GetFileName(_image), url);
        var image = await _http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/img/0000000000.png")).StatusCode);
    }

    [Fact]
    public async Task Access_key_is_required_when_set()
    {
        _config.Server.AccessKey = "1234";
        await _server.ApplyAsync(_config);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync("/api/state?tv=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync("/api/state?tv=1&key=9999")).StatusCode);
        Assert.Equal(120m, (await GetStateAsync(1, "1234")).GetProperty("data").GetProperty("summary").GetProperty("dailyActual").GetDecimal());
        var info = JsonDocument.Parse(await _http.GetStringAsync("/api/info")).RootElement;
        Assert.True(info.GetProperty("keyRequired").GetBoolean());
        Assert.False(info.TryGetProperty("data", out _));

        using var socket = new ClientWebSocket();
        await Assert.ThrowsAnyAsync<WebSocketException>(() => socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/ws?tv=1"), CancellationToken.None));
    }

    [Fact]
    public async Task WebSocket_gets_state_on_connect_and_again_when_excel_changes()
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/ws?tv=1"), CancellationToken.None);
        var first = await ReceiveAsync(socket);
        Assert.Equal(120m, first.GetProperty("data").GetProperty("summary").GetProperty("dailyActual").GetDecimal());
        Assert.Single(_server.Clients);
        Assert.Equal(1, _server.Clients[0].Screen);

        _snapshots.Publish(Snapshot(150));
        var second = await ReceiveAsync(socket);
        Assert.Equal(150m, second.GetProperty("data").GetProperty("summary").GetProperty("dailyActual").GetDecimal());

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        for (var i = 0; i < 50 && _server.Clients.Count > 0; i++)
            await Task.Delay(20);
        Assert.Empty(_server.Clients);
    }

    [Fact]
    public async Task Changing_the_port_restarts_and_disabling_stops()
    {
        var port = FreePort();
        _config.Server.Port = port;
        await _server.ApplyAsync(_config);
        Assert.Equal(port, _server.Port);
        using var other = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        Assert.Contains("board.js", await other.GetStringAsync("/tv/1"));

        _config.Server.Enabled = false;
        await _server.ApplyAsync(_config);
        Assert.False(_server.IsRunning);
        await Assert.ThrowsAsync<HttpRequestException>(() => other.GetStringAsync("/tv/1"));
    }

    [Fact]
    public async Task Port_in_use_is_reported_instead_of_crashing()
    {
        // Giống khi mở app thứ hai trên cùng máy: cổng đã có máy chủ khác giữ.
        var config = new DisplayConfiguration { Server = new LanServerSettings { Port = _port } };
        await using var second = new BoardServer(_snapshots, new FakeConfig(config), TimeProvider.System, NullLogger<BoardServer>.Instance);
        await second.ApplyAsync(config);

        Assert.False(second.IsRunning);
        Assert.Contains("đang bị chương trình khác dùng", second.LastError);
        Assert.True(_server.IsRunning);
    }

    private static async Task<JsonElement> ReceiveAsync(ClientWebSocket socket)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[256 * 1024];
        var total = 0;
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, total, buffer.Length - total), cts.Token);
            total += result.Count;
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, total)).RootElement;
    }
}
