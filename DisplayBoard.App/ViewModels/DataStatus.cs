namespace DisplayBoard.App.ViewModels;

/// <summary>Data source states shown in the main window (spec section 10).</summary>
public enum DataStatus
{
    Ready,
    Reading,
    Updated,
    FileLocked,
    InvalidData,
    FileNotFound,
}

public static class DataStatusText
{
    public static string ToDisplayText(this DataStatus status) => status switch
    {
        DataStatus.Ready => "Sẵn sàng",
        DataStatus.Reading => "Đang đọc",
        DataStatus.Updated => "Đã cập nhật",
        DataStatus.FileLocked => "File đang được sử dụng",
        DataStatus.InvalidData => "Dữ liệu không hợp lệ",
        DataStatus.FileNotFound => "Không tìm thấy file",
        _ => status.ToString(),
    };
}
