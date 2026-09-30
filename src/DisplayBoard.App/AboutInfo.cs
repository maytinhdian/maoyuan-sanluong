using System.Reflection;

namespace DisplayBoard.App;

/// <summary>
/// Thông tin đơn vị phát triển hiện ở tab Cài đặt. Cố định trong app (khách không sửa được);
/// đổi ở đây rồi build lại.
/// </summary>
public static class AboutInfo
{
    // TODO: thay bằng thông tin thật của đơn vị phát triển.
    public const string Developer = "[Tên đơn vị phát triển]";
    public const string Phone = "[Số điện thoại]";
    public const string Email = "[Email]";
    public const string Website = "[Website]";
    public const string Address = "[Địa chỉ]";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
