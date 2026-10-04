using DisplayBoard.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Data;

/// <summary>
/// Giữ cơ sở dữ liệu đang dùng theo thư mục dữ liệu trong cấu hình. Đổi thư mục thì đóng file cũ, mở file mới.
/// Mọi phần của app (TV, trang nhập liệu, trang quản lý, xuất Excel) lấy dữ liệu qua đây.
/// </summary>
public sealed class ProductionDatabase(IConfigurationService config, TimeProvider time, ILogger<ProductionDatabase> logger) : IDisposable
{
    private readonly Lock _lock = new();
    private ProductionStore? _store;

    /// <summary>Phát sau mỗi lần ghi, và khi đổi sang file dữ liệu khác.</summary>
    public event EventHandler? Changed;

    /// <summary>Lỗi khi mở file gần nhất (vd thư mục không ghi được). Null = mở được.</summary>
    public string? Error { get; private set; }

    public string FilePath => config.Current.ResolveDatabaseFile();

    /// <summary>Cơ sở dữ liệu của thư mục dữ liệu hiện tại. Ném <see cref="InvalidOperationException"/> khi không mở được.</summary>
    public ProductionStore Store
    {
        get
        {
            var path = Path.GetFullPath(FilePath);
            ProductionStore? opened = null;
            lock (_lock)
            {
                if (_store is not null && string.Equals(_store.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    return _store;
                if (_store is not null)
                {
                    _store.Changed -= OnChanged;
                    _store.Dispose();
                    _store = null;
                }
                try
                {
                    opened = new ProductionStore(path, time);
                    opened.Changed += OnChanged;
                    _store = opened;
                    Error = null;
                    logger.LogInformation("Mở cơ sở dữ liệu {Path}", path);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Error = $"Không mở được file dữ liệu {path}: {ex.Message}";
                    logger.LogError(ex, "Không mở được cơ sở dữ liệu {Path}", path);
                    throw new InvalidOperationException(Error, ex);
                }
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return opened;
        }
    }

    /// <summary>Dữ liệu hiện tại, hoặc rỗng khi chưa mở được file.</summary>
    public ProductionData TryLoad()
    {
        try
        {
            return Store.Load();
        }
        catch (InvalidOperationException)
        {
            return ProductionData.Empty;
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        lock (_lock)
        {
            _store?.Dispose();
            _store = null;
        }
    }
}
