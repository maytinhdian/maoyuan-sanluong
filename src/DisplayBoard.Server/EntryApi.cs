using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using DisplayBoard.Core.Entry;
using DisplayBoard.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace DisplayBoard.Server;

/// <summary>
/// Trang nhập liệu /nhap cho tổ trưởng (bản 3.x). Đăng nhập bằng mã PIN đặt trong app, nhận một mã phiên lưu trên điện thoại.
/// Mã phiên chỉ nằm trong bộ nhớ: app khởi động lại thì nhập PIN lại. Đổi PIN hoặc xoá người thì mã phiên cũ hết hiệu lực.
/// </summary>
internal sealed class EntryApi(EntryService entry, Func<DisplayConfiguration> config, TimeProvider time)
{
    /// <summary>Dịch vụ nhập liệu, trang quản lý dùng chung để lưu và đọc ảnh hàng lỗi.</summary>
    public EntryService Service => entry;

    /// <summary>
    /// Đọc ảnh/video gửi lên (multipart), tối đa <paramref name="max"/> tệp. Không chép vào bộ nhớ: ASP.NET đã để tệp lớn ở file tạm,
    /// EntryService đọc thẳng từ đó, kiểm tra loại và dung lượng rồi lưu.
    /// </summary>
    internal static Task<List<EntryPhoto>> ReadPhotosAsync(IFormFileCollection files, int max, CancellationToken ct)
    {
        if (files.Count > max)
            throw new EntryException($"Mỗi lần gửi tối đa {max} ảnh/video.");
        var photos = new List<EntryPhoto>();
        foreach (var file in files)
        {
            if (file.Length > EntryService.MaxVideoBytes)
                throw new EntryException($"Tệp {file.FileName} lớn quá {EntryService.MaxVideoBytes / 1024 / 1024} MB.");
            photos.Add(new EntryPhoto(file.FileName, file.Length, file.OpenReadStream));
        }
        return Task.FromResult(photos);
    }

    /// <summary>
    /// Phiếu lỗi có thể kèm video nên nâng giới hạn tải lên của máy chủ (mặc định 30 MB) cho đủ 4 video 100 MB.
    /// Gọi trước khi đọc form.
    /// </summary>
    internal static void AllowMediaUpload(HttpContext http)
    {
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = EntryService.MaxPhotos * EntryService.MaxVideoBytes + 1024 * 1024;
    }

    private const string TokenHeader = "X-Entry-Token";
    private const int MaxFailures = 5;
    // 5 lần sai thì khoá 15 phút: dò hết PIN 6 số mất hàng chục năm.
    private static readonly TimeSpan LockTime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SessionTime = TimeSpan.FromDays(30);

    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Until)> _failures = new(StringComparer.Ordinal);

    private sealed record Session(string Name, string Pin, DateTimeOffset Expires);

    public sealed record LoginRequest(string? Pin);
    public sealed record HourlyRequest(string? Line, int Hour, decimal? Quantity);
    public sealed record PlanRequest(string? Line, string? ProductCode, string? ShiftCode, decimal HourlyTarget);

    public void Map(WebApplication app)
    {
        app.MapGet("/nhap", () => WebAssets.Result("nhap.html"));

        app.MapGet("/api/nhap/info", () =>
        {
            var c = config();
            return Results.Json(new
            {
                AppName = c.ResolveAppName(),
                Enabled = c.DataEntry.Enabled && c.DataEntry.Users.Count > 0,
                Unavailable = entry.UnavailableReason
            }, BoardServer.Json);
        });

        app.MapPost("/api/nhap/login", (HttpContext http, LoginRequest request) =>
        {
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "?";
            var now = time.GetUtcNow();
            if (_failures.TryGetValue(ip, out var failed) && failed.Count >= MaxFailures && failed.Until > now)
                return Error($"Nhập sai PIN nhiều lần. Chờ {LockTime.TotalMinutes:0} phút rồi thử lại.", StatusCodes.Status429TooManyRequests);
            var user = entry.Login(request.Pin);
            if (user is null)
            {
                _failures.AddOrUpdate(ip, _ => (1, now + LockTime), (_, f) => (f.Until > now ? f.Count + 1 : 1, now + LockTime));
                return Error(config().DataEntry.Users.Count == 0 ? "Chưa có ai được nhập liệu. Thêm người ở app trên máy chủ, tab Nhập liệu." : "Sai mã PIN.",
                    StatusCodes.Status401Unauthorized);
            }
            _failures.TryRemove(ip, out _);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            _sessions[token] = new Session(user.Name, user.Pin, now + SessionTime);
            return Results.Json(new { Token = token, user.Name, user.Lines, user.Manager }, BoardServer.Json);
        });

        app.MapGet("/api/nhap/context", (HttpContext http, string? line) =>
            WithUser(http, (user, ct) => entry.GetContextAsync(user, line, ct)));

        app.MapPost("/api/nhap/hourly", (HttpContext http, HourlyRequest request) =>
            WithUser(http, (user, ct) => entry.SubmitHourlyAsync(user, Required(request.Line, "chuyền"), request.Hour, request.Quantity, ct)));

        app.MapPost("/api/nhap/plan", (HttpContext http, PlanRequest request) =>
            WithUser(http, (user, ct) => entry.SubmitPlanAsync(user, Required(request.Line, "chuyền"),
                new DayPlan(Required(request.ProductCode, "mã sản phẩm"), Required(request.ShiftCode, "mã ca"), request.HourlyTarget), ct)));

        // Thêm CancellationToken để lambda không bị hiểu là RequestDelegate (sẽ bỏ qua kết quả trả về).
        app.MapPost("/api/nhap/defect", (HttpContext http, CancellationToken _) =>
            WithUser(http, async (user, ct) =>
            {
                if (!http.Request.HasFormContentType)
                    throw new EntryException("Thiếu dữ liệu phiếu hàng lỗi.");
                AllowMediaUpload(http);
                var form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
                if (!decimal.TryParse(form["quantity"], System.Globalization.CultureInfo.InvariantCulture, out var quantity))
                    throw new EntryException("Chưa nhập số lượng.");
                var photos = await ReadPhotosAsync(form.Files, EntryService.MaxPhotos, ct).ConfigureAwait(false);
                return await entry.SubmitDefectAsync(user, Required(form["line"], "chuyền"), Required(form["defectType"], "loại lỗi"),
                    quantity, form["note"].ToString(), photos, ct).ConfigureAwait(false);
            }));

        app.MapGet("/api/nhap/jobs", (HttpContext http, CancellationToken _) =>
            WithUser(http, (user, _) => Task.FromResult(entry.Recent(user))));
    }

    private async Task<IResult> WithUser<T>(HttpContext http, Func<EntryUser, CancellationToken, Task<T>> action)
    {
        var user = Authenticate(http);
        if (user is null)
            return Error("Phiên đăng nhập đã hết. Nhập lại mã PIN.", StatusCodes.Status401Unauthorized);
        try
        {
            var result = await action(user, http.RequestAborted).ConfigureAwait(false);
            return Results.Text(JsonSerializer.Serialize(result, BoardServer.Json), "application/json; charset=utf-8");
        }
        catch (EntryException ex)
        {
            return Error(ex.Message, StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Người dùng của mã phiên, lấy theo cấu hình hiện tại (đã bị xoá hoặc đổi PIN thì không còn).</summary>
    /// <param name="allowQueryToken">
    /// Cho phép mã phiên ở tham số ?token= (chỉ dùng cho xem ảnh/video hàng lỗi): thẻ &lt;video&gt; của trình duyệt
    /// tự tải từng đoạn video và không gửi kèm header được.
    /// </param>
    internal EntryUser? Authenticate(HttpContext http, bool allowQueryToken = false)
    {
        var token = http.Request.Headers[TokenHeader].ToString();
        if (token.Length == 0 && allowQueryToken)
            token = http.Request.Query["token"].ToString();
        if (token.Length == 0 || !_sessions.TryGetValue(token, out var session))
            return null;
        var settings = config().DataEntry;
        var user = settings.Users.FirstOrDefault(u => u.Name == session.Name && u.Pin == session.Pin);
        if (!settings.Enabled || user is null || session.Expires < time.GetUtcNow())
        {
            _sessions.TryRemove(token, out _);
            return null;
        }
        return user;
    }

    private static string Required(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new EntryException($"Chưa chọn {what}.") : value.Trim();

    private static IResult Error(string message, int status) =>
        Results.Json(new { Error = message }, BoardServer.Json, statusCode: status);
}
