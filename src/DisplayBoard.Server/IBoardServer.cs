using DisplayBoard.Core.Models;

namespace DisplayBoard.Server;

/// <summary>TV (trình duyệt) đang kết nối.</summary>
public sealed record ConnectedClient(Guid Id, int Screen, string Address, string? UserAgent, DateTimeOffset ConnectedAt);

/// <summary>Máy chủ LAN: phát dữ liệu và giao diện web cho các TV.</summary>
public interface IBoardServer : IAsyncDisposable, IDisposable
{
    bool IsRunning { get; }
    int Port { get; }
    string? LastError { get; }
    IReadOnlyList<ConnectedClient> Clients { get; }

    /// <summary>Server bật/tắt, đổi cổng hoặc có TV kết nối/ngắt.</summary>
    event EventHandler? StateChanged;

    /// <summary>Áp dụng cấu hình: bật/tắt, đổi cổng, gửi playlist mới cho các TV đang mở.</summary>
    Task ApplyAsync(DisplayConfiguration configuration, CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);
}
