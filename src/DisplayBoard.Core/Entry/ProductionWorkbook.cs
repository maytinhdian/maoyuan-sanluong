using System.Globalization;
using DisplayBoard.Core.Excel;

namespace DisplayBoard.Core.Entry;

/// <summary>
/// Đọc và ghi các sheet nhập liệu của file V20: NHAP_LIEU (sản lượng theo giờ) và HANG_LOI (hàng lỗi).
/// Chỉ ghi vào ô nhập tay; cột công thức để Excel tự tính. Cột tìm theo tiêu đề tiếng Việt nên thêm cột không làm lệch.
/// </summary>
public static class ProductionWorkbook
{
    public const string EntrySheet = "NHAP_LIEU";
    public const string DefectSheet = "HANG_LOI";
    public const string LineSheet = "DANH_SACH_CHUYEN";
    public const string ProductSheet = "DANH_SACH_SAN_PHAM";
    public const string ShiftSheet = "CAU_HINH_CA";
    public const string DefectTypeSheet = "DANH_SACH_LOAI_LOI";
    public const int MaxHours = 12;

    private const string Date = "NGÀY", Line = "CHUYỀN", Product = "MÃ SẢN PHẨM", Shift = "MÃ CA", HourlyTarget = "MỤC TIÊU MỖI GIỜ";
    private static string HourHeader(int hour) => $"GIỜ {hour}";

    // ---------- Đọc ----------

    public static EntryContext ReadContext(IEntryWorkbook workbook, DateOnly date, TimeOnly now, IReadOnlyCollection<string>? allowedLines, string? line)
    {
        var shifts = ReadShifts(workbook);
        var lines = ReadLines(workbook).Where(l => Allowed(allowedLines, l)).ToList();
        var selected = line is null ? null : lines.FirstOrDefault(l => Same(l, line));
        return new EntryContext(
            date,
            lines,
            ReadProducts(workbook),
            shifts,
            ReadList(workbook, DefectTypeSheet, "LOẠI LỖI"),
            selected is null ? null : ReadLineDay(workbook, date, now, selected, shifts));
    }

    public static bool Allowed(IReadOnlyCollection<string>? allowedLines, string line) =>
        allowedLines is null || allowedLines.Count == 0 || allowedLines.Any(a => Same(a, line));

    public static bool Same(string? a, string? b) =>
        a is not null && b is not null && SheetTable.NormalizeHeader(a) == SheetTable.NormalizeHeader(b);

    public static LineDay ReadLineDay(IEntryWorkbook workbook, DateOnly date, TimeOnly now, string line, IReadOnlyList<ShiftInfo>? shifts = null)
    {
        shifts ??= ReadShifts(workbook);
        var sheet = Entries(workbook);
        var row = FindRow(sheet, date, line);
        DayPlan? plan = null;
        DateOnly? copiedFrom = null;
        if (row is not null)
            plan = ReadPlan(sheet, row.Value);
        else
        {
            var previous = PreviousPlan(sheet, date, line);
            if (previous is not null)
                (plan, copiedFrom) = (previous.Value.Plan, previous.Value.Date);
        }

        var shift = plan is null ? null : shifts.FirstOrDefault(s => Same(s.Code, plan.ShiftCode));
        var slotCount = shift?.Slots.Count is > 0 ? shift.Slots.Count : MaxHours;
        var hours = new List<HourValue>();
        for (var n = 1; n <= slotCount; n++)
        {
            var slot = shift?.Slots.ElementAtOrDefault(n - 1);
            var col = sheet.Column(HourHeader(n));
            var quantity = row is null || col is null ? null : EntryValues.Number(sheet.Sheet.Get(row.Value, col.Value));
            decimal? target = plan is null ? null : Math.Round(plan.HourlyTarget * (decimal)(slot?.Hours ?? 1), 0, MidpointRounding.AwayFromZero);
            hours.Add(new HourValue(n, slot?.Start, slot?.End, quantity, target));
        }

        decimal? dailyTarget = plan is null || shift is null ? null : Math.Round(plan.HourlyTarget * shift.Hours, 0, MidpointRounding.AwayFromZero);
        return new LineDay(line, date, row is not null, plan, copiedFrom, hours, hours.Sum(h => h.Quantity ?? 0), dailyTarget, SuggestHour(hours, now));
    }

    /// <summary>Giờ nên nhập: giờ trống đầu tiên đã bắt đầu; hết giờ trống thì giờ đang chạy.</summary>
    public static int? SuggestHour(IReadOnlyList<HourValue> hours, TimeOnly now)
    {
        if (hours.Count == 0)
            return null;
        if (hours.All(h => h.Start is null))
            return (hours.FirstOrDefault(h => h.Quantity is null) ?? hours[^1]).Number;
        var started = hours.Where(h => h.Start <= now).ToList();
        if (started.Count == 0)
            return hours[0].Number;
        return (started.FirstOrDefault(h => h.Quantity is null) ?? started[^1]).Number;
    }

    public static IReadOnlyList<ShiftInfo> ReadShifts(IEntryWorkbook workbook)
    {
        var sheet = LabeledSheet.Find(workbook, ShiftSheet, "MÃ CA", "ĐỢT 1 BẮT ĐẦU");
        if (sheet is null)
            return [];
        var shifts = new List<ShiftInfo>();
        foreach (var row in sheet.Rows())
        {
            var code = sheet.Text(row, "MÃ CA");
            if (code is null || !Active(sheet.Text(row, "TRẠNG THÁI")))
                continue;
            var periods = new List<(TimeOnly Start, TimeOnly End)>();
            for (var p = 1; p <= 3; p++)
            {
                var start = EntryValues.Time(sheet.Value(row, $"ĐỢT {p} BẮT ĐẦU"));
                var end = EntryValues.Time(sheet.Value(row, $"ĐỢT {p} KẾT THÚC"));
                if (start is not null && end is not null && end > start)
                    periods.Add((start.Value, end.Value));
            }
            var slots = new List<HourSlot>();
            foreach (var (start, end) in periods)
            {
                for (var s = start; s < end && slots.Count < MaxHours;)
                {
                    var e = (end - s) > TimeSpan.FromHours(1) ? s.AddHours(1) : end;
                    slots.Add(new HourSlot(slots.Count + 1, s, e));
                    if (e == end)
                        break;
                    s = e;
                }
            }
            var hours = (decimal)Math.Round(periods.Sum(p => (p.End - p.Start).TotalHours), 2);
            shifts.Add(new ShiftInfo(code, sheet.Text(row, "TÊN CA"), hours, slots));
        }
        return shifts;
    }

    public static IReadOnlyList<string> ReadLines(IEntryWorkbook workbook)
    {
        var sheet = LabeledSheet.Find(workbook, LineSheet, "TÊN CHUYỀN");
        if (sheet is not null)
        {
            var lines = sheet.Rows()
                .Where(r => Active(sheet.Text(r, "TRẠNG THÁI")))
                .Select(r => sheet.Text(r, "TÊN CHUYỀN"))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (lines.Count > 0)
                return lines;
        }
        // File không có danh sách chuyền: lấy các chuyền đã từng nhập.
        var entries = LabeledSheet.Find(workbook, EntrySheet, Date, Line);
        return entries is null ? [] : entries.Rows().Select(r => entries.Text(r, Line)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<ProductOption> ReadProducts(IEntryWorkbook workbook)
    {
        var sheet = LabeledSheet.Find(workbook, ProductSheet, Product);
        if (sheet is null)
            return [];
        return sheet.Rows()
            .Where(r => Active(sheet.Text(r, "TRẠNG THÁI")))
            .Select(r => (Code: sheet.Text(r, Product), Name: sheet.Text(r, "TÊN SẢN PHẨM")))
            .Where(p => p.Code is not null)
            .Select(p => new ProductOption(p.Code!, p.Name))
            .DistinctBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ReadList(IEntryWorkbook workbook, string sheetName, string header)
    {
        var sheet = LabeledSheet.Find(workbook, sheetName, header);
        return sheet is null ? [] : sheet.Rows().Select(r => sheet.Text(r, header)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Trạng thái trống hoặc "Đang sử dụng" là còn dùng; "Ngừng ..." là bỏ.</summary>
    private static bool Active(string? status) => status is null || !SheetTable.NormalizeHeader(status).StartsWith("ngung", StringComparison.Ordinal);

    // ---------- Ghi ----------

    /// <summary>
    /// Dòng NHAP_LIEU của chuyền trong ngày. Chưa có thì tạo mới với kế hoạch được truyền vào, hoặc chép từ ngày làm trước.
    /// Có <paramref name="plan"/> thì ghi đè mã sản phẩm, ca, mục tiêu giờ.
    /// </summary>
    public static int EnsureDayRow(IEntryWorkbook workbook, DateOnly date, string line, DayPlan? plan)
    {
        var sheet = Entries(workbook);
        var row = FindRow(sheet, date, line);
        if (row is null)
        {
            plan ??= PreviousPlan(sheet, date, line)?.Plan
                ?? throw new EntryException($"{line} chưa có kế hoạch ngày {date:dd/MM/yyyy}. Hãy chọn mã sản phẩm, ca và mục tiêu mỗi giờ trước.");
            row = sheet.NewRow(Date, Line);
            sheet.Set(row.Value, Date, date.ToDateTime(TimeOnly.MinValue));
            sheet.Set(row.Value, Line, line);
            WritePlan(sheet, row.Value, plan);
        }
        else if (plan is not null)
            WritePlan(sheet, row.Value, plan);
        return row.Value;
    }

    public static void WriteHour(IEntryWorkbook workbook, DateOnly date, string line, int hour, decimal? quantity)
    {
        if (hour is < 1 or > MaxHours)
            throw new EntryException($"Giờ {hour} không hợp lệ (chỉ có giờ 1 đến {MaxHours}).");
        var row = EnsureDayRow(workbook, date, line, null);
        var sheet = Entries(workbook);
        if (sheet.Column(HourHeader(hour)) is null)
            throw new EntryException($"Sheet {EntrySheet} không có cột {HourHeader(hour)}.");
        sheet.Set(row, HourHeader(hour), quantity is null ? null : (double)quantity.Value);
    }

    public static int AddDefect(IEntryWorkbook workbook, DefectRecord record)
    {
        // Mã sản phẩm ở HANG_LOI là công thức lấy từ dòng NHAP_LIEU cùng ngày + chuyền, nên dòng đó phải có trước.
        EnsureDayRow(workbook, record.Date, record.Line, null);
        var sheet = LabeledSheet.Find(workbook, DefectSheet, Date, Line, "LOẠI LỖI", "SỐ LƯỢNG")
            ?? throw new EntryException($"File Excel chưa có sheet {DefectSheet}. Cần dùng file V20 trở lên để báo hàng lỗi.");
        var row = sheet.NewRow(Date, Line);
        sheet.Set(row, Date, record.Date.ToDateTime(TimeOnly.MinValue));
        sheet.Set(row, "GIỜ", record.Time.ToTimeSpan());
        sheet.Set(row, Line, record.Line);
        sheet.Set(row, "LOẠI LỖI", record.DefectType);
        sheet.Set(row, "SỐ LƯỢNG", (double)record.Quantity);
        sheet.Set(row, "TÊN FILE ẢNH", string.IsNullOrWhiteSpace(record.ImageFiles) ? null : record.ImageFiles);
        sheet.Set(row, "GHI CHÚ", string.IsNullOrWhiteSpace(record.Note) ? null : record.Note.Trim());
        return row;
    }

    private static void WritePlan(LabeledSheet sheet, int row, DayPlan plan)
    {
        sheet.Set(row, Product, plan.ProductCode);
        sheet.Set(row, Shift, plan.ShiftCode);
        sheet.Set(row, HourlyTarget, (double)plan.HourlyTarget);
    }

    // ---------- Tìm dòng ----------

    private static LabeledSheet Entries(IEntryWorkbook workbook) =>
        LabeledSheet.Find(workbook, EntrySheet, Date, Line, Product, HourHeader(1))
        ?? throw new EntryException($"Không tìm thấy sheet {EntrySheet} (cần các cột NGÀY, CHUYỀN, MÃ SẢN PHẨM, GIỜ 1).");

    private static int? FindRow(LabeledSheet sheet, DateOnly date, string line)
    {
        foreach (var row in sheet.Rows())
        {
            if (EntryValues.Date(sheet.Value(row, Date)) == date && Same(sheet.Text(row, Line), line))
                return row;
        }
        return null;
    }

    private static DayPlan? ReadPlan(LabeledSheet sheet, int row)
    {
        var product = sheet.Text(row, Product);
        var shift = sheet.Text(row, Shift);
        var target = EntryValues.Number(sheet.Value(row, HourlyTarget));
        return product is null || shift is null || target is null ? null : new DayPlan(product, shift, target.Value);
    }

    /// <summary>Kế hoạch của ngày gần nhất trước <paramref name="date"/> có đủ mã sản phẩm, ca, mục tiêu.</summary>
    private static (DayPlan Plan, DateOnly Date)? PreviousPlan(LabeledSheet sheet, DateOnly date, string line)
    {
        (DayPlan Plan, DateOnly Date)? best = null;
        foreach (var row in sheet.Rows())
        {
            var d = EntryValues.Date(sheet.Value(row, Date));
            if (d is null || d >= date || (best is not null && d <= best.Value.Date) || !Same(sheet.Text(row, Line), line))
                continue;
            var plan = ReadPlan(sheet, row);
            if (plan is not null)
                best = (plan, d.Value);
        }
        return best;
    }

    /// <summary>Sheet có dòng tiêu đề tiếng Việt (dòng 3 ở file V18+, nằm trên header số của Excel Table).</summary>
    internal sealed class LabeledSheet
    {
        private const int HeaderSearchRows = 8;
        private readonly Dictionary<string, int> _columns;

        private LabeledSheet(IEntrySheet sheet, int labelRow, Dictionary<string, int> columns)
        {
            Sheet = sheet;
            LabelRow = labelRow;
            _columns = columns;
        }

        public IEntrySheet Sheet { get; }
        public int LabelRow { get; }

        public static LabeledSheet? Find(IEntryWorkbook workbook, string sheetName, params string[] required)
        {
            var sheet = workbook.Sheet(sheetName);
            if (sheet is null)
                return null;
            var lastColumn = sheet.LastColumn;
            for (var row = 1; row <= Math.Min(HeaderSearchRows, sheet.LastRow); row++)
            {
                var columns = new Dictionary<string, int>();
                for (var col = 1; col <= lastColumn; col++)
                {
                    var key = EntryValues.Text(sheet.Get(row, col)) is { } text ? SheetTable.NormalizeHeader(text) : "";
                    if (key.Length > 0)
                        columns.TryAdd(key, col);
                }
                var labeled = new LabeledSheet(sheet, row, columns);
                if (required.All(h => labeled.Column(h) is not null))
                    return labeled;
            }
            return null;
        }

        /// <summary>Khớp đúng tiêu đề trước, rồi mới khớp phần đầu ("MÃ SẢN PHẨM (tự lấy)").</summary>
        public int? Column(string header)
        {
            var key = SheetTable.NormalizeHeader(header);
            if (_columns.TryGetValue(key, out var col))
                return col;
            foreach (var (k, c) in _columns)
            {
                if (k.StartsWith(key, StringComparison.Ordinal) && !char.IsDigit(k[key.Length]))
                    return c;
            }
            return null;
        }

        public IEnumerable<int> Rows()
        {
            var first = LabelRow + 1;
            var last = Sheet.LastRow;
            if (Sheet.TableBody() is { } body)
                (first, last) = (Math.Max(first, body.First), Math.Max(body.Last, first - 1));
            for (var row = first; row <= last; row++)
                yield return row;
        }

        public object? Value(int row, string header) => Column(header) is { } col ? Sheet.Get(row, col) : null;

        public string? Text(int row, string header) => EntryValues.Text(Value(row, header));

        public void Set(int row, string header, object? value)
        {
            if (Column(header) is { } col)
                Sheet.Set(row, col, value);
        }

        /// <summary>Dòng trống cuối bảng (chưa có ngày và chuyền) thì dùng lại; không có thì thêm dòng vào Table.</summary>
        public int NewRow(params string[] keyHeaders)
        {
            var rows = Rows().ToList();
            var blankTail = rows.AsEnumerable().Reverse().TakeWhile(r => keyHeaders.All(h => Text(r, h) is null)).LastOrDefault();
            var row = blankTail > 0 ? blankTail : Sheet.AppendTableRow();
            if (row > LabelRow + 1)
                Sheet.FillFormulasFromAbove(row);
            return row;
        }
    }
}

/// <summary>Đổi giá trị ô (từ Excel hoặc ClosedXML) sang kiểu .NET.</summary>
public static class EntryValues
{
    private static readonly string[] DateFormats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy"];

    public static string? Text(object? value)
    {
        var text = value switch
        {
            null => null,
            string s => s,
            double d => d.ToString(CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static decimal? Number(object? value) => value switch
    {
        double d when !double.IsNaN(d) && !double.IsInfinity(d) => (decimal)d,
        int i => i,
        decimal m => m,
        string s when decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) => n,
        _ => null
    };

    public static DateOnly? Date(object? value) => value switch
    {
        DateTime dt => DateOnly.FromDateTime(dt),
        double d when d is > 1 and < 2958466 => DateOnly.FromDateTime(DateTime.FromOADate(d)),
        string s when DateTime.TryParseExact(s.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var p) => DateOnly.FromDateTime(p),
        _ => null
    };

    public static TimeOnly? Time(object? value) => value switch
    {
        DateTime dt => TimeOnly.FromDateTime(dt),
        TimeSpan ts when ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1) => TimeOnly.FromTimeSpan(ts),
        double d when d >= 0 => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Min(1439, Math.Round(d % 1 * 24 * 60)))),
        string s when TimeOnly.TryParse(s.Trim(), CultureInfo.InvariantCulture, out var t) => t,
        _ => null
    };
}
