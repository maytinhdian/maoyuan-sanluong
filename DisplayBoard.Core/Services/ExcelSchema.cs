using System.Text;

namespace DisplayBoard.Core.Services;

/// <summary>Sheet and column names of the workbook, as described in the spec (section 4).</summary>
public static class ExcelSchema
{
    public const string DataSheet = "DATA";

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

    /// <summary>
    /// Trims, collapses inner whitespace and normalizes Unicode so that headers typed with
    /// different Vietnamese input methods (composed vs. decomposed accents) still match.
    /// Compare the result with <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    public static string NormalizeHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        var parts = header.Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }
}
