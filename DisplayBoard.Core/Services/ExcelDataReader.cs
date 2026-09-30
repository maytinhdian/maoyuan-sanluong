using System.Globalization;
using ClosedXML.Excel;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Services;

/// <summary>
/// Reads the <c>DATA</c> sheet with ClosedXML. The file is opened with full sharing and copied
/// into memory first, so staff can keep the workbook open and saving in Excel.
/// </summary>
public sealed class ExcelDataReader(ILogger<ExcelDataReader> logger) : IExcelDataReader
{
    private static readonly string[] DateFormats =
        ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "d-M-yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "d.M.yyyy"];

    public async Task<ExcelReadResult> ReadAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var buffer = await CopyToMemoryAsync(filePath, ct).ConfigureAwait(false);

        // ClosedXML parsing is CPU-bound; keep it off the caller's (UI) thread.
        var result = await Task.Run(() => Parse(buffer, ct), ct).ConfigureAwait(false);

        logger.LogInformation(
            "Read {RecordCount} rows from {File} ({WarningCount} skipped)",
            result.Records.Count, Path.GetFileName(filePath), result.Warnings.Count);
        foreach (var warning in result.Warnings)
        {
            logger.LogWarning("Row {RowNumber} skipped: {Message}", warning.RowNumber, warning.Message);
        }

        return result;
    }

    private static async Task<MemoryStream> CopyToMemoryAsync(string filePath, CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            throw new ExcelValidationException(
                ExcelValidationError.FileNotFound, $"Không tìm thấy file: {filePath}");
        }

        try
        {
            await using var file = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920,
                useAsync: true);
            var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
            buffer.Position = 0;
            return buffer;
        }
        catch (FileNotFoundException ex)
        {
            throw new ExcelValidationException(
                ExcelValidationError.FileNotFound, $"Không tìm thấy file: {filePath}", innerException: ex);
        }
        catch (IOException ex)
        {
            throw new ExcelValidationException(
                ExcelValidationError.FileLocked, "File đang được sử dụng.", innerException: ex);
        }
    }

    private static ExcelReadResult Parse(Stream stream, CancellationToken ct)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ExcelValidationException(
                ExcelValidationError.InvalidWorkbook, "File không phải workbook Excel hợp lệ (.xlsx).", innerException: ex);
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(ws =>
                string.Equals(ws.Name.Trim(), ExcelSchema.DataSheet, StringComparison.OrdinalIgnoreCase))
                ?? throw new ExcelValidationException(
                    ExcelValidationError.MissingDataSheet, $"Thiếu sheet {ExcelSchema.DataSheet}.");

            var headerRow = sheet.FirstRowUsed()
                ?? throw MissingColumns(ExcelSchema.RequiredColumns);
            var columns = MapColumns(headerRow);

            var missing = ExcelSchema.RequiredColumns.Where(c => !columns.ContainsKey(c)).ToList();
            if (missing.Count > 0)
            {
                throw MissingColumns(missing);
            }

            var records = new List<ProductionRecord>();
            var warnings = new List<RowWarning>();
            var lastRow = sheet.LastRowUsed()!.RowNumber();

            for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRow; rowNumber++)
            {
                ct.ThrowIfCancellationRequested();

                var row = sheet.Row(rowNumber);
                if (columns.Values.All(c => row.Cell(c).Value.IsBlank))
                {
                    continue;
                }

                var errors = new List<string>();
                var record = ParseRow(row, columns, errors);
                if (record is null)
                {
                    warnings.Add(new RowWarning(rowNumber, string.Join("; ", errors)));
                }
                else
                {
                    records.Add(record);
                }
            }

            return new ExcelReadResult(records, warnings);
        }
    }

    private static Dictionary<string, int> MapColumns(IXLRow headerRow)
    {
        var known = ExcelSchema.RequiredColumns.Concat(ExcelSchema.OptionalColumns).ToList();
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in headerRow.CellsUsed())
        {
            var header = ExcelSchema.NormalizeHeader(cell.GetFormattedString());
            var match = known.FirstOrDefault(k => string.Equals(k, header, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                // First occurrence wins if a header is duplicated.
                columns.TryAdd(match, cell.Address.ColumnNumber);
            }
        }

        return columns;
    }

    private static ProductionRecord? ParseRow(IXLRow row, Dictionary<string, int> columns, List<string> errors)
    {
        IXLCell Cell(string column) => row.Cell(columns[column]);
        IXLCell? OptionalCell(string column) => columns.TryGetValue(column, out var c) ? row.Cell(c) : null;

        var date = ReadDate(Cell(ExcelSchema.Date), errors);
        var code = ReadRequiredText(Cell(ExcelSchema.EmployeeCode), ExcelSchema.EmployeeCode, errors);
        var name = ReadRequiredText(Cell(ExcelSchema.EmployeeName), ExcelSchema.EmployeeName, errors);
        var department = ReadRequiredText(Cell(ExcelSchema.Department), ExcelSchema.Department, errors);
        var quantity = ReadNumber(Cell(ExcelSchema.Quantity), ExcelSchema.Quantity, required: true, errors);
        var target = OptionalCell(ExcelSchema.Target) is { } targetCell
            ? ReadNumber(targetCell, ExcelSchema.Target, required: false, errors)
            : null;
        var note = OptionalCell(ExcelSchema.Note) is { } noteCell ? ReadOptionalText(noteCell) : null;

        if (errors.Count > 0)
        {
            return null;
        }

        return new ProductionRecord(date!.Value, code!, name!, department!, quantity!.Value, target, note);
    }

    private static DateOnly? ReadDate(IXLCell cell, List<string> errors)
    {
        var value = cell.Value;
        switch (value.Type)
        {
            case XLDataType.DateTime:
                return DateOnly.FromDateTime(value.GetDateTime());
            case XLDataType.Number:
                try
                {
                    return DateOnly.FromDateTime(DateTime.FromOADate(value.GetNumber()));
                }
                catch (ArgumentException)
                {
                    break;
                }
            case XLDataType.Text:
                var text = value.GetText().Trim();
                if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return date;
                }

                break;
            case XLDataType.Blank:
                errors.Add($"thiếu {ExcelSchema.Date}");
                return null;
        }

        errors.Add($"{ExcelSchema.Date} không hợp lệ '{cell.GetFormattedString()}'");
        return null;
    }

    private static decimal? ReadNumber(IXLCell cell, string column, bool required, List<string> errors)
    {
        var value = cell.Value;
        decimal? number = null;

        switch (value.Type)
        {
            case XLDataType.Blank:
                if (required)
                {
                    errors.Add($"thiếu {column}");
                }

                return null;
            case XLDataType.Number:
                number = (decimal)value.GetNumber();
                break;
            case XLDataType.Text:
                number = ParseDecimal(value.GetText());
                if (number is null && !required && string.IsNullOrWhiteSpace(value.GetText()))
                {
                    return null;
                }

                break;
        }

        if (number is null)
        {
            errors.Add($"{column} không phải số '{cell.GetFormattedString()}'");
            return null;
        }

        if (number < 0)
        {
            errors.Add($"{column} không được âm");
            return null;
        }

        return number;
    }

    private static decimal? ParseDecimal(string text)
    {
        text = text.Trim();
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        // Accept a comma as the decimal separator ("12,5"), as typed on Vietnamese keyboards.
        return decimal.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static string? ReadRequiredText(IXLCell cell, string column, List<string> errors)
    {
        var text = ReadOptionalText(cell);
        if (text is null)
        {
            errors.Add($"thiếu {column}");
        }

        return text;
    }

    private static string? ReadOptionalText(IXLCell cell)
    {
        var text = cell.GetFormattedString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static ExcelValidationException MissingColumns(IReadOnlyList<string> missing) =>
        new(ExcelValidationError.MissingColumns, $"Thiếu cột: {string.Join(", ", missing)}.", missing);
}
