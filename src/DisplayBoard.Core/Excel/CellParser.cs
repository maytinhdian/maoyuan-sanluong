using System.Globalization;
using ClosedXML.Excel;

namespace DisplayBoard.Core.Excel;

/// <summary>Chuyển giá trị ô Excel sang kiểu .NET, chấp nhận cả ô dạng số/ngày và ô dạng text.</summary>
internal static partial class CellParser
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly string[] DateFormats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy"];
    private static readonly string[] TimeFormats = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss", "H'h'mm", "H'h'"];

    public static string? GetText(IXLCell cell)
    {
        var value = cell.Value;
        if (value.IsBlank)
            return null;
        var text = value.IsText ? value.GetText() : cell.GetFormattedString();
        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    public static bool IsBlank(IXLCell cell) => GetText(cell) is null;

    public static bool TryGetDate(IXLCell cell, out DateOnly date)
    {
        var value = cell.Value;
        if (value.IsDateTime)
        {
            date = DateOnly.FromDateTime(value.GetDateTime());
            return true;
        }
        if (value.IsNumber)
        {
            var number = value.GetNumber();
            if (number is > 0 and < 2958466)
            {
                date = DateOnly.FromDateTime(DateTime.FromOADate(number));
                return true;
            }
        }
        var text = GetText(cell);
        if (text is not null && DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }
        date = default;
        return false;
    }

    public static bool TryGetTime(IXLCell cell, out TimeOnly time)
    {
        var value = cell.Value;
        if (value.IsTimeSpan)
        {
            var span = value.GetTimeSpan();
            if (span >= TimeSpan.Zero && span < TimeSpan.FromDays(1))
            {
                time = TimeOnly.FromTimeSpan(span);
                return true;
            }
        }
        if (value.IsDateTime)
        {
            time = TimeOnly.FromDateTime(value.GetDateTime());
            return true;
        }
        if (value.IsNumber)
        {
            var fraction = value.GetNumber() % 1;
            if (fraction >= 0)
            {
                time = TimeOnly.FromTimeSpan(TimeSpan.FromDays(fraction));
                return true;
            }
        }
        var text = GetText(cell);
        if (text is not null && DateTime.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            time = TimeOnly.FromDateTime(parsed);
            return true;
        }
        time = default;
        return false;
    }

    public static bool TryGetDecimal(IXLCell cell, out decimal number)
    {
        var value = cell.Value;
        if (value.IsNumber)
        {
            number = (decimal)value.GetNumber();
            return true;
        }
        var text = GetText(cell);
        if (text is null)
        {
            number = 0;
            return false;
        }
        text = text.Replace(" ", "").Replace("\u00A0", "").TrimEnd('%');
        // Có phân cách hàng nghìn: "1,000", "1.000", "40,000", "1.250.000"
        if (ThousandsPattern().IsMatch(text))
            return decimal.TryParse(text.Replace(",", "").Replace(".", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);
        // Kiểu Việt Nam "1.250,5"
        if (text.Contains(',') && text.Contains('.') && text.LastIndexOf(',') > text.LastIndexOf('.'))
            return decimal.TryParse(text, NumberStyles.Number, Vi, out number);
        // Dấu phẩy thập phân "12,5"
        if (text.Contains(',') && !text.Contains('.'))
            return decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out number);
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^-?\d{1,3}([.,]\d{3})+$")]
    private static partial System.Text.RegularExpressions.Regex ThousandsPattern();

    public static bool IsTruthy(string? text) =>
        text is not null && !text.Equals("0", StringComparison.Ordinal)
        && !text.Equals("false", StringComparison.OrdinalIgnoreCase)
        && !text.Equals("không", StringComparison.OrdinalIgnoreCase);
}
