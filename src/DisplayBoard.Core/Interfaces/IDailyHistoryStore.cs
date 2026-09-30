using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Interfaces;

/// <summary>Lưu kết quả từng ngày ngoài file Excel của khách (app không bao giờ ghi vào file khách).</summary>
public interface IDailyHistoryStore
{
    /// <summary>Ghi đè kết quả của một ngày. Gọi mỗi lần đọc file, nên lần đọc cuối cùng trong ngày là số chốt.</summary>
    void Save(DayHistory day);

    /// <summary>Ngày gần nhất trước <paramref name="date"/> đã có dữ liệu (bỏ qua ngày nghỉ).</summary>
    DayHistory? GetPreviousDay(DateOnly date);
}
