using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DisplayBoard.Core.Data;
using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Entry;

public enum EntryJobStatus
{
    Pending,
    Waiting,
    Done,
    Failed
}

public sealed record EntryJobInfo(
    Guid Id,
    string User,
    string Line,
    string Kind,
    string Summary,
    DateTimeOffset CreatedAt,
    EntryJobStatus Status,
    string? Message,
    DateTimeOffset UpdatedAt);

public sealed record EntryQueueStatus(int Pending, DateTimeOffset? LastWriteAt, string? LastError, string? WaitingReason);

/// <summary>Ảnh gửi lên từ điện thoại.</summary>
public sealed record EntryPhoto(byte[] Content, string FileName);

/// <summary>
/// Nhập liệu qua trình duyệt (trang /nhap): kiểm tra số liệu rồi ghi thẳng vào SQLite. Bản 4.x không còn hàng đợi chờ Excel:
/// mỗi lần gửi ghi xong ngay (vài phần nghìn giây). Lịch sử gửi vẫn giữ để người nhập xem ở tab "Đã gửi".
/// Tổ trưởng chỉ nhập cho ngày hôm nay; sửa ngày cũ do quản lý làm ở trang /quan-ly.
/// </summary>
public sealed class EntryService
{
    public const int MaxPhotos = 4;
    public const int MaxPhotoBytes = 8 * 1024 * 1024;
    public const int MaxNoteLength = 200;
    private const decimal MaxQuantity = 100_000;
    private const int HistoryLimit = 300;

    private readonly IConfigurationService _config;
    private readonly ProductionDatabase _database;
    private readonly TimeProvider _time;
    private readonly ILogger<EntryService> _logger;
    private readonly ConcurrentQueue<EntryJobInfo> _history = new();
    private DateTimeOffset? _lastWriteAt;
    private string? _lastError;

    public EntryService(IConfigurationService config, ProductionDatabase database, TimeProvider time, ILogger<EntryService> logger)
    {
        _config = config;
        _database = database;
        _time = time;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public EntryQueueStatus Status => new(0, _lastWriteAt, _lastError, null);

    public string? UnavailableReason
    {
        get
        {
            try
            {
                return _database.Store.Load().Lines.Count == 0 ? "Máy chủ chưa có danh sách chuyền." : null;
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message;
            }
        }
    }

    // ---------- Đăng nhập ----------

    public EntryUser? Login(string? pin)
    {
        var settings = _config.Current.DataEntry;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(pin))
            return null;
        var given = Encoding.UTF8.GetBytes(pin.Trim());
        return settings.Users.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.Pin)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(u.Pin.Trim()), given));
    }

    // ---------- Đọc ----------

    public Task<EntryContext> GetContextAsync(EntryUser user, string? line, CancellationToken ct = default)
    {
        var now = _time.GetLocalNow().DateTime;
        var data = Data();
        return Task.FromResult(BuildContext(data, DateOnly.FromDateTime(now), TimeOnly.FromDateTime(now), user.Lines, line));
    }

    public static EntryContext BuildContext(ProductionData data, DateOnly date, TimeOnly now, IReadOnlyCollection<string>? allowedLines, string? line)
    {
        var shifts = data.Shifts.Where(s => s.Active).Select(ToShiftInfo).ToList();
        var lines = data.Lines.Where(l => l.Active && Allowed(allowedLines, l.Name)).ToList();
        var selected = line is null ? null : lines.FirstOrDefault(l => ProductionData.Same(l.Name, line) || ProductionData.Same(l.Code, line));
        return new EntryContext(
            date,
            lines.Select(l => l.Name).ToList(),
            data.Products.Where(p => p.Active).Select(p => new ProductOption(p.Code, p.Name)).ToList(),
            shifts,
            data.DefectTypes.Select(t => t.Name).ToList(),
            selected is null ? null : ReadLineDay(data, date, now, selected));
    }

    public static LineDay ReadLineDay(ProductionData data, DateOnly date, TimeOnly now, LineDef line)
    {
        var entry = DisplayCalculator.Entry(data, date, line.Code);
        DayPlan? plan = entry is null ? null : Plan(entry);
        DateOnly? copiedFrom = null;
        if (entry is null && PreviousPlan(data, date, line.Code) is { } previous)
            (plan, copiedFrom) = (previous.Plan, previous.Date);

        var shift = plan is null ? null : data.Shifts.FirstOrDefault(s => ProductionData.Same(s.Code, plan.ShiftCode));
        var slots = shift?.Slots() ?? [];
        var slotCount = slots.Count > 0 ? slots.Count : DayEntry.MaxHours;
        var hours = new List<HourValue>();
        for (var n = 1; n <= slotCount; n++)
        {
            var slot = slots.ElementAtOrDefault(n - 1);
            decimal? target = plan is null ? null : Math.Round(plan.HourlyTarget * (decimal)(slot?.Hours ?? 1), 0, MidpointRounding.AwayFromZero);
            hours.Add(new HourValue(n, slot?.Start, slot?.End, entry?.Hours.ElementAtOrDefault(n - 1), target));
        }
        decimal? dailyTarget = plan is null || shift is null ? null : DisplayCalculator.Round(plan.HourlyTarget * shift.Hours);
        return new LineDay(line.Name, date, entry is not null, plan, copiedFrom, hours, hours.Sum(h => h.Quantity ?? 0), dailyTarget,
            ProductionWorkbook.SuggestHour(hours, now));
    }

    public IReadOnlyList<EntryJobInfo> Recent(EntryUser? user, int max = 30) =>
        _history.Reverse().Where(j => user is null || j.User == user.Name).Take(max).ToList();

    public EntryJobInfo? Find(Guid id) => _history.FirstOrDefault(j => j.Id == id);

    // ---------- Gửi ----------

    public async Task<EntryJobInfo> SubmitHourlyAsync(EntryUser user, string line, int hour, decimal? quantity, CancellationToken ct = default)
    {
        var (context, def) = await CheckAsync(user, line, ct).ConfigureAwait(false);
        var day = context.Line!;
        if (day.Plan is null)
            throw new EntryException($"{day.Line} chưa có kế hoạch hôm nay. Hãy chọn mã sản phẩm, ca và mục tiêu mỗi giờ trước.");
        var slot = day.Hours.FirstOrDefault(h => h.Number == hour)
            ?? throw new EntryException($"Ca {day.Plan.ShiftCode} không có giờ {hour}.");
        if (quantity is < 0)
            throw new EntryException("Số sản phẩm không được âm.");
        var limit = slot.Target is > 0 ? Math.Max(slot.Target.Value * 3, 10) : MaxQuantity;
        if (quantity > limit)
            throw new EntryException($"{quantity:N0} lớn hơn 3 lần mục tiêu giờ này ({slot.Target:N0}). Kiểm tra lại số.");

        var summary = quantity is null ? $"Xoá sản lượng giờ {hour}" : $"Sản lượng giờ {hour} · {quantity:N0}";
        return Run(user, day.Line, "hourly", summary, store =>
        {
            EnsureRow(store, context.Date, def, day, user);
            store.SetHour(context.Date, def.Code, hour, quantity, user.Name);
        });
    }

    public async Task<EntryJobInfo> SubmitPlanAsync(EntryUser user, string line, DayPlan plan, CancellationToken ct = default)
    {
        var (context, def) = await CheckAsync(user, line, ct).ConfigureAwait(false);
        var product = context.Products.FirstOrDefault(p => ProductionData.Same(p.Code, plan.ProductCode))?.Code
            ?? throw new EntryException($"Mã sản phẩm \"{plan.ProductCode}\" không có trong danh sách sản phẩm.");
        var shift = context.Shifts.FirstOrDefault(s => ProductionData.Same(s.Code, plan.ShiftCode))?.Code
            ?? throw new EntryException($"Mã ca \"{plan.ShiftCode}\" không có trong danh sách ca.");
        if (plan.HourlyTarget is <= 0 or > MaxQuantity)
            throw new EntryException("Mục tiêu mỗi giờ phải lớn hơn 0.");

        var day = context.Line!;
        return Run(user, day.Line, "plan", $"Kế hoạch · {product} · ca {shift} · {plan.HourlyTarget:N0}/giờ",
            store => store.SavePlan(context.Date, def.Code, product, shift, plan.HourlyTarget, user.Name));
    }

    public async Task<EntryJobInfo> SubmitDefectAsync(
        EntryUser user, string line, string defectType, decimal quantity, string? note, IReadOnlyList<EntryPhoto> photos, CancellationToken ct = default)
    {
        var (context, def) = await CheckAsync(user, line, ct).ConfigureAwait(false);
        var day = context.Line!;
        if (!day.HasRow && day.Plan is null)
            throw new EntryException($"{day.Line} chưa có kế hoạch hôm nay. Hãy nhập kế hoạch ở tab Sản lượng trước.");
        var type = context.DefectTypes.Count == 0 ? defectType.Trim()
            : context.DefectTypes.FirstOrDefault(t => ProductionData.Same(t, defectType))
              ?? throw new EntryException($"Loại lỗi \"{defectType}\" không có trong danh sách loại lỗi.");
        if (type.Length == 0)
            throw new EntryException("Chưa chọn loại lỗi.");
        if (quantity is <= 0 or > MaxQuantity)
            throw new EntryException("Số lượng hàng lỗi phải lớn hơn 0.");
        if (note?.Length > MaxNoteLength)
            throw new EntryException($"Ghi chú dài quá {MaxNoteLength} ký tự.");
        if (photos.Count > MaxPhotos)
            throw new EntryException($"Mỗi lần gửi tối đa {MaxPhotos} ảnh.");

        var now = _time.GetLocalNow().DateTime;
        var names = await SavePhotosAsync(day.Line, now, photos, ct).ConfigureAwait(false);
        var row = new DefectRow
        {
            Date = context.Date,
            Time = new TimeOnly(now.Hour, now.Minute),
            LineCode = def.Code,
            DefectType = type,
            Quantity = quantity,
            ImageFiles = names.Count == 0 ? null : string.Join("; ", names),
            Note = note,
        };
        var summary = $"Hàng lỗi · {type} · {quantity:N0} cái" + (names.Count > 0 ? $" · {names.Count} ảnh" : "");
        return Run(user, day.Line, "defect", summary, store =>
        {
            EnsureRow(store, context.Date, def, day, user);
            store.AddDefect(row, user.Name);
        });
    }

    /// <summary>Chưa có dòng hôm nay nhưng có kế hoạch của ngày làm trước: tạo dòng với kế hoạch đó (như bản Excel).</summary>
    private static void EnsureRow(ProductionStore store, DateOnly date, LineDef line, LineDay day, EntryUser user)
    {
        if (!day.HasRow && day.Plan is { } plan)
            store.SavePlan(date, line.Code, plan.ProductCode, plan.ShiftCode, plan.HourlyTarget, user.Name);
    }

    private async Task<(EntryContext Context, LineDef Line)> CheckAsync(EntryUser user, string line, CancellationToken ct)
    {
        if (UnavailableReason is { } reason)
            throw new EntryException(reason);
        if (!Allowed(user.Lines, line))
            throw new EntryException($"{user.Name} không được nhập cho {line}.");
        var context = await GetContextAsync(user, line, ct).ConfigureAwait(false);
        if (context.Line is null)
            throw new EntryException($"Không có \"{line}\" trong danh sách chuyền.");
        return (context, Data().FindLine(context.Line.Line)!);
    }

    private EntryJobInfo Run(EntryUser user, string line, string kind, string summary, Action<ProductionStore> write)
    {
        var now = _time.GetLocalNow();
        EntryJobInfo job;
        try
        {
            write(_database.Store);
            job = new EntryJobInfo(Guid.NewGuid(), user.Name, line, kind, summary, now, EntryJobStatus.Done, null, now);
            _lastWriteAt = now;
            _lastError = null;
            _logger.LogInformation("Nhập liệu: {User} ghi {Summary} ({Line})", user.Name, summary, line);
        }
        catch (InvalidOperationException ex)
        {
            _lastError = ex.Message;
            _logger.LogWarning("Không ghi được {Summary} ({Line}): {Message}", summary, line, ex.Message);
            throw new EntryException(ex.Message);
        }
        _history.Enqueue(job);
        while (_history.Count > HistoryLimit)
            _history.TryDequeue(out _);
        Changed?.Invoke(this, EventArgs.Empty);
        return job;
    }

    /// <summary>Lưu ảnh vào images\hang_loi trong thư mục dữ liệu. Chỉ nhận JPEG/PNG (kiểm tra nội dung, không tin đuôi file).</summary>
    private async Task<IReadOnlyList<string>> SavePhotosAsync(string line, DateTime now, IReadOnlyList<EntryPhoto> photos, CancellationToken ct)
    {
        if (photos.Count == 0)
            return [];
        var folder = Path.Combine(_config.Current.ResolveImagesFolder()!, ImageResolver.DefectFolder);
        Directory.CreateDirectory(folder);
        var slug = SheetTable.NormalizeHeader(line);
        var names = new List<string>();
        for (var i = 0; i < photos.Count; i++)
        {
            var photo = photos[i];
            if (photo.Content.Length > MaxPhotoBytes)
                throw new EntryException($"Ảnh {i + 1} lớn quá {MaxPhotoBytes / 1024 / 1024} MB.");
            var ext = ImageExtension(photo.Content) ?? throw new EntryException($"Ảnh {i + 1} không phải ảnh JPG/PNG.");
            var name = $"{slug}_{now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}_{i + 1}{ext}";
            for (var n = 2; File.Exists(Path.Combine(folder, name)); n++)
                name = $"{slug}_{now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}_{i + 1}-{n}{ext}";
            await File.WriteAllBytesAsync(Path.Combine(folder, name), photo.Content, ct).ConfigureAwait(false);
            names.Add(name);
        }
        return names;
    }

    private static string? ImageExtension(byte[] bytes) =>
        bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF ? ".jpg"
        : bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 ? ".png"
        : null;

    private ProductionData Data()
    {
        try
        {
            return _database.Store.Load();
        }
        catch (InvalidOperationException ex)
        {
            throw new EntryException(ex.Message);
        }
    }

    public static bool Allowed(IReadOnlyCollection<string>? allowedLines, string line) =>
        allowedLines is null || allowedLines.Count == 0 || allowedLines.Any(a => ProductionData.Same(a, line));

    private static ShiftInfo ToShiftInfo(ShiftDef s) => new(s.Code, s.Name, s.Hours, s.Slots());

    private static DayPlan? Plan(DayEntry e) =>
        e.ProductCode is null || e.ShiftCode is null || e.HourlyTarget is null ? null : new DayPlan(e.ProductCode, e.ShiftCode, e.HourlyTarget.Value);

    /// <summary>Kế hoạch đủ (mã sản phẩm, ca, mục tiêu) của ngày gần nhất trước <paramref name="date"/>.</summary>
    private static (DayPlan Plan, DateOnly Date)? PreviousPlan(ProductionData data, DateOnly date, string lineCode) =>
        data.Entries.Where(e => e.Date < date && ProductionData.Same(e.LineCode, lineCode))
            .OrderByDescending(e => e.Date)
            .Select(e => Plan(e) is { } p ? (p, e.Date) : ((DayPlan, DateOnly)?)null)
            .FirstOrDefault(p => p is not null);
}
