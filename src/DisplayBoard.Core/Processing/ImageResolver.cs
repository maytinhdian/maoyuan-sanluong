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
}
