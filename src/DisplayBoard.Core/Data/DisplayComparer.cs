using System.Globalization;
using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Data;

/// <summary>So hai bảng hiển thị (một do Excel tính, một do app tính) từng chuyền, từng cột. Trả về các ô lệch.</summary>
public static class DisplayComparer
{
    private const decimal Tolerance = 0.0001m;

    public static IReadOnlyList<string> Compare(DisplaySheet excel, DisplaySheet app)
    {
        var differences = new List<string>();
        if (excel.Date != app.Date)
            differences.Add($"Ngày hiển thị: Excel {excel.Date:dd/MM/yyyy}, app {app.Date:dd/MM/yyyy}");

        var count = Math.Max(excel.Lines.Count, app.Lines.Count);
        for (var i = 0; i < count; i++)
        {
            var x = excel.Lines.ElementAtOrDefault(i);
            var a = app.Lines.ElementAtOrDefault(i);
            if (x is null || a is null)
            {
                differences.Add($"Dòng {i + 1}: Excel {x?.Line ?? "(không có)"}, app {a?.Line ?? "(không có)"}");
                continue;
            }
            CompareRecord(x.Line, x, a, differences);
        }
        if (excel.Total is not null && app.Total is not null)
            CompareRecord("TỔNG CỘNG", excel.Total, app.Total, differences, totalRow: true);

        var excelDefects = excel.DefectLog.Select(Describe).ToList();
        var appDefects = app.DefectLog.Select(Describe).ToList();
        if (!excelDefects.SequenceEqual(appDefects))
            differences.Add($"Hàng lỗi hiện trên TV: Excel [{string.Join(" | ", excelDefects)}], app [{string.Join(" | ", appDefects)}]");
        return differences;
    }

    private static void CompareRecord(string label, LineRecord x, LineRecord a, List<string> differences, bool totalRow = false)
    {
        void Text(string name, string? ex, string? ap)
        {
            if (!string.Equals(ex ?? "", ap ?? "", StringComparison.OrdinalIgnoreCase))
                differences.Add($"{label} · {name}: Excel \"{ex}\", app \"{ap}\"");
        }
        void Num(string name, decimal? ex, decimal? ap)
        {
            if (ex is null && ap is null)
                return;
            if (ex is null || ap is null || Math.Abs(ex.Value - ap.Value) > Tolerance * Math.Max(1, Math.Abs(ex.Value)))
                differences.Add($"{label} · {name}: Excel {Show(ex)}, app {Show(ap)}");
        }

        Text("CHUYỀN", x.Line, a.Line);
        Num("MỤC TIÊU TRONG NGÀY", x.DailyTarget, a.DailyTarget);
        Num("THỰC TẾ TRONG NGÀY", x.DailyActual, a.DailyActual);
        Num("TỶ LỆ ĐẠT TRONG NGÀY", x.DailyRate, a.DailyRate);
        Num("CHÊNH LỆCH", x.DailyVariance, a.DailyVariance);
        Num("CÒN THIẾU", x.Remaining, a.Remaining);
        Num("MỤC TIÊU ĐẾN GIỜ ĐÃ NHẬP", x.TargetToNow, a.TargetToNow);
        Num("TIẾN ĐỘ THEO GIỜ", x.HourlyProgress, a.HourlyProgress);
        Num("THIẾU HÔM TRƯỚC", x.CarriedShortfall, a.CarriedShortfall);
        Num("LŨY KẾ THÁNG CỦA CHUYỀN", x.LineMonthCumulative, a.LineMonthCumulative);
        Num("SỐ LỖI", x.Defects, a.Defects);
        Num("TỶ LỆ LỖI", x.DefectRate, a.DefectRate);
        if (totalRow)
            return;

        Text("MÃ SẢN PHẨM", x.ProductCode, a.ProductCode);
        Text("MÃ CA", x.ShiftCode, a.ShiftCode);
        Num("GIỜ CA", x.ShiftHours, a.ShiftHours);
        Num("MỤC TIÊU MỖI GIỜ", x.HourlyTarget, a.HourlyTarget);
        Num("SỐ GIỜ ĐÃ NHẬP", x.HoursEntered, a.HoursEntered);
        if (x.PreviousDay != a.PreviousDay)
            differences.Add($"{label} · NGÀY LÀM TRƯỚC: Excel {x.PreviousDay:dd/MM/yyyy}, app {a.PreviousDay:dd/MM/yyyy}");
        Num("MỤC TIÊU THÁNG", x.MonthTarget, a.MonthTarget);
        Num("LŨY KẾ THÁNG", x.MonthCumulative, a.MonthCumulative);
        Num("TỶ LỆ ĐẠT THÁNG", x.MonthRate, a.MonthRate);
        Num("CÒN THIẾU THÁNG", x.MonthRemaining, a.MonthRemaining);
        Num("THIẾU THÁNG TRƯỚC", x.PreviousMonthShortfall, a.PreviousMonthShortfall);
        Num("NGÀY LÀM VIỆC CÒN LẠI", x.WorkingDaysLeft, a.WorkingDaysLeft);
        Num("CẦN LÀM MỖI NGÀY", x.NeededPerDay, a.NeededPerDay);
        for (var h = 0; h < DayEntry.MaxHours; h++)
            Num($"GIỜ {h + 1}", x.Hourly.ElementAtOrDefault(h), a.Hourly.ElementAtOrDefault(h));
        Text("TRẠNG THÁI", x.Status, a.Status);
        Text("GHI CHÚ", x.Note, a.Note);
    }

    private static string Describe(DefectEntry d) =>
        string.Join(" · ", d.Date?.ToString("dd/MM", CultureInfo.InvariantCulture), d.Time?.ToString("HH:mm", CultureInfo.InvariantCulture),
            d.Line, d.ProductCode, d.DefectType, Show(d.Quantity), d.ImageFile, d.Note);

    private static string Show(decimal? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "(trống)";
}
