namespace DisplayBoard.Core.Entry;

/// <summary>
/// File Excel đang mở để nhập liệu. Có hai cách mở: qua chính Excel trên máy chủ (công thức tự tính lại, dùng khi chạy thật)
/// và qua ClosedXML (chỉ để đọc danh sách/ngày hiện tại và để test).
/// </summary>
public interface IEntryWorkbook
{
    /// <summary>Sheet theo tên, không phân biệt hoa/thường và dấu. Null khi file không có sheet này.</summary>
    IEntrySheet? Sheet(string name);
}

public interface IEntrySheet
{
    string Name { get; }
    int LastRow { get; }
    int LastColumn { get; }

    /// <summary>Giá trị ô: null, double, string, bool hoặc DateTime. Ô công thức trả về giá trị đã tính.</summary>
    object? Get(int row, int column);

    /// <summary>Ghi ô. Nhận string, số, DateTime (ngày), TimeSpan (giờ trong ngày) hoặc null (xoá).</summary>
    void Set(int row, int column, object? value);

    /// <summary>Các dòng dữ liệu của Excel Table đầu tiên trên sheet. Null khi sheet không có Table hoặc Table chưa có dòng.</summary>
    (int First, int Last)? TableBody();

    /// <summary>Thêm một dòng cuối Table (Excel tự điền cột công thức) và trả về số dòng mới. Sheet không có Table thì trả về dòng sau dòng cuối.</summary>
    int AppendTableRow();
}

/// <summary>Lỗi do dữ liệu nhập (thiếu kế hoạch, sai chuyền...): báo nguyên văn cho người nhập, không thử lại.</summary>
public class EntryException(string message) : Exception(message);

/// <summary>Excel đang bận (có người đang sửa ô, đang mở hộp thoại, file đang mở chỉ đọc): chờ rồi thử lại.</summary>
public sealed class WorkbookBusyException(string message, Exception? inner = null) : Exception(message, inner);
