using System.Globalization;
using ClosedXML.Excel;
using DisplayBoard.Core.Entry;
using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Models;
using LabeledSheet = DisplayBoard.Core.Entry.ProductionWorkbook.LabeledSheet;

namespace DisplayBoard.Core.Data;

/// <summary>Kết quả đọc file V20: dữ liệu để đưa vào SQLite và các điều cần người dùng xem lại.</summary>
public sealed record ImportResult(ProductionData Data, IReadOnlyList<string> Warnings);

/// <summary>Một dòng NHAP_LIEU: số Excel đã tính (lưu trong file) đặt cạnh số bản 4.x tính.</summary>
public sealed record ImportCheckRow(DateOnly Date, string Line, decimal? ExcelTarget, decimal? AppTarget, decimal? ExcelActual, decimal? AppActual)
{
    public bool Matches => Same(ExcelTarget, AppTarget) && Same(ExcelActual, AppActual);

    private static bool Same(decimal? a, decimal? b) => a is null ? b is null : b is not null && Math.Abs(a.Value - b.Value) < 0.0001m;
}

/// <summary>
/// Bảng so sánh sau khi nhập: từng dòng NHAP_LIEU, và bảng HIEN_THI của ngày Excel đang hiện.
/// <see cref="HasExcelValues"/> = false khi file chưa được Excel tính (chưa từng bấm Lưu trong Excel) nên không có số để so.
/// </summary>
public sealed record ImportCheck(bool HasExcelValues, IReadOnlyList<ImportCheckRow> Rows, IReadOnlyList<string> DisplayDifferences)
{
    public int Mismatches => Rows.Count(r => !r.Matches) + DisplayDifferences.Count;
}

/// <summary>
/// Đọc file Excel V18–V20 đang dùng (bản 3.x) để chuyển sang SQLite. Chỉ đọc cột nhập tay; cột công thức bản 4.x tự tính.
/// File gốc không bị sửa.
/// </summary>
public static class V20Importer
{
    public static ImportResult Read(string path)
    {
        using var buffer = new MemoryStream();
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            file.CopyTo(buffer);
        buffer.Position = 0;
        using var workbook = new XLWorkbook(buffer);
        return Read(workbook);
    }

    public static ImportResult Read(XLWorkbook xl)
    {
        var workbook = new ClosedXmlEntryWorkbook(xl);
        var warnings = new List<string>();

        var shifts = new List<ShiftDef>();
        if (Sheet(workbook, ProductionWorkbook.ShiftSheet, "MÃ CA", "ĐỢT 1 BẮT ĐẦU") is { } ca)
        {
            foreach (var row in ca.Rows())
            {
                var code = ca.Text(row, "MÃ CA");
                if (code is null)
                    continue;
                var periods = new List<WorkPeriod>();
                for (var p = 1; p <= 3; p++)
                {
                    var start = EntryValues.Time(ca.Value(row, $"ĐỢT {p} BẮT ĐẦU"));
                    var end = EntryValues.Time(ca.Value(row, $"ĐỢT {p} KẾT THÚC"));
                    if (start is not null && end is not null)
                        periods.Add(new WorkPeriod(start.Value, end.Value));
                }
                if (shifts.Any(s => ProductionData.Same(s.Code, code)))
                    warnings.Add($"CAU_HINH_CA: mã ca {code} bị trùng, chỉ lấy dòng đầu.");
                else
                    shifts.Add(new ShiftDef(code, ca.Text(row, "TÊN CA"), periods, Active(ca.Text(row, "TRẠNG THÁI"))));
            }
        }
        else
            warnings.Add("Không có sheet CAU_HINH_CA.");

        var lines = new List<LineDef>();
        if (Sheet(workbook, ProductionWorkbook.LineSheet, "TÊN CHUYỀN") is { } ch)
        {
            foreach (var row in ch.Rows())
            {
                var name = ch.Text(row, "TÊN CHUYỀN");
                if (name is null)
                    continue;
                var code = ch.Text(row, "MÃ CHUYỀN") ?? NewLineCode(lines);
                if (lines.Any(l => ProductionData.Same(l.Name, name) || ProductionData.Same(l.Code, code)))
                {
                    warnings.Add($"DANH_SACH_CHUYEN: {name} ({code}) bị trùng, chỉ lấy dòng đầu.");
                    continue;
                }
                lines.Add(new LineDef(code, name, ch.Text(row, "TÊN TRƯỞNG CHUYỀN"), Active(ch.Text(row, "TRẠNG THÁI"))));
            }
        }

        var products = new List<ProductDef>();
        if (Sheet(workbook, ProductionWorkbook.ProductSheet, "MÃ SẢN PHẨM") is { } sp)
        {
            foreach (var row in sp.Rows())
            {
                var code = sp.Text(row, "MÃ SẢN PHẨM");
                if (code is null || products.Any(p => ProductionData.Same(p.Code, code)))
                    continue;
                products.Add(new ProductDef(code, sp.Text(row, "TÊN SẢN PHẨM"), sp.Text(row, "GHI CHÚ"), Active(sp.Text(row, "TRẠNG THÁI"))));
            }
        }

        var calendar = new List<CalendarDay>();
        if (Sheet(workbook, "LICH_LAM_VIEC", "NGÀY", "LOẠI") is { } lich)
        {
            foreach (var row in lich.Rows())
            {
                var date = EntryValues.Date(lich.Value(row, "NGÀY"));
                var kind = lich.Text(row, "LOẠI");
                if (date is null || kind is null)
                    continue;
                if (calendar.Any(c => c.Date == date))
                    warnings.Add($"LICH_LAM_VIEC: ngày {date:dd/MM/yyyy} ghi hai lần, chỉ lấy dòng đầu.");
                else
                    calendar.Add(new CalendarDay(date.Value, kind, lich.Text(row, "GHI CHÚ")));
            }
        }

        var targets = new List<MonthTarget>();
        if (Sheet(workbook, "MUC_TIEU_THANG", "THÁNG", "MÃ SẢN PHẨM") is { } mt)
        {
            var targetHeader = mt.Column("TỔNG SẢN LƯỢNG MỤC TIÊU") is not null ? "TỔNG SẢN LƯỢNG MỤC TIÊU" : "MỤC TIÊU";
            foreach (var row in mt.Rows())
            {
                var month = Month(mt.Value(row, "THÁNG"));
                var product = mt.Text(row, "MÃ SẢN PHẨM");
                var target = EntryValues.Number(mt.Value(row, targetHeader));
                if (month is null || product is null || target is null)
                    continue;
                var existing = targets.FindIndex(t => t.Month == month && ProductionData.Same(t.ProductCode, product));
                if (existing >= 0)
                {
                    // Excel cộng các dòng trùng (SUMIFS), nên gộp lại cho cùng số.
                    warnings.Add($"MUC_TIEU_THANG: {product} tháng {month:MM/yyyy} có nhiều dòng, đã cộng lại.");
                    targets[existing] = targets[existing] with { Target = targets[existing].Target + target.Value };
                }
                else
                    targets.Add(new MonthTarget(month.Value, product, target.Value, mt.Text(row, "GHI CHÚ")));
            }
        }

        var entries = new List<DayEntry>();
        if (Sheet(workbook, ProductionWorkbook.EntrySheet, "NGÀY", "CHUYỀN", "GIỜ 1") is { } nl)
        {
            foreach (var row in nl.Rows())
            {
                var date = EntryValues.Date(nl.Value(row, "NGÀY"));
                var lineName = nl.Text(row, "CHUYỀN");
                if (date is null || lineName is null)
                    continue;
                var line = FindOrAddLine(lines, lineName, warnings);
                if (entries.Any(e => e.Date == date && ProductionData.Same(e.LineCode, line.Code)))
                {
                    warnings.Add($"NHAP_LIEU dòng {row}: {lineName} ngày {date:dd/MM/yyyy} bị trùng, bỏ qua dòng này (Excel cũng chỉ lấy dòng đầu).");
                    continue;
                }
                entries.Add(new DayEntry
                {
                    Date = date.Value,
                    LineCode = line.Code,
                    ProductCode = nl.Text(row, "MÃ SẢN PHẨM"),
                    ShiftCode = nl.Text(row, "MÃ CA"),
                    HourlyTarget = EntryValues.Number(nl.Value(row, "MỤC TIÊU MỖI GIỜ")),
                    Hours = Enumerable.Range(1, DayEntry.MaxHours).Select(h => EntryValues.Number(nl.Value(row, $"GIỜ {h}"))).ToArray(),
                    Status = nl.Text(row, "TRẠNG THÁI"),
                    Note = nl.Text(row, "GHI CHÚ"),
                    Workers = EntryValues.Number(nl.Value(row, "SỐ CÔNG NHÂN")),
                    Reason = nl.Text(row, "LÝ DO KHÔNG ĐẠT"),
                    DowntimeMinutes = EntryValues.Number(nl.Value(row, "PHÚT DỪNG MÁY")),
                });
            }
        }
        else
            warnings.Add("Không có sheet NHAP_LIEU (cần file V18 trở lên).");

        var defects = new List<DefectRow>();
        if (Sheet(workbook, ProductionWorkbook.DefectSheet, "NGÀY", "CHUYỀN", "LOẠI LỖI") is { } hl)
        {
            foreach (var row in hl.Rows())
            {
                var date = EntryValues.Date(hl.Value(row, "NGÀY"));
                var lineName = hl.Text(row, "CHUYỀN");
                if (date is null || lineName is null)
                    continue;
                defects.Add(new DefectRow
                {
                    Id = defects.Count + 1,
                    Date = date.Value,
                    Time = EntryValues.Time(hl.Value(row, "GIỜ")),
                    LineCode = FindOrAddLine(lines, lineName, warnings).Code,
                    DefectType = hl.Text(row, "LOẠI LỖI"),
                    Quantity = EntryValues.Number(hl.Value(row, "SỐ LƯỢNG")),
                    ImageFiles = hl.Text(row, "TÊN FILE ẢNH"),
                    Note = hl.Text(row, "GHI CHÚ"),
                });
            }
        }

        var data = new ProductionData
        {
            Shifts = shifts,
            Lines = lines,
            Products = products,
            Reasons = List(workbook, "DANH_SACH_LY_DO", "LÝ DO KHÔNG ĐẠT"),
            DefectTypes = List(workbook, ProductionWorkbook.DefectTypeSheet, "LOẠI LỖI"),
            Calendar = calendar,
            MonthTargets = targets,
            Entries = entries,
            Defects = defects,
            DisplayDateOverride = DisplayDateOverride(xl),
        };
        return new ImportResult(data, warnings);
    }

    /// <summary>So số Excel đã lưu trong file với số bản 4.x tính từ dữ liệu vừa đọc.</summary>
    public static ImportCheck Check(XLWorkbook xl, ProductionData data)
    {
        var workbook = new ClosedXmlEntryWorkbook(xl);
        var rows = new List<ImportCheckRow>();
        var hasValues = false;
        if (Sheet(workbook, ProductionWorkbook.EntrySheet, "NGÀY", "CHUYỀN", "GIỜ 1") is { } nl)
        {
            var seen = new HashSet<(DateOnly, string)>();
            foreach (var row in nl.Rows())
            {
                var date = EntryValues.Date(nl.Value(row, "NGÀY"));
                var lineName = nl.Text(row, "CHUYỀN");
                if (date is null || lineName is null || data.FindLine(lineName) is not { } line || !seen.Add((date.Value, line.Code.ToUpperInvariant())))
                    continue;
                var entry = DisplayCalculator.Entry(data, date.Value, line.Code);
                var excelTarget = EntryValues.Number(nl.Value(row, "MỤC TIÊU TRONG NGÀY"));
                var excelActual = EntryValues.Number(nl.Value(row, "THỰC TẾ TRONG NGÀY"));
                hasValues |= excelTarget is not null || excelActual is not null;
                rows.Add(new ImportCheckRow(date.Value, line.Name, excelTarget, entry is null ? null : DisplayCalculator.DailyTarget(data, entry),
                    excelActual, entry is { HasHours: true } ? entry.HoursTotal : null));
            }
        }

        var differences = new List<string>();
        try
        {
            var excel = DisplaySheetReader.Read(xl);
            hasValues |= excel.Lines.Count > 0;
            if (excel.Date is { } shown)
                differences.AddRange(DisplayComparer.Compare(excel, DisplayCalculator.Compute(data, shown)));
        }
        catch (ExcelValidationException)
        {
            // File chưa được Excel tính: không có bảng HIEN_THI để so.
        }
        return new ImportCheck(hasValues, hasValues ? rows : [], differences);
    }

    private static LabeledSheet? Sheet(IEntryWorkbook workbook, string name, params string[] headers) => LabeledSheet.Find(workbook, name, headers);

    private static IReadOnlyList<ListItem> List(IEntryWorkbook workbook, string sheetName, string header)
    {
        var sheet = Sheet(workbook, sheetName, header);
        if (sheet is null)
            return [];
        var items = new List<ListItem>();
        foreach (var row in sheet.Rows())
        {
            var name = sheet.Text(row, header);
            if (name is not null && !items.Any(i => ProductionData.Same(i.Name, name)))
                items.Add(new ListItem(name, sheet.Text(row, "GHI CHÚ")));
        }
        return items;
    }

    private static LineDef FindOrAddLine(List<LineDef> lines, string name, List<string> warnings)
    {
        var line = lines.FirstOrDefault(l => ProductionData.Same(l.Name, name));
        if (line is not null)
            return line;
        line = new LineDef(NewLineCode(lines), name, Active: false);
        lines.Add(line);
        warnings.Add($"Chuyền \"{name}\" có số liệu nhưng không có trong DANH_SACH_CHUYEN; đã thêm với trạng thái Ngừng sử dụng.");
        return line;
    }

    private static string NewLineCode(List<LineDef> lines)
    {
        for (var n = lines.Count + 1; ; n++)
        {
            var code = $"CH{n:00}";
            if (!lines.Any(l => ProductionData.Same(l.Code, code)))
                return code;
        }
    }

    private static bool Active(string? status) => status is null || !SheetTable.NormalizeHeader(status).StartsWith("ngung", StringComparison.Ordinal);

    /// <summary>Cột THÁNG: ô ngày (bất kỳ ngày nào trong tháng) hoặc chữ "09/2026".</summary>
    private static DateOnly? Month(object? value)
    {
        if (EntryValues.Date(value) is { } date)
            return new DateOnly(date.Year, date.Month, 1);
        if (EntryValues.Text(value) is { } text && DateTime.TryParseExact(text, ["MM/yyyy", "M/yyyy", "yyyy-MM"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return new DateOnly(parsed.Year, parsed.Month, 1);
        return null;
    }

    /// <summary>Ô J1 của HIEN_THI: ngày chọn tay để TV hiện.</summary>
    private static DateOnly? DisplayDateOverride(XLWorkbook xl)
    {
        var sheet = xl.Worksheets.FirstOrDefault(ws => SheetTable.NormalizeHeader(ws.Name) == SheetTable.NormalizeHeader(DisplayCalculator.SheetName));
        if (sheet is null)
            return null;
        return CellParser.TryGetDate(sheet.Cell("J1"), out var date) ? date : null;
    }
}
