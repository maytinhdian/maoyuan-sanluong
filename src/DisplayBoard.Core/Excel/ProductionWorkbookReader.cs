using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Excel;

/// <summary>
/// Đọc file sản lượng của khách: mỗi dòng một sản phẩm, header song ngữ Trung/Việt,
/// ngày nằm trong ô chữ phía trên header (vd "日期：2026/09/30"). File có thể có nhiều sheet.
/// </summary>
public static partial class ProductionWorkbookReader
{
    private const int HeaderSearchRows = 10;

    /// <summary>Các cột nhận diện được. Khớp phần tiếng Trung trước vì nhãn tiếng Việt dễ trùng/sai chính tả.</summary>
    internal enum Column
    {
        ProductCode,
        ShiftHours,
        HourlyTarget,
        DailyTarget,
        DailyActual,
        MonthTarget,
        MonthCumulative
    }

    private static readonly (Column Column, string Chinese, string Vietnamese, string Label)[] Columns =
    [
        (Column.ProductCode, "产品代码", "masanpham", "产品代码 / MÃ SẢN PHẨM"),
        (Column.ShiftHours, "每日工时", "thoigianlenca", "每日工时数 / THỜI GIAN LÊN CA"),
        (Column.HourlyTarget, "每小时目标", "muctieumoigio", "每小时目标产量 / SẢN LƯỢNG MỤC TIÊU MỖI GIỜ"),
        (Column.DailyTarget, "每日目标", "muctieutrongngay", "每日目标产量 / MỤC TIÊU TRONG NGÀY"),
        (Column.DailyActual, "每日实际", "thuctetrongngay", "每日实际产量 / SẢN LƯỢNG THỰC TẾ TRONG NGÀY"),
        (Column.MonthTarget, "当月总目标", "tongsanluongtrongthang", "当月总目标产量 / TỔNG SẢN LƯỢNG TRONG THÁNG"),
        (Column.MonthCumulative, "当月累计产", "luykesanluongtrongthang", "当月累计产能 / LŨY KẾ SẢN LƯỢNG TRONG THÁNG"),
    ];

    public static ProductionSheet Read(Stream stream, string? sheetName = null)
    {
        using var workbook = new XLWorkbook(stream);
        return Read(workbook, sheetName);
    }

    public static ProductionSheet Read(XLWorkbook workbook, string? sheetName = null)
    {
        if (!string.IsNullOrWhiteSpace(sheetName))
        {
            var named = workbook.Worksheets.FirstOrDefault(ws => string.Equals(ws.Name.Trim(), sheetName.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ExcelValidationException($"Không tìm thấy sheet \"{sheetName}\" trong file Excel.");
            var layout = FindLayout(named)
                ?? throw new ExcelValidationException($"Sheet \"{named.Name}\" không có dòng tiêu đề chứa cột {Columns[0].Label}.");
            return ReadSheet(named, layout);
        }

        var candidates = workbook.Worksheets
            .Select(ws => (Sheet: ws, Layout: FindLayout(ws)))
            .Where(x => x.Layout is not null)
            .ToList();
        if (candidates.Count == 0)
            throw new ExcelValidationException($"Không tìm thấy sheet nào có dòng tiêu đề chứa cột {Columns[0].Label}.");

        // Mỗi sheet là một chuyền: đọc mọi sheet có ngày mới nhất (sheet ngày cũ còn sót lại bị bỏ qua).
        // Không sheet nào có ngày thì đọc tất cả.
        var newest = candidates.Max(x => x.Layout!.Date);
        var chosen = candidates
            .Where(x => newest is null || x.Layout!.Date == newest || x.Layout!.Date is null)
            .OrderBy(x => x.Sheet.Position)
            .ToList();
        var sheets = chosen.Select(x => ReadSheet(x.Sheet, x.Layout!)).ToList();
        if (sheets.Count == 1)
            return sheets[0];
        return new ProductionSheet(
            string.Join(", ", sheets.Select(x => x.SheetName)),
            newest,
            sheets.SelectMany(x => x.Records).ToList(),
            sheets.SelectMany(x => x.Warnings).ToList())
        {
            Lines = sheets.Select(x => x.SheetName).ToList()
        };
    }

    private sealed record Layout(int HeaderRow, IReadOnlyDictionary<Column, int> Columns, DateOnly? Date);

    private static Layout? FindLayout(IXLWorksheet sheet)
    {
        var used = sheet.RangeUsed();
        if (used is null)
            return null;
        var lastRow = Math.Min(used.LastRow().RowNumber(), HeaderSearchRows);
        var lastColumn = used.LastColumn().ColumnNumber();

        for (var row = 1; row <= lastRow; row++)
        {
            var columns = new Dictionary<Column, int>();
            for (var col = 1; col <= lastColumn; col++)
            {
                var text = CellParser.GetText(sheet.Cell(row, col));
                if (text is null)
                    continue;
                var match = MatchColumn(text);
                if (match is { } column)
                    columns.TryAdd(column, col);
            }
            if (!columns.ContainsKey(Column.ProductCode))
                continue;
            return new Layout(row, columns, FindDate(sheet, row, lastColumn));
        }
        return null;
    }

    internal static Column? MatchColumn(string header)
    {
        var compact = header.Replace("\n", "").Replace("\r", "").Replace(" ", "");
        foreach (var c in Columns)
        {
            if (compact.Contains(c.Chinese, StringComparison.Ordinal))
                return c.Column;
        }
        var normalized = SheetTable.NormalizeHeader(header);
        foreach (var c in Columns)
        {
            if (normalized.Contains(c.Vietnamese, StringComparison.Ordinal))
                return c.Column;
        }
        return null;
    }

    private static DateOnly? FindDate(IXLWorksheet sheet, int headerRow, int lastColumn)
    {
        for (var row = 1; row < headerRow; row++)
        {
            for (var col = 1; col <= lastColumn; col++)
            {
                var cell = sheet.Cell(row, col);
                if (cell.Value.IsDateTime)
                    return DateOnly.FromDateTime(cell.Value.GetDateTime());
                var text = CellParser.GetText(cell);
                if (text is not null && TryParseDateInText(text, out var date))
                    return date;
            }
        }
        return null;
    }

    [GeneratedRegex(@"(?<y>\d{4})\s*[/\-.年]\s*(?<m>\d{1,2})\s*[/\-.月]\s*(?<d>\d{1,2})")]
    private static partial Regex YearFirst();

    [GeneratedRegex(@"(?<d>\d{1,2})\s*[/\-.]\s*(?<m>\d{1,2})\s*[/\-.]\s*(?<y>\d{4})")]
    private static partial Regex DayFirst();

    /// <summary>Tìm ngày trong chuỗi: "日期：2026/09/30", "2026-09-30", "30/09/2026".</summary>
    public static bool TryParseDateInText(string text, out DateOnly date)
    {
        foreach (var regex in new[] { YearFirst(), DayFirst() })
        {
            var match = regex.Match(text);
            if (!match.Success)
                continue;
            var y = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
            var d = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);
            if (m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m))
            {
                date = new DateOnly(y, m, d);
                return true;
            }
        }
        date = default;
        return false;
    }

    private static ProductionSheet ReadSheet(IXLWorksheet sheet, Layout layout)
    {
        var warnings = new List<string>();
        var columns = layout.Columns;

        var hasDailyTarget = columns.ContainsKey(Column.DailyTarget)
            || (columns.ContainsKey(Column.ShiftHours) && columns.ContainsKey(Column.HourlyTarget));
        var missing = new List<string>();
        if (!hasDailyTarget)
            missing.Add(Label(Column.DailyTarget));
        if (!columns.ContainsKey(Column.DailyActual))
            missing.Add(Label(Column.DailyActual));
        if (missing.Count > 0)
            throw new ExcelValidationException($"Sheet \"{sheet.Name}\" thiếu cột bắt buộc: {string.Join("; ", missing)}.");

        if (layout.Date is null)
            warnings.Add($"Sheet \"{sheet.Name}\": không đọc được ngày trong file, dùng ngày hôm nay.");

        var records = new List<ProductRecord>();
        var lastRow = sheet.RangeUsed()!.LastRow().RowNumber();
        for (var row = layout.HeaderRow + 1; row <= lastRow; row++)
        {
            var code = Text(sheet, row, columns, Column.ProductCode);
            if (code is null)
                continue; // dòng trống có kẻ khung cuối bảng
            if (IsTotalRow(code))
                continue; // dòng tổng do khách tự thêm: app tự cộng, không tính là một sản phẩm

            var hours = Number(sheet, row, columns, Column.ShiftHours);
            var hourly = Number(sheet, row, columns, Column.HourlyTarget);
            var target = Number(sheet, row, columns, Column.DailyTarget) ?? (hours * hourly);
            var actual = Number(sheet, row, columns, Column.DailyActual);

            if (actual is null)
            {
                warnings.Add($"{sheet.Name} dòng {row} ({code}): thiếu sản lượng thực tế, đã bỏ qua.");
                continue;
            }
            if (target is null)
                warnings.Add($"{sheet.Name} dòng {row} ({code}): thiếu mục tiêu ngày.");

            records.Add(new ProductRecord(
                row, code, hours, hourly, target ?? 0, actual.Value,
                Number(sheet, row, columns, Column.MonthTarget),
                Number(sheet, row, columns, Column.MonthCumulative),
                sheet.Name.Trim()));
        }

        return new ProductionSheet(sheet.Name, layout.Date, records, warnings) { Lines = [sheet.Name.Trim()] };
    }

    private static readonly string[] TotalWords = ["tong", "tongcong", "total", "sum"];

    /// <summary>Dòng tổng cuối bảng: "TỔNG", "TỔNG CỘNG", "Total", "合计", "总计", "小计".</summary>
    public static bool IsTotalRow(string code)
    {
        if (code.Contains("合计", StringComparison.Ordinal) || code.Contains("总计", StringComparison.Ordinal) || code.Contains("小计", StringComparison.Ordinal))
            return true;
        var letters = new string(SheetTable.NormalizeHeader(code).Where(char.IsAsciiLetter).ToArray());
        return TotalWords.Contains(letters);
    }

    private static string Label(Column column) => Columns.First(c => c.Column == column).Label;

    private static string? Text(IXLWorksheet sheet, int row, IReadOnlyDictionary<Column, int> columns, Column column) =>
        columns.TryGetValue(column, out var col) ? CellParser.GetText(sheet.Cell(row, col)) : null;

    private static decimal? Number(IXLWorksheet sheet, int row, IReadOnlyDictionary<Column, int> columns, Column column)
    {
        if (!columns.TryGetValue(column, out var col))
            return null;
        var cell = sheet.Cell(row, col);
        return CellParser.TryGetDecimal(cell, out var value) ? value : null;
    }
}
