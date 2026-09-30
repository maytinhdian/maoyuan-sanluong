namespace DisplayBoard.Core.Interfaces;

public interface IExcelWatcher : IDisposable
{
    /// <summary>Phát sau khi file thay đổi và đã hết thời gian debounce.</summary>
    event EventHandler? FileChanged;
    void Watch(string filePath, int debounceMilliseconds);
    void Stop();
}
