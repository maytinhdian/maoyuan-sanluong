namespace DisplayBoard.Core.Models;

public enum LoadStatus
{
    Ready,
    Reading,
    Updated,
    FileLocked,
    InvalidData,
    FileNotFound,
    NoFileSelected
}

public static class LoadStatusText
{
    public static string ToVietnamese(this LoadStatus status) => status switch
    {
        LoadStatus.Ready => "Sẵn sàng",
        LoadStatus.Reading => "Đang đọc",
        LoadStatus.Updated => "Đã cập nhật",
        LoadStatus.FileLocked => "File đang được sử dụng",
        LoadStatus.InvalidData => "Dữ liệu không hợp lệ",
        LoadStatus.FileNotFound => "Không tìm thấy file",
        LoadStatus.NoFileSelected => "Chưa chọn file",
        _ => status.ToString()
    };
}
