using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface ISnapshotService
{
    /// <summary>Snapshot tốt gần nhất. Không bị thay thế khi lần đọc sau thất bại.</summary>
    DisplayDataSnapshot? Current { get; }
    LoadStatus Status { get; }
    string? LastError { get; }
    DateTimeOffset? LastLoadedAt { get; }

    event EventHandler<DisplayDataSnapshot>? SnapshotChanged;
    event EventHandler? StatusChanged;

    Task ReloadAsync(CancellationToken ct = default);
}
