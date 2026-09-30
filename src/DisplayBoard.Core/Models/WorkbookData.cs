namespace DisplayBoard.Core.Models;

/// <summary>Dữ liệu thô đọc từ workbook, chưa tổng hợp.</summary>
public sealed record WorkbookData(
    IReadOnlyList<ProductionRecord> Records,
    IReadOnlyList<EmployeeInfo> Employees,
    IReadOnlyList<DepartmentInfo> Departments,
    IReadOnlyList<NoticeRow> Notices,
    IReadOnlyList<Slogan> Slogans,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<string> Warnings);

/// <summary>Dòng trong sheet DANH_MUC.</summary>
public sealed record EmployeeInfo(
    string EmployeeCode,
    string? EmployeeName,
    string? Department,
    string? Status,
    string? PhotoFile);

/// <summary>Dòng trong sheet BO_PHAN.</summary>
public sealed record DepartmentInfo(
    string Name,
    decimal? Target,
    string? Color,
    string? Icon,
    int? Order);

/// <summary>Dòng trong sheet THONG_BAO.</summary>
public sealed record NoticeRow(
    string Title,
    string Content,
    string? BackgroundImage,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Order,
    bool Enabled);

/// <summary>Dòng trong sheet KHAU_HIEU.</summary>
public sealed record Slogan(string Icon, string Line1, string? Line2);
