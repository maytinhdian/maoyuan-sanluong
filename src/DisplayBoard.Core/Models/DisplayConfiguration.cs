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
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Mirror;
    public bool AutoReload { get; set; } = true;
    public int DebounceMilliseconds { get; set; } = 800;
    public string? ImagesFolder { get; set; }
    public int DefaultViewSeconds { get; set; } = 15;
    public int MaxPagedViewSeconds { get; set; } = 60;
    public List<ScreenAssignment> Screens { get; set; } = [];

    /// <summary>Thư mục ảnh thực tế: cấu hình hoặc thư mục <c>images</c> cạnh file Excel.</summary>
    public string? ResolveImagesFolder()
    {
        if (!string.IsNullOrWhiteSpace(ImagesFolder))
            return ImagesFolder;
        var dir = string.IsNullOrWhiteSpace(ExcelFile) ? null : Path.GetDirectoryName(ExcelFile);
        return dir is null ? null : Path.Combine(dir, "images");
    }
}
