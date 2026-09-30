using System.Globalization;
using System.Text;

namespace DisplayBoard.Core.Excel;

/// <summary>
/// Layout of the DATA sheet (spec §4). Headers are matched after trimming, collapsing whitespace,
/// Unicode normalization and ignoring case.
/// </summary>
public static class DataSheetSchema
{
    public const string SheetName = "DATA";

    public const string Date = "Ngày";
    public const string EmployeeCode = "Mã NV";
    public const string EmployeeName = "Họ tên";
    public const string Department = "Bộ phận";
    public const string Quantity = "Sản lượng";
    public const string Target = "Mục tiêu";
    public const string Note = "Ghi chú";

    public static IReadOnlyList<string> RequiredColumns { get; } =
        [Date, EmployeeCode, EmployeeName, Department, Quantity];

    public static IReadOnlyList<string> OptionalColumns { get; } = [Target, Note];

    public static IReadOnlyList<string> AllColumns { get; } = [.. RequiredColumns, .. OptionalColumns];

    public static string NormalizeHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        var collapsed = string.Join(' ', header.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Normalize(NormalizationForm.FormC).ToLower(CultureInfo.InvariantCulture);
    }
}
