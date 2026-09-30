namespace DisplayBoard.Core.Models;

public enum DisplayMode
{
    Mirror,
    Independent
}

public sealed class PlaylistItem
{
    public string ViewId { get; set; } = "";
    public int? Seconds { get; set; }
}

public sealed class ScreenAssignment
{
    public string MonitorId { get; set; } = "";
    public List<PlaylistItem> Playlist { get; set; } = [];
}

/// <summary>Nội dung file display-config.json.</summary>
public sealed class DisplayConfiguration
{
    public string? ExcelFile { get; set; }

    /// <summary>Tên sheet cố định. Null = tự chọn sheet có ngày mới nhất.</summary>
    public string? SheetName { get; set; }

    /// <summary>File nội dung phụ (thông báo, khẩu hiệu, tên sản phẩm). Null = display-content.xlsx cạnh file dữ liệu.</summary>
    public string? ContentFile { get; set; }
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Mirror;
    public bool AutoReload { get; set; } = true;
    public int DebounceMilliseconds { get; set; } = 800;
    public string? ImagesFolder { get; set; }
    public int DefaultViewSeconds { get; set; } = 15;
    public int MaxPagedViewSeconds { get; set; } = 60;
    public List<ScreenAssignment> Screens { get; set; } = [];

    public const string DefaultContentFileName = "display-content.xlsx";

    public string? ResolveContentFile()
    {
        if (!string.IsNullOrWhiteSpace(ContentFile))
            return ContentFile;
        var dir = string.IsNullOrWhiteSpace(ExcelFile) ? null : Path.GetDirectoryName(ExcelFile);
        return dir is null ? null : Path.Combine(dir, DefaultContentFileName);
    }

    /// <summary>Các file cần theo dõi thay đổi: file sản lượng và file nội dung phụ.</summary>
    public IReadOnlyList<string> WatchedFiles()
    {
        if (string.IsNullOrWhiteSpace(ExcelFile))
            return [];
        var content = ResolveContentFile();
        return content is null ? [ExcelFile] : [ExcelFile, content];
    }

    /// <summary>Thư mục ảnh thực tế: cấu hình hoặc thư mục <c>images</c> cạnh file Excel.</summary>
    public string? ResolveImagesFolder()
    {
        if (!string.IsNullOrWhiteSpace(ImagesFolder))
            return ImagesFolder;
        var dir = string.IsNullOrWhiteSpace(ExcelFile) ? null : Path.GetDirectoryName(ExcelFile);
        return dir is null ? null : Path.Combine(dir, "images");
    }
}
