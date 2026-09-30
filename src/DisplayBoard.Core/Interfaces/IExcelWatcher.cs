namespace DisplayBoard.Core.Interfaces;

public interface IExcelWatcher : IDisposable
{
    /// <summary>Phát sau khi file thay đổi và đã hết thời gian debounce.</summary>
    event EventHandler? FileChanged;
    /// <summary>Theo dõi một hoặc nhiều file (file sản lượng + file nội dung phụ).</summary>
    void Watch(IReadOnlyList<string> filePaths, int debounceMilliseconds);
    void Stop();
}
