namespace DisplayBoard.Core.Models;

/// <summary>Nội dung phụ đọc từ display-content.xlsx (tùy chọn). File khách không bị sửa.</summary>
public sealed record ContentData(
    IReadOnlyList<NoticeRow> Notices,
    IReadOnlyList<Slogan> Slogans,
    IReadOnlyList<ProductInfo> Products,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<string> Warnings)
{
    public static ContentData Empty { get; } = new([], [], [], new Dictionary<string, string>(), []);
}

/// <summary>Dòng trong sheet SAN_PHAM: tên hiển thị, màu, ảnh, thứ tự của sản phẩm.</summary>
public sealed record ProductInfo(
    string ProductCode,
    string? DisplayName,
    string? Color,
    string? ImageFile,
    int? Order);

/// <summary>Dòng trong sheet THONG_BAO.</summary>
public sealed record NoticeRow(
    string Title,
    string Content,
    string? BackgroundImage,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Order,
    bool Enabled);

/// <summary>Dòng trong sheet KHAU_HIEU.</summary>
public sealed record Slogan(string Icon, string Line1, string? Line2);
