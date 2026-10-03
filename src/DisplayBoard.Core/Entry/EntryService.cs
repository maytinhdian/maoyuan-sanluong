using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Entry;

/// <summary>Ghi file Excel nhập liệu. Bản chạy thật điều khiển chính Excel trên máy chủ để công thức tự tính lại.</summary>
public interface IWorkbookHost
{
    /// <summary>Null = ghi được. Khác null = lý do không ghi được (vd máy chưa cài Excel).</summary>
    string? UnavailableReason { get; }

    /// <summary>Mở file, chạy <paramref name="action"/>, lưu. Ném <see cref="WorkbookBusyException"/> khi Excel đang bận.</summary>
    Task RunAsync(string path, Action<IEntryWorkbook> action, CancellationToken ct = default);
}

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
/// Nhập liệu qua trình duyệt: kiểm tra số liệu, xếp hàng các lần ghi và ghi lần lượt vào file Excel.
/// Excel bận (có người đang sửa ô) thì phiếu chờ và tự ghi lại; người nhập xem trạng thái ở tab "Đã gửi".
/// </summary>
public sealed class EntryService : IDisposable
{
    public const int MaxPhotos = 4;
    public const int MaxPhotoBytes = 8 * 1024 * 1024;
    public const int MaxNoteLength = 200;
    private const decimal MaxQuantity = 100_000;
    private const int HistoryLimit = 300;

    private readonly IConfigurationService _config;
    private readonly IWorkbookHost _host;
    private readonly TimeProvider _time;
    private readonly ILogger<EntryService> _logger;
    private readonly Channel<Job> _queue = Channel.CreateUnbounded<Job>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentQueue<Job> _history = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private (string Path, DateTime Written, long Length, ClosedXmlEntryWorkbook Workbook)? _cache;
    private DateTimeOffset? _lastWriteAt;
    private string? _lastError;
    private string? _waitingReason;

    public EntryService(IConfigurationService config, IWorkbookHost host, TimeProvider time, ILogger<EntryService> logger)
    {
        _config = config;
        _host = host;
        _time = time;
        _logger = logger;
        _worker = Task.Run(() => ProcessAsync(_stop.Token));
    }

    /// <summary>Excel bận thì thử lại sau khoảng này.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Chờ Excel quá lâu thì báo lỗi để người nhập biết mà nhập lại.</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(15);

    public event EventHandler? Changed;

    public EntryQueueStatus Status => new(
        _history.Count(j => j.Status is EntryJobStatus.Pending or EntryJobStatus.Waiting), _lastWriteAt, _lastError, _waitingReason);

    public string? UnavailableReason =>
        string.IsNullOrWhiteSpace(_config.Current.ExcelFile) ? "Máy chủ chưa chọn file Excel." : _host.UnavailableReason;

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

    public async Task<EntryContext> GetContextAsync(EntryUser user, string? line, CancellationToken ct = default)
    {
        var now = _time.GetLocalNow();
        var workbook = await OpenForReadAsync(ct).ConfigureAwait(false);
        await _readGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return ProductionWorkbook.ReadContext(workbook, DateOnly.FromDateTime(now.DateTime), TimeOnly.FromDateTime(now.DateTime), user.Lines, line);
        }
        finally
        {
            _readGate.Release();
        }
    }

    public IReadOnlyList<EntryJobInfo> Recent(EntryUser? user, int max = 30) =>
        _history.Reverse().Where(j => user is null || j.User == user.Name).Take(max).Select(j => j.Info()).ToList();

    public EntryJobInfo? Find(Guid id) => _history.FirstOrDefault(j => j.Id == id)?.Info();

    // ---------- Gửi ----------

    public async Task<EntryJobInfo> SubmitHourlyAsync(EntryUser user, string line, int hour, decimal? quantity, CancellationToken ct = default)
    {
        var context = await CheckAsync(user, line, ct).ConfigureAwait(false);
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

        var date = context.Date;
        var summary = quantity is null ? $"Xoá sản lượng giờ {hour}" : $"Sản lượng giờ {hour} · {quantity:N0}";
        return Enqueue(user, day.Line, "hourly", summary, wb => ProductionWorkbook.WriteHour(wb, date, day.Line, hour, quantity));
    }

    public async Task<EntryJobInfo> SubmitPlanAsync(EntryUser user, string line, DayPlan plan, CancellationToken ct = default)
    {
        var context = await CheckAsync(user, line, ct).ConfigureAwait(false);
        var product = context.Products.Count == 0 ? plan.ProductCode.Trim()
            : context.Products.FirstOrDefault(p => ProductionWorkbook.Same(p.Code, plan.ProductCode))?.Code
              ?? throw new EntryException($"Mã sản phẩm \"{plan.ProductCode}\" không có trong DANH_SACH_SAN_PHAM.");
        var shift = context.Shifts.FirstOrDefault(s => ProductionWorkbook.Same(s.Code, plan.ShiftCode))?.Code
            ?? throw new EntryException($"Mã ca \"{plan.ShiftCode}\" không có trong CAU_HINH_CA.");
        if (plan.HourlyTarget is <= 0 or > MaxQuantity)
            throw new EntryException("Mục tiêu mỗi giờ phải lớn hơn 0.");
        if (product.Length == 0)
            throw new EntryException("Chưa chọn mã sản phẩm.");

        var date = context.Date;
        var day = context.Line!;
        var fixedPlan = new DayPlan(product, shift, plan.HourlyTarget);
        return Enqueue(user, day.Line, "plan", $"Kế hoạch · {product} · ca {shift} · {plan.HourlyTarget:N0}/giờ",
            wb => ProductionWorkbook.EnsureDayRow(wb, date, day.Line, fixedPlan));
    }

    public async Task<EntryJobInfo> SubmitDefectAsync(
        EntryUser user, string line, string defectType, decimal quantity, string? note, IReadOnlyList<EntryPhoto> photos, CancellationToken ct = default)
    {
        var context = await CheckAsync(user, line, ct).ConfigureAwait(false);
        var day = context.Line!;
        if (!day.HasRow && day.Plan is null)
            throw new EntryException($"{day.Line} chưa có kế hoạch hôm nay. Hãy nhập kế hoạch ở tab Sản lượng trước.");
        var type = context.DefectTypes.Count == 0 ? defectType.Trim()
            : context.DefectTypes.FirstOrDefault(t => ProductionWorkbook.Same(t, defectType))
              ?? throw new EntryException($"Loại lỗi \"{defectType}\" không có trong DANH_SACH_LOAI_LOI.");
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
        var record = new DefectRecord(context.Date, new TimeOnly(now.Hour, now.Minute), day.Line, type, quantity,
            names.Count == 0 ? null : string.Join("; ", names), note);
        var summary = $"Hàng lỗi · {type} · {quantity:N0} cái" + (names.Count > 0 ? $" · {names.Count} ảnh" : "");
        return Enqueue(user, day.Line, "defect", summary, wb => ProductionWorkbook.AddDefect(wb, record));
    }

    private async Task<EntryContext> CheckAsync(EntryUser user, string line, CancellationToken ct)
    {
        if (UnavailableReason is { } reason)
            throw new EntryException(reason);
        if (!ProductionWorkbook.Allowed(user.Lines, line))
            throw new EntryException($"{user.Name} không được nhập cho {line}.");
        var context = await GetContextAsync(user, line, ct).ConfigureAwait(false);
        if (context.Line is null)
            throw new EntryException($"Không có \"{line}\" trong DANH_SACH_CHUYEN.");
        return context;
    }

    /// <summary>Lưu ảnh vào images\hang_loi cạnh file Excel. Chỉ nhận JPEG/PNG (kiểm tra nội dung, không tin đuôi file).</summary>
    private async Task<IReadOnlyList<string>> SavePhotosAsync(string line, DateTime now, IReadOnlyList<EntryPhoto> photos, CancellationToken ct)
    {
        if (photos.Count == 0)
            return [];
        var folder = _config.Current.ResolveImagesFolder() ?? throw new EntryException("Máy chủ chưa chọn file Excel.");
        folder = Path.Combine(folder, ImageResolver.DefectFolder);
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

    // ---------- Hàng đợi ----------

    private EntryJobInfo Enqueue(EntryUser user, string line, string kind, string summary, Action<IEntryWorkbook> apply)
    {
        var job = new Job(Guid.NewGuid(), user.Name, line, kind, summary, _time.GetLocalNow(), apply);
        _history.Enqueue(job);
        while (_history.Count > HistoryLimit && _history.TryPeek(out var old) && old.Status is EntryJobStatus.Done or EntryJobStatus.Failed)
            _history.TryDequeue(out _);
        _queue.Writer.TryWrite(job);
        _logger.LogInformation("Nhập liệu: {User} gửi {Summary} ({Line})", user.Name, summary, line);
        Changed?.Invoke(this, EventArgs.Empty);
        return job.Info();
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                await RunJobAsync(job, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Ghi lần lượt từng phiếu. Phiếu đang chờ Excel thì các phiếu sau chờ theo, để giữ đúng thứ tự nhập.</summary>
    private async Task RunJobAsync(Job job, CancellationToken ct)
    {
        while (true)
        {
            var path = _config.Current.ExcelFile;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new EntryException("Máy chủ chưa chọn file Excel.");
                await _host.RunAsync(path, job.Apply, ct).ConfigureAwait(false);
                job.Set(EntryJobStatus.Done, null, _time.GetLocalNow());
                _lastWriteAt = _time.GetLocalNow();
                _lastError = null;
                _waitingReason = null;
                _logger.LogInformation("Đã ghi vào Excel: {Summary} ({Line}, {User})", job.Summary, job.Line, job.User);
            }
            catch (WorkbookBusyException ex)
            {
                if (_time.GetLocalNow() - job.CreatedAt > MaxWait)
                {
                    Fail(job, $"Chờ Excel quá {MaxWait.TotalMinutes:0} phút nên chưa ghi được. {ex.Message} Hãy nhập lại.");
                }
                else
                {
                    _waitingReason = ex.Message;
                    if (job.Status != EntryJobStatus.Waiting)
                    {
                        job.Set(EntryJobStatus.Waiting, ex.Message, _time.GetLocalNow());
                        _logger.LogInformation("Excel đang bận, chờ để ghi {Summary}: {Reason}", job.Summary, ex.Message);
                        Changed?.Invoke(this, EventArgs.Empty);
                    }
                    await Task.Delay(RetryInterval, _time, ct).ConfigureAwait(false);
                    continue;
                }
            }
            catch (EntryException ex)
            {
                Fail(job, ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi ghi vào Excel: {Summary}", job.Summary);
                Fail(job, "Lỗi khi ghi vào Excel: " + ex.Message);
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
    }

    private void Fail(Job job, string message)
    {
        job.Set(EntryJobStatus.Failed, message, _time.GetLocalNow());
        _lastError = message;
        _waitingReason = null;
        _logger.LogWarning("Không ghi được {Summary} ({Line}): {Message}", job.Summary, job.Line, message);
    }

    /// <summary>Đọc file đã lưu (Excel đang mở vẫn đọc được). File chưa đổi thì dùng lại bản đã đọc.</summary>
    private async Task<ClosedXmlEntryWorkbook> OpenForReadAsync(CancellationToken ct)
    {
        var path = _config.Current.ExcelFile;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new EntryException("Máy chủ chưa chọn file Excel hoặc không tìm thấy file.");
        var info = new FileInfo(path);
        var cache = _cache;
        if (cache is { } c && c.Path == path && c.Written == info.LastWriteTimeUtc && c.Length == info.Length)
            return c.Workbook;
        try
        {
            var workbook = await ClosedXmlEntryWorkbook.OpenReadAsync(path, ct).ConfigureAwait(false);
            _cache = (path, info.LastWriteTimeUtc, info.Length, workbook);
            return workbook;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (cache is { } old && old.Path == path)
                return old.Workbook;
            throw new EntryException("Không đọc được file Excel: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _queue.Writer.TryComplete();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _stop.Dispose();
        _readGate.Dispose();
    }

    private sealed class Job(Guid id, string user, string line, string kind, string summary, DateTimeOffset createdAt, Action<IEntryWorkbook> apply)
    {
        private readonly Lock _lock = new();
        private string? _message;
        private DateTimeOffset _updatedAt = createdAt;

        public Guid Id { get; } = id;
        public string User { get; } = user;
        public string Line { get; } = line;
        public string Summary { get; } = summary;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public Action<IEntryWorkbook> Apply { get; } = apply;
        public EntryJobStatus Status { get; private set; } = EntryJobStatus.Pending;

        public void Set(EntryJobStatus status, string? message, DateTimeOffset at)
        {
            lock (_lock)
                (Status, _message, _updatedAt) = (status, message, at);
        }

        public EntryJobInfo Info()
        {
            lock (_lock)
                return new EntryJobInfo(Id, User, Line, kind, Summary, CreatedAt, Status, _message, _updatedAt);
        }
    }
}
