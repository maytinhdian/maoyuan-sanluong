using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface ISnapshotService
{
    /// <summary>Last good snapshot; stays in place when a reload fails.</summary>
    DisplayDataSnapshot? Current { get; }

    event EventHandler<DisplayDataSnapshot>? SnapshotChanged;

    Task ReloadAsync(CancellationToken ct = default);
}
