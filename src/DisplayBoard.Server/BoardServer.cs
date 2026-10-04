using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DisplayBoard.Core.Entry;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Server;

/// <summary>
/// Máy chủ HTTP + WebSocket trong mạng LAN. Đọc dữ liệu từ <see cref="ISnapshotService"/> (vẫn là sheet HIEN_THI, chỉ đọc)
/// và đẩy sang các TV ngay khi dữ liệu đổi. TV chỉ cần trình duyệt mở http://&lt;IP&gt;:&lt;cổng&gt;/tv/1.
/// </summary>
public sealed class BoardServer : IBoardServer
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string Version =
        (Assembly.GetEntryAssembly() ?? typeof(BoardServer).Assembly).GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly ISnapshotService _snapshots;
    private readonly TimeProvider _time;
    private readonly ILogger<BoardServer> _logger;
    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();
    private readonly ConcurrentDictionary<string, string> _images = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly EntryApi? _entry;
    private DisplayConfiguration _config;
    private WebApplication? _app;

    /// <param name="entry">Nhập liệu qua trình duyệt (/nhap). Null = không có trang nhập liệu.</param>
    public BoardServer(ISnapshotService snapshots, IConfigurationService configuration, TimeProvider time, ILogger<BoardServer> logger,
        EntryService? entry = null)
    {
        _snapshots = snapshots;
        _time = time;
        _logger = logger;
        _config = configuration.Current;
        _entry = entry is null ? null : new EntryApi(entry, () => _config, time);
        _snapshots.SnapshotChanged += (_, _) => _ = BroadcastAsync();
    }

    public bool IsRunning => _app is not null;
    public int Port { get; private set; }
    public string? LastError { get; private set; }
    public IReadOnlyList<ConnectedClient> Clients => _connections.Values.Select(c => c.Info).OrderBy(c => c.ConnectedAt).ToList();
    public event EventHandler? StateChanged;

    public async Task ApplyAsync(DisplayConfiguration configuration, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _config = configuration;
            var settings = configuration.Server;
            var port = settings.Port is > 0 and < 65536 ? settings.Port : LanServerSettings.DefaultPort;
            if (_app is not null && (!settings.Enabled || port != Port))
                await StopCoreAsync().ConfigureAwait(false);
            if (settings.Enabled && _app is null)
                await StartCoreAsync(port, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        // Playlist/tên app có thể đã đổi: gửi lại cho TV đang mở. TV sai mã truy cập mới thì bị ngắt.
        await DropUnauthorizedAsync().ConfigureAwait(false);
        await BroadcastAsync().ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task StartCoreAsync(int port, CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BoardServer).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(port));
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        MapEndpoints(app);
        try
        {
            await app.StartAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            LastError = IsAddressInUse(ex)
                ? $"Cổng {port} đang bị chương trình khác dùng. Hãy đổi cổng khác."
                : $"Không mở được máy chủ ở cổng {port}: {ex.Message}";
            _logger.LogError(ex, "Không mở được máy chủ LAN ở cổng {Port}", port);
            return;
        }
        _app = app;
        Port = port;
        LastError = null;
        _logger.LogInformation("Máy chủ LAN chạy ở cổng {Port}: {Urls}", port, string.Join(", ", ScreenUrls(port, 1)));
    }

    private static bool IsAddressInUse(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is Microsoft.AspNetCore.Connections.AddressInUseException or SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
                return true;
        return false;
    }

    private async Task StopCoreAsync()
    {
        if (_app is null)
            return;
        foreach (var connection in _connections.Values)
            connection.Abort();
        _connections.Clear();
        var app = _app;
        _app = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await app.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        await app.DisposeAsync().ConfigureAwait(false);
        _logger.LogInformation("Đã tắt máy chủ LAN");
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/", () => WebAssets.Result("index.html"));
        app.MapGet("/tv/{screen:int}", (int screen) => WebAssets.Result("tv.html"));
        app.MapGet("/assets/{file}", (string file) => WebAssets.Result(file));

        // Trang chọn TV cần biết số TV và có cần mã hay không; không trả dữ liệu sản lượng.
        app.MapGet("/api/info", () => Results.Json(new
        {
            AppName = _config.ResolveAppName(),
            Version,
            KeyRequired = KeyRequired,
            Screens = _config.ResolveNetworkScreens().Select(s => new { s.Number, Name = ScreenName(s) }),
            DataEntry = _entry is not null && _config.DataEntry.Enabled
        }, Json));

        _entry?.Map(app);

        app.MapGet("/api/state", (HttpContext http, int? tv) =>
        {
            if (!Authorized(http))
                return Results.Json(new { Error = "Sai mã truy cập" }, Json, statusCode: StatusCodes.Status401Unauthorized);
            return Results.Text(JsonSerializer.Serialize(BuildState(tv ?? 1), Json), "application/json; charset=utf-8");
        });

        app.MapGet("/img/{token}", (HttpContext http, string token) =>
        {
            if (!Authorized(http))
                return Results.Unauthorized();
            if (!_images.TryGetValue(token, out var path) || !File.Exists(path))
                return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(path, ContentTypes.ForFile(path));
        });

        app.Map("/ws", async (HttpContext http) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (!Authorized(http))
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            var screen = int.TryParse(http.Request.Query["tv"], out var n) ? n : 1;
            using var socket = await http.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            await RunConnectionAsync(http, socket, screen, http.RequestAborted).ConfigureAwait(false);
        });
    }

    private async Task RunConnectionAsync(HttpContext http, WebSocket socket, int screen, CancellationToken ct)
    {
        var info = new ConnectedClient(Guid.NewGuid(), screen, http.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "?",
            http.Request.Headers.UserAgent.ToString(), _time.GetLocalNow());
        var connection = new Connection(info, socket, http.Request.Query["key"].ToString());
        _connections[info.Id] = connection;
        _logger.LogInformation("TV{Screen} kết nối từ {Address}", screen, info.Address);
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await connection.SendAsync(Serialize(screen), ct).ConfigureAwait(false);
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // TV chỉ nhận; đọc để biết khi nào TV đóng kết nối.
                var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
        }
        finally
        {
            _connections.TryRemove(info.Id, out _);
            _logger.LogInformation("TV{Screen} ({Address}) ngắt kết nối", screen, info.Address);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task BroadcastAsync()
    {
        if (_app is null)
            return;
        var messages = new Dictionary<int, byte[]>();
        foreach (var connection in _connections.Values)
        {
            if (!messages.TryGetValue(connection.Info.Screen, out var message))
                messages[connection.Info.Screen] = message = Serialize(connection.Info.Screen);
            try
            {
                await connection.SendAsync(message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
            {
                connection.Abort();
            }
        }
    }

    private Task DropUnauthorizedAsync()
    {
        foreach (var connection in _connections.Values.Where(c => !KeyMatches(c.Key)))
            connection.Abort();
        return Task.CompletedTask;
    }

    private byte[] Serialize(int screen) => JsonSerializer.SerializeToUtf8Bytes(BuildState(screen), Json);

    /// <summary>Toàn bộ những gì một TV cần: playlist của TV đó và dữ liệu (đường dẫn ảnh đổi thành URL).</summary>
    public BoardState BuildState(int screen)
    {
        var config = _config;
        var snapshot = _snapshots.Current;
        return new BoardState(
            "state",
            Version,
            _time.GetLocalNow(),
            config.ResolveAppName(),
            BuildScreen(config, screen),
            snapshot is null ? null : MapImages(snapshot),
            _snapshots.Status.ToString(),
            _snapshots.LastError);
    }

    /// <summary>Null khi máy chủ không có TV số này (vd đã bị xoá); TV vẫn giữ kết nối để hiện lại khi được thêm.</summary>
    private static ScreenState? BuildScreen(DisplayConfiguration config, int screen)
    {
        var tv = config.ResolveNetworkScreens().FirstOrDefault(s => s.Number == screen);
        if (tv is null)
            return null;
        var playlist = tv.Playlist.Count > 0 ? tv.Playlist : [new PlaylistItem { ViewId = "overview" }];
        return new ScreenState(tv.Number, ScreenName(tv), playlist,
            Math.Max(3, config.DefaultViewSeconds), Math.Max(5, config.MaxPagedViewSeconds));
    }

    private DisplayDataSnapshot MapImages(DisplayDataSnapshot snapshot) => snapshot with
    {
        Products = snapshot.Products.Select(p => p with { ImagePath = ImageUrl(p.ImagePath) }).ToList(),
        Notices = snapshot.Notices.Select(n => n with { BackgroundImagePath = ImageUrl(n.BackgroundImagePath) }).ToList(),
        DefectPhotos = snapshot.DefectPhotos.Select(d => d with { ImagePath = ImageUrl(d.ImagePath) }).ToList()
    };

    /// <summary>Chỉ ảnh có trong dữ liệu mới được phát, qua mã băm của đường dẫn (không lộ đường dẫn trên máy).</summary>
    private string? ImageUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..20].ToLowerInvariant();
        var token = hash + Path.GetExtension(path).ToLowerInvariant();
        _images[token] = path;
        return $"/img/{token}?v={File.GetLastWriteTimeUtc(path).Ticks}";
    }

    private static string ScreenName(NetworkScreen screen) =>
        string.IsNullOrWhiteSpace(screen.Name) ? $"TV{screen.Number}" : screen.Name.Trim();

    private bool KeyRequired => !string.IsNullOrWhiteSpace(_config.Server.AccessKey);

    private bool Authorized(HttpContext http)
    {
        var key = http.Request.Query["key"].ToString();
        if (key.Length == 0)
            key = http.Request.Headers["X-Board-Key"].ToString();
        return KeyMatches(key);
    }

    private bool KeyMatches(string? key)
    {
        var expected = _config.Server.AccessKey?.Trim();
        if (string.IsNullOrEmpty(expected))
            return true;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key?.Trim() ?? ""), Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>Địa chỉ IPv4 trong LAN của máy này (bỏ loopback và card ảo đang tắt).</summary>
    public static IReadOnlyList<string> LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(a => a.ToString())
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>Đường dẫn TV mở được, mỗi địa chỉ IP một dòng.</summary>
    public static IReadOnlyList<string> ScreenUrls(int port, int screen, string? key = null)
    {
        var suffix = string.IsNullOrWhiteSpace(key) ? "" : "?key=" + Uri.EscapeDataString(key.Trim());
        var addresses = LocalAddresses();
        if (addresses.Count == 0)
            addresses = ["localhost"];
        return addresses.Select(a => $"http://{a}:{port}/tv/{screen}{suffix}").ToList();
    }

    /// <summary>Địa chỉ trang nhập liệu, mỗi địa chỉ IP một dòng.</summary>
    public static IReadOnlyList<string> EntryUrls(int port)
    {
        var addresses = LocalAddresses();
        if (addresses.Count == 0)
            addresses = ["localhost"];
        return addresses.Select(a => $"http://{a}:{port}/nhap").ToList();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    /// <summary>Cho ServiceProvider.Dispose() khi app tắt. Chạy trên thread pool để không kẹt UI thread.</summary>
    public void Dispose() => Task.Run(async () => await DisposeAsync().ConfigureAwait(false)).Wait(TimeSpan.FromSeconds(5));

    private sealed class Connection(ConnectedClient info, WebSocket socket, string key)
    {
        private readonly SemaphoreSlim _send = new(1, 1);

        public ConnectedClient Info { get; } = info;
        public string Key { get; } = key;

        public async Task SendAsync(byte[] message, CancellationToken ct)
        {
            await _send.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.SendAsync(message, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
            finally
            {
                _send.Release();
            }
        }

        public void Abort() => socket.Abort();
    }
}

public sealed record ScreenState(int Number, string Name, IReadOnlyList<PlaylistItem> Playlist, int DefaultSeconds, int MaxPagedSeconds);

public sealed record BoardState(
    string Type,
    string Version,
    DateTimeOffset ServerTime,
    string AppName,
    ScreenState? Screen,
    DisplayDataSnapshot? Data,
    string Status,
    string? Error);
