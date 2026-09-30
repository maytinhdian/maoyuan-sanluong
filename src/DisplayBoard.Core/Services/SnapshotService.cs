using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Services;

/// <summary>Đọc Excel có retry khi file bị khóa, build snapshot và thay thế nguyên khối. Lỗi thì giữ Last Good Snapshot.</summary>
public sealed class SnapshotService(
    IExcelDataReader reader,
    SnapshotBuilder builder,
    IConfigurationService configuration,
    TimeProvider time,
    ILogger<SnapshotService> logger) : ISnapshotService
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DisplayDataSnapshot? _current;

    public DisplayDataSnapshot? Current => Volatile.Read(ref _current);
    public LoadStatus Status { get; private set; } = LoadStatus.NoFileSelected;
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

    private async Task ReloadCoreAsync(CancellationToken ct)
    {
        var config = configuration.Current;
        var path = config.ExcelFile;
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus(LoadStatus.NoFileSelected, null);
            return;
        }
        if (!File.Exists(path))
        {
            logger.LogWarning("Không tìm thấy file Excel {Path}", path);
            SetStatus(LoadStatus.FileNotFound, $"Không tìm thấy file: {path}");
            return;
        }

        SetStatus(LoadStatus.Reading, null);
        var started = time.GetTimestamp();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var sheet = await reader.ReadDisplayAsync(path, config.SheetName, ct).ConfigureAwait(false);
                var content = await ReadContentAsync(config.ResolveContentFile(), ct).ConfigureAwait(false);
                var snapshot = builder.Build(sheet, content, time.GetLocalNow(), config.ResolveImagesFolder());
                Volatile.Write(ref _current, snapshot);
                LastLoadedAt = snapshot.GeneratedAt;
                logger.LogInformation("Đọc Excel xong: sheet {Sheet}, ngày {Date}, {Rows} sản phẩm, {Warnings} cảnh báo, {Elapsed} ms",
                    sheet.SheetName, sheet.Date, sheet.Lines.Count, snapshot.Warnings.Count, (int)time.GetElapsedTime(started).TotalMilliseconds);
                foreach (var warning in snapshot.Warnings)
                    logger.LogWarning("{Warning}", warning);
                SetStatus(LoadStatus.Updated, snapshot.Warnings.Count > 0 ? $"{snapshot.Warnings.Count} cảnh báo dữ liệu" : null);
                SnapshotChanged?.Invoke(this, snapshot);
                return;
            }
            catch (ExcelValidationException ex)
            {
                logger.LogError("Dữ liệu Excel không hợp lệ: {Message}", ex.Message);
                SetStatus(LoadStatus.InvalidData, ex.Message);
                return;
            }
            catch (FileNotFoundException)
            {
                SetStatus(LoadStatus.FileNotFound, $"Không tìm thấy file: {path}");
                return;
            }
            catch (IOException ex) when (attempt < MaxAttempts)
            {
                logger.LogDebug("File đang bị khóa (lần {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(RetryDelay, time, ct).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                logger.LogWarning("File vẫn bị khóa sau {Attempts} lần thử: {Message}", MaxAttempts, ex.Message);
                SetStatus(LoadStatus.FileLocked, "File đang được sử dụng, sẽ thử lại ở lần lưu tiếp theo.");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // File hỏng hoặc không phải .xlsx: ClosedXML ném nhiều loại exception khác nhau.
                logger.LogError(ex, "Không đọc được file Excel");
                SetStatus(LoadStatus.InvalidData, $"Không đọc được file Excel: {ex.Message}");
                return;
            }
        }
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
                return await reader.ReadContentAsync(path, ct).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < MaxAttempts)
            {
                await Task.Delay(RetryDelay, time, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Không đọc được file nội dung {Path}", path);
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
}
