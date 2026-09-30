using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace DisplayBoard.Core.Excel;

/// <summary>Một sheet dạng bảng: dòng đầu tiên có dữ liệu là header.</summary>
internal sealed class SheetTable
{
    private readonly Dictionary<string, int> _columns;

    private SheetTable(IXLWorksheet sheet, int headerRow, Dictionary<string, int> columns, int lastRow)
    {
        Sheet = sheet;
        HeaderRow = headerRow;
        _columns = columns;
        LastRow = lastRow;
    }

    public IXLWorksheet Sheet { get; }
    public int HeaderRow { get; }
    public int LastRow { get; }

    public static SheetTable? TryOpen(XLWorkbook workbook, string sheetName)
    {
        var sheet = workbook.Worksheets.FirstOrDefault(ws => NormalizeHeader(ws.Name) == NormalizeHeader(sheetName));
        if (sheet is null)
            return null;

        var used = sheet.RangeUsed();
        if (used is null)
            return new SheetTable(sheet, 1, [], 1);

        var headerRow = used.FirstRow().RowNumber();
        var columns = new Dictionary<string, int>();
        foreach (var cell in sheet.Row(headerRow).CellsUsed())
        {
            var key = NormalizeHeader(cell.GetString());
            if (key.Length > 0)
                columns.TryAdd(key, cell.Address.ColumnNumber);
        }
        return new SheetTable(sheet, headerRow, columns, used.LastRow().RowNumber());
    }

    public bool Has(string header) => _columns.ContainsKey(NormalizeHeader(header));

    public IXLCell? Cell(int row, string header) =>
        _columns.TryGetValue(NormalizeHeader(header), out var col) ? Sheet.Cell(row, col) : null;

    public string? Text(int row, string header)
    {
        var cell = Cell(row, header);
        return cell is null ? null : CellParser.GetText(cell);
    }

    public bool IsRowBlank(int row) => _columns.Values.All(col => CellParser.IsBlank(Sheet.Cell(row, col)));

    public IEnumerable<int> DataRows()
    {
        for (var row = HeaderRow + 1; row <= LastRow; row++)
        {
            if (!IsRowBlank(row))
                yield return row;
        }
    }

    /// <summary>Trim, bỏ phân biệt hoa/thường, bỏ dấu và khoảng trắng để "Mã NV", "ma nv", "MÃ  NV" khớp nhau.</summary>
    public static string NormalizeHeader(string header)
    {
        var decomposed = header.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
        }
        return builder.ToString();
    }
}
