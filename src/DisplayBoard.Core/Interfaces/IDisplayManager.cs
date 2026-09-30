using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface IDisplayManager
{
    bool IsRunning { get; }
    event EventHandler? RunningChanged;
    void Start(DisplayConfiguration configuration);
    void Stop();
}
