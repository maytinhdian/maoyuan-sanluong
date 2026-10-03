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

/// <summary>Một TV xem qua mạng LAN, mở http://&lt;IP&gt;:&lt;cổng&gt;/tv/{Number}. Số không đổi khi xoá TV khác.</summary>
public sealed class NetworkScreen
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public List<PlaylistItem> Playlist { get; set; } = [];
}

/// <summary>Máy chủ mạng LAN: TV mở trình duyệt vào http://&lt;IP máy này&gt;:Port/tv/1.</summary>
public sealed class LanServerSettings
{
    public const int DefaultPort = 5080;

    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = DefaultPort;

    /// <summary>Mã truy cập TV phải gửi kèm (?key=...). Trống = không cần mã.</summary>
    public string? AccessKey { get; set; }
}

/// <summary>Nhập liệu qua trình duyệt (bản 3.x): tổ trưởng mở http://&lt;IP&gt;:&lt;cổng&gt;/nhap và đăng nhập bằng mã PIN.</summary>
public sealed class DataEntrySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Người được nhập liệu. Trống = chưa ai đăng nhập được.</summary>
    public List<EntryUser> Users { get; set; } = [];
}

public sealed class EntryUser
{
    public string Name { get; set; } = "";
    public string Pin { get; set; } = "";

    /// <summary>Chuyền được nhập, đúng tên trong DANH_SACH_CHUYEN. Trống = tất cả chuyền.</summary>
    public List<string> Lines { get; set; } = [];
}

/// <summary>Nội dung file display-config.json.</summary>
public sealed class DisplayConfiguration
{
    public string? ExcelFile { get; set; }

    /// <summary>Tên hiện ở cửa sổ chính và khay hệ thống. Null = "Display Board".</summary>
    public string? AppName { get; set; }

    /// <summary>Sheet hiển thị. Null = HIEN_THI.</summary>
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
    public LanServerSettings Server { get; set; } = new();
    public DataEntrySettings DataEntry { get; set; } = new();

    /// <summary>Các TV xem qua mạng. Trống (cấu hình từ bản 1.x) thì lấy theo TV1/TV2 ở <see cref="Screens"/>.</summary>
    public List<NetworkScreen> NetworkScreens { get; set; } = [];

    public const string DefaultContentFileName = "display-content.xlsx";
    public const string DefaultAppName = "Display Board";

    /// <summary>Danh sách TV qua mạng, luôn có ít nhất một TV.</summary>
    public IReadOnlyList<NetworkScreen> ResolveNetworkScreens()
    {
        var screens = NetworkScreens.Where(s => s.Number > 0).GroupBy(s => s.Number).Select(g => g.First()).OrderBy(s => s.Number).ToList();
        if (screens.Count > 0)
            return screens;
        // Nâng cấp từ 1.x: dùng nội dung đã chọn cho TV1/TV2.
        var count = Math.Max(2, Screens.Count);
        return Enumerable.Range(1, count).Select(n => new NetworkScreen
        {
            Number = n,
            Name = $"TV{n}",
            Playlist = n <= Screens.Count && Screens[n - 1].Playlist.Count > 0
                ? Screens[n - 1].Playlist
                : [new PlaylistItem { ViewId = n == 1 ? "overview" : "ranking" }]
        }).ToList();
    }

    public string ResolveAppName() => string.IsNullOrWhiteSpace(AppName) ? DefaultAppName : AppName.Trim();

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
