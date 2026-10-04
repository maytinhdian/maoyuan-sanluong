using DisplayBoard.Core.Data;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Services;

/// <summary>
/// Bản 4.x: tính bảng hiển thị từ SQLite (<see cref="DisplayCalculator"/>), ghép nội dung phụ rồi thay snapshot nguyên khối.
/// Mỗi lần ghi dữ liệu là tự tính lại (gộp các lần ghi sát nhau). Lỗi thì giữ snapshot tốt gần nhất.
/// </summary>
public sealed class SnapshotService : ISnapshotService, IDisposable
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    private readonly ProductionDatabase _database;
    private readonly IExcelDataReader _reader;
    private readonly SnapshotBuilder _builder;
    private readonly IConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<SnapshotService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ITimer _debounce;
    private DisplayDataSnapshot? _current;

    public SnapshotService(ProductionDatabase database, IExcelDataReader reader, SnapshotBuilder builder, IConfigurationService configuration,
        TimeProvider time, ILogger<SnapshotService> logger)
    {
        _database = database;
        _reader = reader;
        _builder = builder;
        _configuration = configuration;
        _time = time;
        _logger = logger;
        _debounce = time.CreateTimer(_ => _ = ReloadSafeAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _database.Changed += (_, _) => _debounce.Change(ChangeDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Chờ sau lần ghi cuối rồi mới tính lại, để nhiều người bấm gửi cùng lúc chỉ tính một lần.</summary>
    public TimeSpan ChangeDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    public DisplayDataSnapshot? Current => Volatile.Read(ref _current);
    public LoadStatus Status { get; private set; } = LoadStatus.NoData;
    public string? LastError { get; private set; }
    public DateTimeOffset? LastLoadedAt { get; private set; }

    public event EventHandler<DisplayDataSnapshot>? SnapshotChanged;
    public event EventHandler? StatusChanged;

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ReloadCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReloadSafeAsync()
    {
        try
        {
            await ReloadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tính lại bảng hiển thị");
        }
    }

    private async Task ReloadCoreAsync(CancellationToken ct)
    {
        var config = _configuration.Current;
        var started = _time.GetTimestamp();
        ProductionData data;
        try
        {
            data = _database.Store.Load();
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(LoadStatus.InvalidData, ex.Message);
            return;
        }
        if (data.Lines.Count == 0)
        {
            SetStatus(LoadStatus.NoData, "Chưa có danh sách chuyền. Nhập dữ liệu từ file Excel cũ hoặc khai báo ở trang quản lý.");
            return;
        }

        try
        {
            var sheet = DisplayCalculator.Compute(data);
            var content = MergeProducts(await ReadContentAsync(config.ResolveContentFile(), ct).ConfigureAwait(false), data);
            var snapshot = _builder.Build(sheet, content, _time.GetLocalNow(), config.ResolveImagesFolder());
            Volatile.Write(ref _current, snapshot);
            LastLoadedAt = snapshot.GeneratedAt;
            _logger.LogInformation("Tính bảng hiển thị xong: ngày {Date}, {Rows} chuyền, {Elapsed} ms",
                sheet.Date, sheet.Lines.Count, (int)_time.GetElapsedTime(started).TotalMilliseconds);
            SetStatus(LoadStatus.Updated, snapshot.Warnings.Count > 0 ? $"{snapshot.Warnings.Count} cảnh báo dữ liệu" : null);
            SnapshotChanged?.Invoke(this, snapshot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Không tính được bảng hiển thị");
            SetStatus(LoadStatus.InvalidData, $"Không tính được bảng hiển thị: {ex.Message}");
        }
    }

    /// <summary>Tên sản phẩm khai báo trong danh mục hiện trên TV, trừ khi file nội dung phụ đặt tên khác.</summary>
    private static ContentData MergeProducts(ContentData content, ProductionData data)
    {
        var products = content.Products.ToList();
        foreach (var p in data.Products.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
        {
            var index = products.FindIndex(i => ProductionData.Same(i.ProductCode, p.Code));
            if (index < 0)
                products.Add(new ProductInfo(p.Code, p.Name, null, null, null));
            else if (string.IsNullOrWhiteSpace(products[index].DisplayName))
                products[index] = products[index] with { DisplayName = p.Name };
        }
        return content with { Products = products };
    }

    /// <summary>File nội dung phụ là tùy chọn: thiếu hoặc lỗi thì chạy tiếp với nội dung rỗng + cảnh báo.</summary>
    private async Task<ContentData> ReadContentAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return ContentData.Empty;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _reader.ReadContentAsync(path, ct).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < MaxAttempts)
            {
                await Task.Delay(RetryDelay, _time, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Không đọc được file nội dung {Path}", path);
                return ContentData.Empty with { Warnings = [$"Không đọc được file nội dung {Path.GetFileName(path)}: {ex.Message}"] };
            }
        }
    }

    private void SetStatus(LoadStatus status, string? error)
    {
        Status = status;
        LastError = error;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _debounce.Dispose();
        _gate.Dispose();
    }
}
