namespace DisplayBoard.Core.Processing;

/// <summary>Tìm file ảnh trong thư mục ảnh. Chỉ trả đường dẫn; phần load ảnh do App (WPF) làm.</summary>
public sealed class ImageResolver(string? imagesFolder)
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp"];

    public string? ResolveImage(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(imagesFolder))
            return null;
        var path = Path.Combine(imagesFolder, fileName.Trim());
        if (File.Exists(path))
            return path;
        if (!Path.HasExtension(path))
            return Extensions.Select(ext => path + ext).FirstOrDefault(File.Exists);
        return null;
    }

    public string? ResolveProductImage(string productCode, string? fileName) =>
        ResolveImage(fileName) ?? ResolveImage(productCode);

    /// <summary>Thư mục con chứa ảnh hàng lỗi QC chụp.</summary>
    public const string DefectFolder = "hang_loi";

    /// <summary>Ảnh hàng lỗi: tìm trong images\hang_loi trước, rồi tới images.</summary>
    public string? ResolveDefectImage(string? fileName) =>
        string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(fileName.Trim())
            ? null
            : ResolveImage(Path.Combine(DefectFolder, fileName.Trim())) ?? ResolveImage(fileName);
}
