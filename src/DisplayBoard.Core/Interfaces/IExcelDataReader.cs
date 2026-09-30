using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface IExcelDataReader
{
    /// <summary>Đọc sheet sản lượng. <paramref name="sheetName"/> null = tự chọn sheet có ngày mới nhất.</summary>
    Task<ProductionSheet> ReadProductionAsync(string filePath, string? sheetName, CancellationToken ct);

    /// <summary>Đọc file nội dung phụ (display-content.xlsx).</summary>
    Task<ContentData> ReadContentAsync(string filePath, CancellationToken ct);
}
