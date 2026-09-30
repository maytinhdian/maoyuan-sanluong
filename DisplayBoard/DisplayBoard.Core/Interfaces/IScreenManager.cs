using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface IScreenManager
{
    IReadOnlyList<MonitorInfo> GetMonitors();
}
