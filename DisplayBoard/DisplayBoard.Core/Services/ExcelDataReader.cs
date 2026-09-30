using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisplayBoard.Core.Services;

/// <summary>
/// Reads the DATA sheet with ClosedXML. The file is opened with FileShare.ReadWrite so it can be read
/// while Excel has it open, and it is never written.
/// </summary>
public sealed partial class ExcelDataReader : IExcelDataReader
{
    private static readonly string[] DateFormats =
        ["d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "yyyy-MM-dd", "yyyy/MM/dd"];

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    private readonly ILogger<ExcelDataReader> _logger;

    [GeneratedRegex(@"^-?[1-9]\d{0,2}([.,]\d{3})+$")]
    private static partial Regex AmbiguousGrouping();

    public ExcelDataReader(ILogger<ExcelDataReader>? logger = null)
    {
        _logger = logger ?? NullLogger<ExcelDataReader>.Instance;
    }

    public async Task<ExcelReadResult> ReadAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new ExcelValidationException($"Không tìm thấy file Excel: {filePath}");
        }

        var stopwatch = Stopwatch.StartNew();

        // Copy into memory first so the file handle is held as briefly as possible while Excel is saving.
        var buffer = new MemoryStream();
        await using (var file = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true))
        {
            await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
        }

        buffer.Position = 0;
        var result = await Task.Run(() => Read(buffer, ct), ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Read {RecordCount} rows from {FileName} in {ElapsedMs} ms; {WarningCount} rows skipped",
            result.Records.Count,
            Path.GetFileName(filePath),
            stopwatch.ElapsedMilliseconds,
            result.Warnings.Count);

        foreach (var warning in result.Warnings)
        {
            _logger.LogWarning("Skipped row: {Warning}", warning.ToString());
        }

        return result;
    }

    /// <summary>Reads and validates a workbook from a stream (.xlsx content).</summary>
    public ExcelReadResult Read(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            throw new ExcelValidationException("File không phải là workbook Excel (.xlsx) hợp lệ.", innerException: ex);
        }

        using (workbook)
        {
            var sheet = FindDataSheet(workbook)
                ?? throw new ExcelValidationException($"Không tìm thấy sheet '{DataSheetSchema.SheetName}' trong workbook.");

            return ReadSheet(sheet, ct);
        }
    }

    private static IXLWorksheet? FindDataSheet(XLWorkbook workbook)
    {
        var expected = DataSheetSchema.NormalizeHeader(DataSheetSchema.SheetName);
        return workbook.Worksheets.FirstOrDefault(s => DataSheetSchema.NormalizeHeader(s.Name) == expected);
    }

    private static ExcelReadResult ReadSheet(IXLWorksheet sheet, CancellationToken ct)
    {
        var headerRow = sheet.FirstRowUsed()
            ?? throw new ExcelValidationException(
                $"Sheet '{DataSheetSchema.SheetName}' đang trống.", DataSheetSchema.RequiredColumns);

        var columns = MapColumns(headerRow);

        var records = new List<ProductionRecord>();
        var warnings = new List<ExcelRowIssue>();
        var lastRow = sheet.LastRowUsed()!.RowNumber();

        for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRow; rowNumber++)
        {
            ct.ThrowIfCancellationRequested();

            var row = sheet.Row(rowNumber);
            if (columns.Values.All(c => IsBlank(GetValue(row.Cell(c)))))
            {
                continue;
            }

            var parser = new RowParser(row, rowNumber, columns);
            var record = parser.Parse();
            if (record is not null)
            {
                records.Add(record);
            }
            else
            {
                warnings.Add(parser.Issue!);
            }
        }

        return new ExcelReadResult(records, warnings);
    }

    /// <summary>Maps canonical column name to its 1-based column number.</summary>
    private static Dictionary<string, int> MapColumns(IXLRow headerRow)
    {
        var known = DataSheetSchema.AllColumns.ToDictionary(DataSheetSchema.NormalizeHeader, c => c);
        var map = new Dictionary<string, int>();

        foreach (var cell in headerRow.CellsUsed())
        {
            var normalized = DataSheetSchema.NormalizeHeader(GetValue(cell).ToString(CultureInfo.InvariantCulture));
            if (!known.TryGetValue(normalized, out var canonical))
            {
                continue;
            }

            if (map.ContainsKey(canonical))
            {
                throw new ExcelValidationException(
                    $"Cột '{canonical}' xuất hiện nhiều lần trong dòng tiêu đề (dòng {headerRow.RowNumber()}).");
            }

            map[canonical] = cell.Address.ColumnNumber;
        }

        var missing = DataSheetSchema.RequiredColumns.Where(c => !map.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            throw new ExcelValidationException(
                $"Sheet '{DataSheetSchema.SheetName}' thiếu cột bắt buộc: {string.Join(", ", missing)}.", missing);
        }

        return map;
    }

    private static XLCellValue GetValue(IXLCell cell)
    {
        // Prefer the value Excel saved for formulas; ClosedXML's own evaluation may not support every function.
        if (cell.HasFormula)
        {
            var cached = cell.CachedValue;
            return cached.IsBlank ? cell.Value : cached;
        }

        return cell.Value;
    }

    private static bool IsBlank(XLCellValue value) =>
        value.IsBlank || (value.IsText && string.IsNullOrWhiteSpace(value.GetText()));

    private sealed class RowParser(IXLRow row, int rowNumber, IReadOnlyDictionary<string, int> columns)
    {
        public ExcelRowIssue? Issue { get; private set; }

        public ProductionRecord? Parse()
        {
            if (!TryGetDate(DataSheetSchema.Date, out var date)
                || !TryGetRequiredText(DataSheetSchema.EmployeeCode, out var employeeCode)
                || !TryGetRequiredText(DataSheetSchema.EmployeeName, out var employeeName)
                || !TryGetRequiredText(DataSheetSchema.Department, out var department)
                || !TryGetNumber(DataSheetSchema.Quantity, required: true, out var quantity)
                || !TryGetNumber(DataSheetSchema.Target, required: false, out var target))
            {
                return null;
            }

            var note = GetOptionalText(DataSheetSchema.Note);
            return new ProductionRecord(date, employeeCode, employeeName, department, quantity!.Value, target, note);
        }

        private XLCellValue? Value(string column) =>
            columns.TryGetValue(column, out var columnNumber) ? GetValue(row.Cell(columnNumber)) : (XLCellValue?)null;

        private bool Fail(string column, string message)
        {
            Issue = new ExcelRowIssue(rowNumber, column, message);
            return false;
        }

        private bool TryGetDate(string column, out DateOnly date)
        {
            date = default;
            var value = Value(column);
            if (value is null || IsBlank(value.Value))
            {
                return Fail(column, "thiếu giá trị bắt buộc.");
            }

            var v = value.Value;
            if (v.IsDateTime)
            {
                date = DateOnly.FromDateTime(v.GetDateTime());
                return true;
            }

            if (v.IsNumber)
            {
                var serial = v.GetNumber();
                if (serial is >= 1 and < 2958466)
                {
                    date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
                    return true;
                }
            }

            if (v.IsText
                && DateTime.TryParseExact(v.GetText().Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                date = DateOnly.FromDateTime(parsed);
                return true;
            }

            return Fail(column, $"'{Describe(v)}' không phải là ngày hợp lệ (dd/MM/yyyy).");
        }

        private bool TryGetRequiredText(string column, out string text)
        {
            text = string.Empty;
            var value = Value(column);
            if (value is null || IsBlank(value.Value))
            {
                return Fail(column, "thiếu giá trị bắt buộc.");
            }

            if (value.Value.IsError)
            {
                return Fail(column, $"ô có lỗi công thức {value.Value.GetError()}.");
            }

            text = AsText(value.Value);
            return true;
        }

        private string? GetOptionalText(string column)
        {
            var value = Value(column);
            if (value is null || IsBlank(value.Value) || value.Value.IsError)
            {
                return null;
            }

            return AsText(value.Value);
        }

        private bool TryGetNumber(string column, bool required, out decimal? number)
        {
            number = null;
            var value = Value(column);
            if (value is null || IsBlank(value.Value))
            {
                return required ? Fail(column, "thiếu giá trị bắt buộc.") : true;
            }

            var v = value.Value;
            if (v.IsNumber)
            {
                var d = v.GetNumber();
                if (double.IsFinite(d) && Math.Abs(d) <= (double)decimal.MaxValue)
                {
                    number = (decimal)d;
                }
            }
            else if (v.IsText)
            {
                number = ParseNumberText(v.GetText());
            }

            if (number is null)
            {
                return Fail(column, $"'{Describe(v)}' không phải là số.");
            }

            if (number < 0)
            {
                number = null;
                return Fail(column, "không được là số âm.");
            }

            return true;
        }

        private static decimal? ParseNumberText(string text)
        {
            var trimmed = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

            // "1.250" / "1,250" could mean 1250 (vi-VN / en-US grouping) or 1.25, so refuse to guess.
            if (AmbiguousGrouping().IsMatch(trimmed))
            {
                return null;
            }

            // "1250.5" (dot decimal) first, then Vietnamese "1250,5" (comma decimal).
            if (decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant))
            {
                return invariant;
            }

            if (decimal.TryParse(trimmed, NumberStyles.Float, Vietnamese, out var vietnamese))
            {
                return vietnamese;
            }

            return null;
        }

        private static string AsText(XLCellValue value) =>
            value.IsNumber
                ? value.GetNumber().ToString(CultureInfo.InvariantCulture)
                : value.ToString(CultureInfo.InvariantCulture).Trim();

        private static string Describe(XLCellValue value) =>
            value.IsError ? value.GetError().ToString() : value.ToString(CultureInfo.InvariantCulture);
    }
}
