using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using DisplayBoard.Core.Entry;
using DisplayBoard.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DisplayBoard.Server;

/// <summary>
/// Trang nhập liệu /nhap cho tổ trưởng (bản 3.x). Đăng nhập bằng mã PIN đặt trong app, nhận một mã phiên lưu trên điện thoại.
/// Mã phiên chỉ nằm trong bộ nhớ: app khởi động lại thì nhập PIN lại. Đổi PIN hoặc xoá người thì mã phiên cũ hết hiệu lực.
/// </summary>
internal sealed class EntryApi(EntryService entry, Func<DisplayConfiguration> config, TimeProvider time)
{
    private const string TokenHeader = "X-Entry-Token";
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockTime = TimeSpan.FromMinutes(1);
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
                return Error("Nhập sai PIN nhiều lần. Chờ 1 phút rồi thử lại.", StatusCodes.Status429TooManyRequests);
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
            return Results.Json(new { Token = token, user.Name, user.Lines }, BoardServer.Json);
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
                var form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
                if (!decimal.TryParse(form["quantity"], System.Globalization.CultureInfo.InvariantCulture, out var quantity))
                    throw new EntryException("Chưa nhập số lượng.");
                if (form.Files.Count > EntryService.MaxPhotos)
                    throw new EntryException($"Mỗi lần gửi tối đa {EntryService.MaxPhotos} ảnh.");
                var photos = new List<EntryPhoto>();
                foreach (var file in form.Files)
                {
                    if (file.Length > EntryService.MaxPhotoBytes)
                        throw new EntryException($"Ảnh lớn quá {EntryService.MaxPhotoBytes / 1024 / 1024} MB.");
                    using var memory = new MemoryStream();
                    await file.CopyToAsync(memory, ct).ConfigureAwait(false);
                    photos.Add(new EntryPhoto(memory.ToArray(), file.FileName));
                }
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
    private EntryUser? Authenticate(HttpContext http)
    {
        var token = http.Request.Headers[TokenHeader].ToString();
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
