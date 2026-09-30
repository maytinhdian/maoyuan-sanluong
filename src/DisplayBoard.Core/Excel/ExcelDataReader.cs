using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Excel;

/// <summary>Mở file với FileShare.ReadWrite để đọc được khi Excel đang mở file; copy ra bộ nhớ để giữ file trong thời gian ngắn nhất.</summary>
public sealed class ExcelDataReader : IExcelDataReader
{
    public async Task<DisplaySheet> ReadDisplayAsync(string filePath, string? sheetName, CancellationToken ct)
    {
        using var buffer = await CopyAsync(filePath, ct).ConfigureAwait(false);
        return await Task.Run(() => DisplaySheetReader.Read(buffer, sheetName), ct).ConfigureAwait(false);
    }

    public async Task<ContentData> ReadContentAsync(string filePath, CancellationToken ct)
    {
        using var buffer = await CopyAsync(filePath, ct).ConfigureAwait(false);
        return await Task.Run(() => ContentWorkbookReader.Read(buffer), ct).ConfigureAwait(false);
    }

    private static async Task<MemoryStream> CopyAsync(string filePath, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        await using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true))
        {
            await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
        }
        buffer.Position = 0;
        return buffer;
    }
}
