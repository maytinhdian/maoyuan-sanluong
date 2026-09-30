using ClosedXML.Excel;
using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Excel;

/// <summary>
/// Đọc sheet HIEN_THI của file theo dõi sản lượng (V18). Sheet này toàn công thức: mỗi dòng một chuyền
/// của ngày đang hiển thị (ô M1), cuối bảng là dòng TỔNG CỘNG. App chỉ đọc giá trị Excel đã tính, không tính lại.
/// Cột được nhận theo tiêu đề tiếng Việt (dòng 3), nên thêm/đổi thứ tự cột vẫn đọc được.
/// </summary>
public static class DisplaySheetReader
{
    public const string DefaultSheetName = "HIEN_THI";
    private const int HeaderSearchRows = 8;
    private const int HourSlots = 12;

    internal enum Column
    {
        Date, Line, Product, ShiftCode, ShiftHours, HourlyTarget, DailyTarget, DailyActual, DailyRate,
        DailyVariance, Remaining, HoursEntered, TargetToNow, HourlyProgress, PreviousDay, CarriedShortfall,
        MonthTarget, MonthCumulative, MonthRate, MonthRemaining, LineMonthCumulative, Status, Note
    }

    // Tiêu đề tiếng Việt ở dòng 3 của HIEN_THI, so sánh sau khi bỏ dấu/khoảng trắng.
    private static readonly (Column Column, string Header)[] Headers =
    [
        (Column.Date, "NGÀY"),
        (Column.Line, "CHUYỀN"),
        (Column.Product, "MÃ SẢN PHẨM"),
        (Column.ShiftCode, "MÃ CA"),
        (Column.ShiftHours, "GIỜ CA"),
        (Column.HourlyTarget, "MỤC TIÊU MỖI GIỜ"),
        (Column.DailyTarget, "MỤC TIÊU TRONG NGÀY"),
        (Column.DailyActual, "THỰC TẾ TRONG NGÀY"),
        (Column.DailyRate, "TỶ LỆ ĐẠT TRONG NGÀY"),
        (Column.DailyVariance, "CHÊNH LỆCH"),
        (Column.Remaining, "CÒN THIẾU"),
        (Column.HoursEntered, "SỐ GIỜ ĐÃ NHẬP"),
        (Column.TargetToNow, "MỤC TIÊU ĐẾN GIỜ ĐÃ NHẬP"),
        (Column.HourlyProgress, "TIẾN ĐỘ THEO GIỜ"),
        (Column.PreviousDay, "NGÀY LÀM TRƯỚC"),
        (Column.CarriedShortfall, "THIẾU HÔM TRƯỚC"),
        (Column.MonthTarget, "MỤC TIÊU THÁNG (SẢN PHẨM)"),
        (Column.MonthCumulative, "LŨY KẾ THÁNG (SẢN PHẨM, đến ngày này)"),
        (Column.MonthRate, "TỶ LỆ ĐẠT THÁNG"),
        (Column.MonthRemaining, "CÒN THIẾU THÁNG"),
        (Column.LineMonthCumulative, "LŨY KẾ THÁNG CỦA CHUYỀN"),
        (Column.Status, "TRẠNG THÁI"),
        (Column.Note, "GHI CHÚ"),
    ];

    private static readonly Column[] Required = [Column.Line, Column.DailyTarget, Column.DailyActual];

    public static DisplaySheet Read(Stream stream, string? sheetName = null)
    {
        using var workbook = new XLWorkbook(stream);
        return Read(workbook, sheetName);
    }

    public static DisplaySheet Read(XLWorkbook workbook, string? sheetName = null)
    {
        var name = string.IsNullOrWhiteSpace(sheetName) ? DefaultSheetName : sheetName.Trim();
        var sheet = workbook.Worksheets.FirstOrDefault(ws => SheetTable.NormalizeHeader(ws.Name) == SheetTable.NormalizeHeader(name))
            ?? throw new ExcelValidationException($"Không tìm thấy sheet \"{name}\" trong file Excel.");

        var (headerRow, columns, hours) = FindHeader(sheet)
            ?? throw new ExcelValidationException($"Sheet \"{sheet.Name}\" không có dòng tiêu đề chứa cột CHUYỀN.");
        var missing = Required.Where(c => !columns.ContainsKey(c)).Select(Label).ToList();
        if (missing.Count > 0)
            throw new ExcelValidationException($"Sheet \"{sheet.Name}\" thiếu cột: {string.Join(", ", missing)}. Bố cục sheet có thể đã bị đổi.");

        var warnings = new List<string>();
        var lines = new List<ParsedRow>();
        ParsedRow? total = null;
        var uncalculated = false;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var lineCell = sheet.Cell(row, columns[Column.Line]);
            var line = CellParser.GetText(lineCell);
            if (line is null)
            {
                // Ô có công thức nhưng chưa có giá trị: file chưa được Excel tính.
                if (lineCell.HasFormula)
                    uncalculated = true;
                continue;
            }
            if (line.All(char.IsDigit))
                continue; // dòng số thứ tự cột (dòng tiêu đề của Excel Table)
            if (SheetTable.NormalizeHeader(line).StartsWith("tongcong", StringComparison.Ordinal))
            {
                total = ReadRow(sheet, row, line, columns, hours);
                break;
            }
            lines.Add(ReadRow(sheet, row, line, columns, hours));
        }

        if (lines.Count == 0 && uncalculated)
            throw new ExcelValidationException(
                "File chưa được Excel tính công thức (thường gặp khi file vừa tạo hoặc copy). Mở file bằng Excel, bấm Lưu (Ctrl+S) rồi đóng.");
        if (total is null)
            warnings.Add($"Sheet \"{sheet.Name}\": không thấy dòng TỔNG CỘNG.");

        var date = lines.Select(l => l.Date).FirstOrDefault(d => d is not null) ?? FindDateInTitle(sheet, headerRow);
        if (date is null)
            warnings.Add($"Sheet \"{sheet.Name}\": không đọc được ngày đang hiển thị.");

        return new DisplaySheet(sheet.Name, date, lines.Select(l => l.Record).ToList(), total?.Record, warnings);
    }

    private sealed record ParsedRow(LineRecord Record, DateOnly? Date);

    private static ParsedRow ReadRow(IXLWorksheet sheet, int row, string line, IReadOnlyDictionary<Column, int> columns, IReadOnlyList<int> hours)
    {
        decimal? Num(Column c) => columns.TryGetValue(c, out var col) && CellParser.TryGetDecimal(sheet.Cell(row, col), out var v) ? v : null;
        decimal? Pct(Column c) => columns.TryGetValue(c, out var col) ? Percent(sheet.Cell(row, col)) : null;
        string? Text(Column c) => columns.TryGetValue(c, out var col) ? CellParser.GetText(sheet.Cell(row, col)) : null;
        DateOnly? Date(Column c) => columns.TryGetValue(c, out var col) && CellParser.TryGetDate(sheet.Cell(row, col), out var d) ? d : null;

        var record = new LineRecord
        {
            RowNumber = row,
            Line = line,
            ProductCode = Text(Column.Product),
            ShiftCode = Text(Column.ShiftCode),
            ShiftHours = Num(Column.ShiftHours),
            HourlyTarget = Num(Column.HourlyTarget),
            DailyTarget = Num(Column.DailyTarget),
            DailyActual = Num(Column.DailyActual),
            DailyRate = Pct(Column.DailyRate),
            DailyVariance = Num(Column.DailyVariance),
            Remaining = Num(Column.Remaining),
            HoursEntered = Num(Column.HoursEntered),
            TargetToNow = Num(Column.TargetToNow),
            HourlyProgress = Pct(Column.HourlyProgress),
            PreviousDay = Date(Column.PreviousDay),
            CarriedShortfall = Num(Column.CarriedShortfall),
            MonthTarget = Num(Column.MonthTarget),
            MonthCumulative = Num(Column.MonthCumulative),
            MonthRate = Pct(Column.MonthRate),
            MonthRemaining = Num(Column.MonthRemaining),
            LineMonthCumulative = Num(Column.LineMonthCumulative),
            Hourly = hours.Select(col => CellParser.TryGetDecimal(sheet.Cell(row, col), out var v) ? v : (decimal?)null).ToList(),
            Status = Text(Column.Status),
            Note = Text(Column.Note),
        };
        return new ParsedRow(record, Date(Column.Date));
    }

    /// <summary>Ô tỷ lệ: số 0.375 (định dạng %) → 37.5; chữ "37.5%" → 37.5.</summary>
    private static decimal? Percent(IXLCell cell)
    {
        var raw = CellParser.Raw(cell);
        if (raw.IsNumber)
            return (decimal)raw.GetNumber() * 100;
        return CellParser.TryGetDecimal(cell, out var value) ? value : null;
    }

    private static (int Row, Dictionary<Column, int> Columns, List<int> Hours)? FindHeader(IXLWorksheet sheet)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var keys = Headers.ToDictionary(h => SheetTable.NormalizeHeader(h.Header), h => h.Column);
        for (var row = 1; row <= HeaderSearchRows; row++)
        {
            var columns = new Dictionary<Column, int>();
            var hours = new SortedDictionary<int, int>();
            for (var col = 1; col <= lastColumn; col++)
            {
                var text = CellParser.GetText(sheet.Cell(row, col));
                if (text is null)
                    continue;
                var key = SheetTable.NormalizeHeader(text);
                if (keys.TryGetValue(key, out var column))
                    columns.TryAdd(column, col);
                else if (key.StartsWith("gio", StringComparison.Ordinal) && int.TryParse(key[3..], out var slot) && slot is >= 1 and <= HourSlots)
                    hours.TryAdd(slot, col);
            }
            if (columns.ContainsKey(Column.Line))
                return (row, columns, hours.Values.ToList());
        }
        return null;
    }

    private static DateOnly? FindDateInTitle(IXLWorksheet sheet, int headerRow)
    {
        // M1 = ngày đang hiển thị; tìm ô ngày cuối cùng ở các dòng tiêu đề (bỏ qua ô chọn ngày J1 nếu M1 có giá trị).
        for (var row = 1; row < headerRow; row++)
        {
            var cells = sheet.Row(row).CellsUsed().Reverse();
            foreach (var cell in cells)
            {
                var raw = CellParser.Raw(cell);
                if (raw.IsDateTime)
                    return DateOnly.FromDateTime(raw.GetDateTime());
            }
        }
        return null;
    }

    private static string Label(Column column) => Headers.First(h => h.Column == column).Header;
}
