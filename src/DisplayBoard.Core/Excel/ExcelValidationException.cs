namespace DisplayBoard.Core.Excel;

/// <summary>Workbook sai cấu trúc (thiếu sheet DATA, thiếu cột bắt buộc...).</summary>
public sealed class ExcelValidationException(string message) : Exception(message);
