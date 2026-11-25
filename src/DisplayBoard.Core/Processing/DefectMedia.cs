using System.Text;

namespace DisplayBoard.Core.Processing;

/// <summary>
/// Nhận dạng ảnh và video hàng lỗi theo nội dung file (không tin đuôi file người gửi đặt).
/// Video lưu chung thư mục images\hang_loi với ảnh; TV chiếu video trong ô như ảnh (tự phát lặp lại, tắt tiếng).
/// </summary>
public static class DefectMedia
{
    /// <summary>Số byte đầu file cần đọc để nhận dạng.</summary>
    public const int HeaderLength = 64;

    /// <summary>Đuôi video đã lưu. Trình duyệt phát được mp4, mov, m4v, webm; mkv, avi, 3gp tuỳ máy (không phát được thì tải về xem).</summary>
    public static readonly IReadOnlyList<string> VideoExtensions = [".mp4", ".mov", ".m4v", ".webm", ".mkv", ".avi", ".3gp"];

    /// <summary>Tên file là video (theo đuôi file đã lưu).</summary>
    public static bool IsVideo(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && VideoExtensions.Contains(Path.GetExtension(fileName.Trim()).ToLowerInvariant());

    /// <summary>Đuôi ảnh (.jpg/.png) theo nội dung, không phải ảnh thì null.</summary>
    public static string? ImageExtension(ReadOnlySpan<byte> header) =>
        header.Length > 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF ? ".jpg"
        : header.Length > 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 ? ".png"
        : null;

    /// <summary>Đuôi video theo nội dung (MP4/MOV/M4V/3GP, WebM/MKV, AVI), không phải video thì null.</summary>
    public static string? VideoExtension(ReadOnlySpan<byte> header)
    {
        // ISO/QuickTime: [cỡ hộp 4 byte]["ftyp"][hãng 4 byte]. MOV cũ có thể mở đầu bằng moov/mdat/wide/free.
        if (header.Length >= 12 && Ascii(header, 4, 4) == "ftyp")
        {
            var brand = Ascii(header, 8, 4);
            return brand == "qt  " ? ".mov"
                : brand.StartsWith("M4V", StringComparison.Ordinal) ? ".m4v"
                : brand.StartsWith("3g", StringComparison.Ordinal) ? ".3gp"
                : ".mp4";
        }
        if (header.Length >= 8 && Ascii(header, 4, 4) is "moov" or "mdat" or "wide" or "free" or "skip" or "pnot")
            return ".mov";
        // Matroska/WebM: mã EBML 1A 45 DF A3, loại tài liệu "webm" hoặc "matroska" nằm ngay sau.
        if (header.Length >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
            return Ascii(header, 0, header.Length).Contains("webm", StringComparison.Ordinal) ? ".webm" : ".mkv";
        if (header.Length >= 12 && Ascii(header, 0, 4) == "RIFF" && Ascii(header, 8, 4) == "AVI ")
            return ".avi";
        return null;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes, int start, int length) =>
        Encoding.Latin1.GetString(bytes.Slice(start, length));
}
