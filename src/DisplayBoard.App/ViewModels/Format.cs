using System.Globalization;

namespace DisplayBoard.App.ViewModels;

internal static class Format
{
    public static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static string Number(decimal value) => value.ToString("N0", Vi);
    public static string Number(decimal? value) => value is null ? "—" : Number(value.Value);
    public static string Percent(decimal? value) => value is null ? "—" : $"{value.Value:0}%";
    public static string Signed(decimal? value) => value switch
    {
        null => "—",
        > 0 => "+" + Number(value.Value),
        _ => Number(value.Value)
    };

    public static string DayName(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "Thứ Hai",
        DayOfWeek.Tuesday => "Thứ Ba",
        DayOfWeek.Wednesday => "Thứ Tư",
        DayOfWeek.Thursday => "Thứ Năm",
        DayOfWeek.Friday => "Thứ Sáu",
        DayOfWeek.Saturday => "Thứ Bảy",
        _ => "Chủ Nhật"
    };
}
