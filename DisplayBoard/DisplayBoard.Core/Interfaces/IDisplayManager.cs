using DisplayBoard.Core.Configuration;

namespace DisplayBoard.Core.Interfaces;

public interface IDisplayManager
{
    void Start(DisplayConfiguration configuration);
    void Stop();
}
