using System.Reflection;

namespace DisplayBoard.App;

/// <summary>
/// Thông tin đơn vị phát triển hiện ở tab Cài đặt. Cố định trong app (khách không sửa được);
/// đổi ở đây rồi build lại.
/// </summary>
public static class AboutInfo
{
    public const string Developer = "Công ty TNHH Giải Pháp Sáng Tạo TMT Việt Nam";
    public const string Phone = "0393 080 822";
    public const string Website = "https://maytinhdian.com";
    public static Uri WebsiteUri { get; } = new(Website);

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
