using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DisplayBoard.Core.Data;

/// <summary>
/// Xuất dữ liệu SQLite ra file Excel đúng mẫu V20 (sheet, màu, danh sách chọn, biểu đồ báo cáo tháng giữ nguyên).
/// Cột nhập tay được điền số; cột tính giữ công thức V20, Excel tự tính lại khi mở file. Nhờ vậy nhân viên vẫn
/// kiểm tra được bằng công thức, và bài test so được số app tính với số công thức tính.
/// Sửa file mẫu trực tiếp ở mức XML (không qua ClosedXML) để không mất biểu đồ và định dạng.
/// </summary>
public static partial class ExcelExporter
{
    private const string TemplateResource = "DisplayBoard.Core.Data.Templates.Theo_doi_san_luong_V20.xlsx";
    private const int FirstDataRow = 5;
    private const string ActiveText = "Đang sử dụng", InactiveText = "Ngừng sử dụng";

    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly DateOnly ExcelEpoch = new(1899, 12, 30);

    /// <summary>
    /// Ghi file Excel vào <paramref name="output"/>. Lấy mọi dữ liệu đến hết ngày <paramref name="upTo"/> (để lũy kế,
    /// thiếu hôm trước, thiếu tháng trước tính đúng như trên TV). HIEN_THI hiện ngày <paramref name="upTo"/>,
    /// BAO_CAO_THANG hiện tháng của ngày đó.
    /// </summary>
    public static void Export(ProductionData data, DateOnly upTo, Stream output)
    {
        using (var template = typeof(ExcelExporter).Assembly.GetManifestResourceStream(TemplateResource)
            ?? throw new InvalidOperationException("Thiếu file mẫu Excel V20 trong chương trình."))
            template.CopyTo(output);
        output.Position = 0;

        using var zip = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true);
        var sheets = SheetParts(zip);
        var lines = data.Lines.OrderBy(l => l.Active ? 0 : 1).ToList();   // HIEN_THI lấy 6 chuyền đầu danh sách
        var entries = data.Entries.Where(e => e.Date <= upTo).OrderBy(e => e.Date).ThenBy(e => LineOrder(lines, e.LineCode)).ToList();
        var defects = data.Defects.Where(d => d.Date <= upTo).OrderBy(d => d.Date).ThenBy(d => d.Id).ToList();   // thứ tự nhập, như TV

        FillTable(zip, sheets["CAU_HINH_CA"], data.Shifts.Select(s => new Dictionary<int, object?>
        {
            [1] = s.Code, [2] = s.Name,
            [4] = Period(s, 0, start: true), [5] = Period(s, 0, start: false),
            [6] = Period(s, 1, start: true), [7] = Period(s, 1, start: false),
            [8] = Period(s, 2, start: true), [9] = Period(s, 2, start: false),
            [10] = s.Active ? ActiveText : InactiveText,
        }));
        FillTable(zip, sheets["DANH_SACH_CHUYEN"], lines.Select(l => new Dictionary<int, object?>
        {
            [1] = l.Code, [2] = l.Name, [3] = l.Leader, [4] = l.Active ? ActiveText : InactiveText,
        }));
        FillTable(zip, sheets["DANH_SACH_SAN_PHAM"], data.Products.Select(p => new Dictionary<int, object?>
        {
            [1] = p.Code, [2] = p.Name, [3] = p.Note, [4] = p.Active ? ActiveText : InactiveText,
        }));
        FillTable(zip, sheets["DANH_SACH_LY_DO"], data.Reasons.Select(r => new Dictionary<int, object?> { [1] = r.Name, [2] = r.Note }));
        FillTable(zip, sheets["DANH_SACH_LOAI_LOI"], data.DefectTypes.Select(r => new Dictionary<int, object?> { [1] = r.Name, [2] = r.Note }));
        FillTable(zip, sheets["LICH_LAM_VIEC"], data.Calendar.OrderBy(c => c.Date).Select(c => new Dictionary<int, object?>
        {
            [1] = c.Date, [2] = c.Kind, [4] = c.Note,
        }));
        FillTable(zip, sheets["NHAP_LIEU"], entries.Select(e =>
        {
            var row = new Dictionary<int, object?>
            {
                [1] = e.Date, [2] = data.LineName(e.LineCode), [3] = e.ProductCode, [4] = e.ShiftCode, [5] = e.HourlyTarget,
                [29] = e.Status, [30] = e.Note, [33] = e.Workers, [35] = e.Reason, [36] = e.DowntimeMinutes,
            };
            for (var h = 0; h < DayEntry.MaxHours; h++)
                row[8 + h] = e.Hours.ElementAtOrDefault(h);
            return row;
        }));
        FillTable(zip, sheets["HANG_LOI"], defects.Select(d => new Dictionary<int, object?>
        {
            [1] = d.Date, [2] = d.Time, [3] = data.LineName(d.LineCode), [5] = d.DefectType, [6] = d.Quantity,
            [7] = d.ImageFiles, [8] = d.Note,
        }));
        FillTable(zip, sheets["MUC_TIEU_THANG"], data.MonthTargets.OrderBy(t => t.Month).Select(t => new Dictionary<int, object?>
        {
            [1] = t.Month, [2] = t.ProductCode, [3] = t.Target, [4] = t.Note,
        }));

        // Ô chọn ngày của HIEN_THI và ô chọn tháng của BAO_CAO_THANG.
        SetCell(zip, sheets["HIEN_THI"], "J1", upTo);
        SetCell(zip, sheets["BAO_CAO_THANG"], "F2", new DateOnly(upTo.Year, upTo.Month, 1));
    }

    /// <summary>Tên file gợi ý: SanLuong_2026-10-04.xlsx.</summary>
    public static string FileName(DateOnly date) => $"SanLuong_{date:yyyy-MM-dd}.xlsx";

    private static int LineOrder(IReadOnlyList<LineDef> lines, string code)
    {
        for (var i = 0; i < lines.Count; i++)
            if (ProductionData.Same(lines[i].Code, code))
                return i;
        return int.MaxValue;
    }

    private static TimeOnly? Period(ShiftDef shift, int index, bool start) =>
        index < shift.Periods.Count ? (start ? shift.Periods[index].Start : shift.Periods[index].End) : null;

    // ---------- Gói xlsx ----------

    private sealed record SheetPart(string Path, string? TablePath);

    private static Dictionary<string, SheetPart> SheetParts(ZipArchive zip)
    {
        var workbook = Load(zip, "xl/workbook.xml");
        var rels = Load(zip, "xl/_rels/workbook.xml.rels").Root!.Elements(Rel + "Relationship")
            .ToDictionary(r => (string)r.Attribute("Id")!, r => PartPath("xl", (string)r.Attribute("Target")!));
        var result = new Dictionary<string, SheetPart>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Root!.Element(S + "sheets")!.Elements(S + "sheet"))
        {
            var path = rels[(string)sheet.Attribute(R + "id")!];
            var relPath = $"{Path.GetDirectoryName(path)!.Replace('\\', '/')}/_rels/{Path.GetFileName(path)}.rels";
            string? table = null;
            if (zip.GetEntry(relPath) is not null)
            {
                var tableRel = Load(zip, relPath).Root!.Elements(Rel + "Relationship")
                    .FirstOrDefault(r => ((string?)r.Attribute("Type"))?.EndsWith("/table", StringComparison.Ordinal) == true);
                if (tableRel is not null)
                    table = PartPath(Path.GetDirectoryName(path)!.Replace('\\', '/'), (string)tableRel.Attribute("Target")!);
            }
            result[(string)sheet.Attribute("name")!] = new SheetPart(path, table);
        }
        return result;
    }

    private static string PartPath(string baseDir, string target) =>
        target.StartsWith('/') ? target.TrimStart('/')
        : Path.GetFullPath(Path.Combine("/" + baseDir, target)).Replace('\\', '/').TrimStart('/');

    private static XDocument Load(ZipArchive zip, string path)
    {
        using var stream = zip.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }

    private static void Save(ZipArchive zip, string path, XDocument doc)
    {
        zip.GetEntry(path)!.Delete();
        using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        doc.Save(stream, SaveOptions.DisableFormatting);
    }

    // ---------- Điền bảng ----------

    /// <summary>
    /// Thay các dòng dữ liệu của Excel Table trên sheet bằng <paramref name="rows"/> (cột đánh số từ 1).
    /// Dòng 5 của mẫu làm khuôn: kiểu ô theo cột, cột có công thức thì chép công thức xuống từng dòng.
    /// Bảng rỗng vẫn giữ một dòng trống để Table hợp lệ và người dùng gõ tiếp được.
    /// </summary>
    private static void FillTable(ZipArchive zip, SheetPart part, IEnumerable<Dictionary<int, object?>> rows)
    {
        var doc = Load(zip, part.Path);
        var sheetData = doc.Root!.Element(S + "sheetData")!;
        var existing = sheetData.Elements(S + "row").ToList();
        var prototype = existing.First(r => (int)r.Attribute("r")! == FirstDataRow);
        var columns = prototype.Elements(S + "c").Select(c => new
        {
            Index = ColumnIndex((string)c.Attribute("r")!),
            Style = (string?)c.Attribute("s"),
            Formula = (string?)c.Element(S + "f"),
        }).ToList();
        var lastColumn = columns.Max(c => c.Index);
        foreach (var row in existing.Where(r => (int)r.Attribute("r")! >= FirstDataRow))
            row.Remove();

        var data = rows.ToList();
        if (data.Count == 0)
            data.Add([]);
        var rowNumber = FirstDataRow;
        foreach (var values in data)
        {
            var row = new XElement(S + "row", new XAttribute("r", rowNumber));
            foreach (var column in columns)
            {
                var cell = new XElement(S + "c", new XAttribute("r", ColumnName(column.Index) + rowNumber));
                if (column.Style is not null)
                    cell.Add(new XAttribute("s", column.Style));
                if (values.TryGetValue(column.Index, out var value) && value is not null)
                    WriteValue(cell, value);
                else if (column.Formula is not null)
                    cell.Add(new XElement(S + "f", ShiftFormula(column.Formula, rowNumber - FirstDataRow)));
                row.Add(cell);
            }
            sheetData.Add(row);
            rowNumber++;
        }

        var lastRow = rowNumber - 1;
        var lastRef = ColumnName(lastColumn) + lastRow;
        doc.Root.Element(S + "dimension")?.SetAttributeValue("ref", "A1:" + lastRef);
        // Danh sách chọn và tô màu có điều kiện phủ hết các dòng mới.
        foreach (var range in doc.Root.Descendants(S + "dataValidation").Concat(doc.Root.Descendants(S + "conditionalFormatting")))
            range.SetAttributeValue("sqref", ExtendRanges((string)range.Attribute("sqref")!, lastRow));
        Save(zip, part.Path, doc);

        if (part.TablePath is not null)
        {
            var table = Load(zip, part.TablePath);
            var start = ((string)table.Root!.Attribute("ref")!).Split(':')[0];
            table.Root.SetAttributeValue("ref", $"{start}:{lastRef}");
            table.Root.Element(S + "autoFilter")?.SetAttributeValue("ref", $"{start}:{lastRef}");
            Save(zip, part.TablePath, table);
        }
    }

    private static void SetCell(ZipArchive zip, SheetPart part, string address, object value)
    {
        var doc = Load(zip, part.Path);
        var sheetData = doc.Root!.Element(S + "sheetData")!;
        var rowNumber = int.Parse(RowDigits().Match(address).Value, CultureInfo.InvariantCulture);
        var row = sheetData.Elements(S + "row").FirstOrDefault(r => (int)r.Attribute("r")! == rowNumber);
        if (row is null)
        {
            row = new XElement(S + "row", new XAttribute("r", rowNumber));
            var after = sheetData.Elements(S + "row").LastOrDefault(r => (int)r.Attribute("r")! < rowNumber);
            if (after is null)
                sheetData.AddFirst(row);
            else
                after.AddAfterSelf(row);
        }
        var cell = row.Elements(S + "c").FirstOrDefault(c => (string?)c.Attribute("r") == address);
        if (cell is null)
        {
            cell = new XElement(S + "c", new XAttribute("r", address));
            var index = ColumnIndex(address);
            var after = row.Elements(S + "c").LastOrDefault(c => ColumnIndex((string)c.Attribute("r")!) < index);
            if (after is null)
                row.AddFirst(cell);
            else
                after.AddAfterSelf(cell);
        }
        cell.Attribute("t")?.Remove();
        cell.Elements().Remove();
        WriteValue(cell, value);
        Save(zip, part.Path, doc);
    }

    private static void WriteValue(XElement cell, object value)
    {
        string? number = value switch
        {
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            DateOnly date => (date.DayNumber - ExcelEpoch.DayNumber).ToString(CultureInfo.InvariantCulture),
            TimeOnly time => (time.ToTimeSpan().TotalDays).ToString("R", CultureInfo.InvariantCulture),
            _ => null
        };
        if (number is not null)
        {
            cell.SetAttributeValue("t", "n");
            cell.Add(new XElement(S + "v", number));
            return;
        }
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        cell.SetAttributeValue("t", "inlineStr");
        cell.Add(new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
    }

    // ---------- Địa chỉ ô và công thức ----------

    /// <summary>
    /// Dời các tham chiếu dòng tương đối (A5, $AJ5, H5:S5) xuống <paramref name="delta"/> dòng, như Excel chép công thức.
    /// Tham chiếu tuyệt đối ($A$5:$A$10004) và chữ trong ngoặc kép giữ nguyên.
    /// </summary>
    public static string ShiftFormula(string formula, int delta)
    {
        if (delta == 0)
            return formula;
        var parts = formula.Split('"');
        for (var i = 0; i < parts.Length; i += 2)   // phần chẵn nằm ngoài ngoặc kép
            parts[i] = CellReference().Replace(parts[i], m => m.Groups["rowAbs"].Value == "$"
                ? m.Value
                : m.Groups["colAbs"].Value + m.Groups["col"].Value + (int.Parse(m.Groups["row"].Value, CultureInfo.InvariantCulture) + delta));
        return string.Join('"', parts);
    }

    /// <summary>"B5:B9 H5:S9" → "B5:B{last} H5:S{last}"; "B5" → "B5:B{last}". Vùng không bắt đầu ở dòng 5 giữ nguyên.</summary>
    private static string ExtendRanges(string sqref, int lastRow) =>
        string.Join(' ', sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(range =>
        {
            var m = RangeReference().Match(range);
            if (!m.Success || m.Groups["row1"].Value != FirstDataRow.ToString(CultureInfo.InvariantCulture))
                return range;
            var col2 = m.Groups["col2"].Success ? m.Groups["col2"].Value : m.Groups["col1"].Value;
            return $"{m.Groups["col1"].Value}{FirstDataRow}:{col2}{Math.Max(lastRow, FirstDataRow)}";
        }));

    private static int ColumnIndex(string address)
    {
        var index = 0;
        foreach (var ch in address)
        {
            if (!char.IsAsciiLetter(ch))
                break;
            index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return index;
    }

    private static string ColumnName(int index)
    {
        var name = "";
        for (; index > 0; index = (index - 1) / 26)
            name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_.$])(?<colAbs>\$?)(?<col>[A-Z]{1,3})(?<rowAbs>\$?)(?<row>\d+)(?![A-Za-z0-9_(])")]
    private static partial Regex CellReference();

    [GeneratedRegex(@"^(?<col1>[A-Z]{1,3})(?<row1>\d+)(?::(?<col2>[A-Z]{1,3})(?<row2>\d+))?$")]
    private static partial Regex RangeReference();

    [GeneratedRegex(@"\d+")]
    private static partial Regex RowDigits();
}
