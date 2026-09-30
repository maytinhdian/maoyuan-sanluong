using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface IExcelDataReader
{
    /// <summary>Đọc sheet hiển thị (mặc định HIEN_THI) của file theo dõi sản lượng.</summary>
    Task<DisplaySheet> ReadDisplayAsync(string filePath, string? sheetName, CancellationToken ct);

    /// <summary>Đọc file nội dung phụ (display-content.xlsx).</summary>
    Task<ContentData> ReadContentAsync(string filePath, CancellationToken ct);
}
