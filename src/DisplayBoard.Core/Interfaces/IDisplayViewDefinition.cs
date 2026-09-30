using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

public interface IDisplayViewDefinition
{
    string Id { get; }
    string Name { get; }

    /// <summary>False khi view không có gì để hiển thị; vòng xoay sẽ bỏ qua view này.</summary>
    bool HasContent(DisplayDataSnapshot snapshot);

    /// <summary>Thời gian view cần để hiển thị hết (vd số trang của bảng chi tiết). Null = dùng thời gian cấu hình.</summary>
    TimeSpan? GetRequiredDuration(DisplayDataSnapshot snapshot) => null;
}
