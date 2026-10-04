using System.Globalization;
using ClosedXML.Excel;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Data;

/// <summary>File Excel xuất ra: tên gợi ý và nội dung.</summary>
public sealed record ExportFile(string FileName, byte[] Content);

/// <summary>Kết quả nhập file Excel 3.x: dữ liệu đọc được, cảnh báo, và bảng so số Excel với số app tính.</summary>
public sealed record ImportPreview(string Path, ImportResult Result, ImportCheck Check);

/// <summary>
/// Việc định kỳ của máy chủ bản 4.x: sao lưu cơ sở dữ liệu mỗi ngày (giữ <see cref="DisplayConfiguration.BackupCount"/> bản)
/// và tự xuất file Excel mỗi ngày lúc <see cref="ExcelExportSettings.Time"/>. Cũng là chỗ xuất Excel theo ngày/tháng
/// cho nút bấm trên app và trang quản lý, và nhập dữ liệu cũ từ file Excel V20.
/// </summary>
public sealed class DataMaintenance : IDisposable
{
    public const string BackupPrefix = "sanluong_";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private readonly ProductionDatabase _database;
    private readonly IConfigurationService _config;
    private readonly TimeProvider _time;
    private readonly ILogger<DataMaintenance> _logger;
    private readonly Lock _lock = new();
    private ITimer? _timer;

    public DataMaintenance(ProductionDatabase database, IConfigurationService config, TimeProvider time, ILogger<DataMaintenance> logger)
    {
        _database = database;
        _config = config;
        _time = time;
        _logger = logger;
    }

    /// <summary>Lần tự xuất Excel gần nhất (đường dẫn file) và lỗi gần nhất, để hiện trên app.</summary>
    public string? LastExport { get; private set; }
    public string? LastBackup { get; private set; }
    public string? LastError { get; private set; }

    public event EventHandler? Changed;

    public ProductionDatabase Database => _database;

    /// <summary>Bắt đầu kiểm tra mỗi phút. Chạy một lần ngay để sao lưu khi vừa mở app.</summary>
    public void Start() => _timer ??= _time.CreateTimer(_ => RunDue(), null, TimeSpan.Zero, CheckInterval);

    // ---------- Xuất Excel ----------

    /// <summary>File Excel của một ngày: mọi dữ liệu đến hết ngày đó, HIEN_THI hiện ngày đó.</summary>
    public ExportFile ExportDay(DateOnly date) => new(ExcelExporter.FileName(date), Export(_database.Store.Load(), date));

    /// <summary>File Excel của một tháng: dữ liệu đến hết tháng (hoặc hôm nay nếu là tháng này), báo cáo tháng hiện tháng đó.</summary>
    public ExportFile ExportMonth(DateOnly month)
    {
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var end = new DateOnly(month.Year, month.Month, 1).AddMonths(1).AddDays(-1);
        return new($"SanLuong_{month:yyyy-MM}.xlsx", Export(_database.Store.Load(), end < today ? end : today));
    }

    private static byte[] Export(ProductionData data, DateOnly upTo)
    {
        using var memory = new MemoryStream();
        ExcelExporter.Export(data, upTo, memory);
        return memory.ToArray();
    }

    /// <summary>Ghi file Excel của ngày vào thư mục xuất. Trả về đường dẫn file.</summary>
    public string ExportDayToFolder(DateOnly date)
    {
        var folder = _config.Current.ResolveExportFolder();
        Directory.CreateDirectory(folder);
        var file = ExportDay(date);
        var path = Path.Combine(folder, file.FileName);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, file.Content);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    // ---------- Sao lưu ----------

    /// <summary>Sao lưu vào SaoLuu\sanluong_yyyy-MM-dd.db rồi xoá bớt bản cũ. Trả về đường dẫn bản sao lưu.</summary>
    public string BackupNow()
    {
        var config = _config.Current;
        var folder = config.ResolveBackupFolder();
        Directory.CreateDirectory(folder);
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var path = Path.Combine(folder, $"{BackupPrefix}{today:yyyy-MM-dd}.db");
        _database.Store.Backup(path);
        foreach (var old in Backups(folder).Skip(Math.Max(1, config.BackupCount)))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Không xoá được bản sao lưu cũ {Path}", old);
            }
        }
        return path;
    }

    /// <summary>Các bản sao lưu, mới nhất trước.</summary>
    public static IReadOnlyList<string> Backups(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, BackupPrefix + "*.db").Where(f => BackupDate(f) is not null).OrderByDescending(BackupDate).ToList()
            : [];

    private static DateOnly? BackupDate(string path) =>
        DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(path)[BackupPrefix.Length..], "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;

    // ---------- Định kỳ ----------

    /// <summary>Sao lưu nếu hôm nay chưa sao lưu; tự xuất Excel nếu đã qua giờ xuất mà hôm nay chưa xuất.</summary>
    public void RunDue()
    {
        if (!_lock.TryEnter())
            return;
        try
        {
            var config = _config.Current;
            var now = _time.GetLocalNow().DateTime;
            var today = DateOnly.FromDateTime(now);
            if (_database.TryLoad().Lines.Count == 0)
                return;   // chưa có dữ liệu thì chưa cần sao lưu/xuất

            var backup = Path.Combine(config.ResolveBackupFolder(), $"{BackupPrefix}{today:yyyy-MM-dd}.db");
            if (!File.Exists(backup))
            {
                LastBackup = BackupNow();
                _logger.LogInformation("Sao lưu dữ liệu vào {Path}", LastBackup);
                Changed?.Invoke(this, EventArgs.Empty);
            }

            var export = config.Export;
            var due = today.ToDateTime(export.ResolveTime());
            var file = Path.Combine(config.ResolveExportFolder(), ExcelExporter.FileName(today));
            if (export.Enabled && now >= due && (!File.Exists(file) || File.GetLastWriteTime(file) < due))
            {
                LastExport = ExportDayToFolder(today);
                _logger.LogInformation("Tự xuất Excel {Path}", LastExport);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Lỗi khi sao lưu hoặc tự xuất Excel");
            LastError = ex.Message;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _lock.Exit();
        }
    }

    // ---------- Nhập dữ liệu cũ ----------

    /// <summary>Đọc file Excel V20 (bản 3.x) và so số: chưa ghi gì vào cơ sở dữ liệu.</summary>
    public static ImportPreview PreviewImport(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var result = V20Importer.Read(workbook);
        return new ImportPreview(path, result, V20Importer.Check(workbook, result.Data));
    }

    /// <summary>
    /// Thay toàn bộ dữ liệu bằng dữ liệu đọc từ file Excel (sao lưu bản hiện có trước). Chép kèm thư mục ảnh và file nội dung phụ
    /// nằm cạnh file Excel sang thư mục dữ liệu, file đã có thì giữ nguyên.
    /// </summary>
    public void ApplyImport(ImportPreview preview, string user)
    {
        if (!_database.Store.IsEmpty)
            BackupNow();
        _database.Store.ReplaceAll(preview.Result.Data, user, Path.GetFileName(preview.Path));

        _logger.LogInformation("Nhập dữ liệu từ {Path}: {Entries} dòng nhập liệu, {Defects} phiếu lỗi", preview.Path,
            preview.Result.Data.Entries.Count, preview.Result.Data.Defects.Count);

        // File tải lên qua trình duyệt chỉ có tên file, không có thư mục gốc để chép ảnh theo.
        if (!Path.IsPathRooted(preview.Path) || !File.Exists(preview.Path))
            return;
        var config = _config.Current;
        var source = Path.GetDirectoryName(Path.GetFullPath(preview.Path))!;
        CopyMissing(Path.Combine(source, "images"), config.ResolveImagesFolder()!);
        var content = Path.Combine(source, DisplayConfiguration.DefaultContentFileName);
        if (File.Exists(content) && config.ResolveContentFile() is { } target && !File.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(content, target);
        }
    }

    private static void CopyMissing(string from, string to)
    {
        if (!Directory.Exists(from) || string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
            return;
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            if (File.Exists(target))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
