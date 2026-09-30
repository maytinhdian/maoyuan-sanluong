using ClosedXML.Excel;

namespace DisplayBoard.Tests.Excel;

/// <summary>Builds small .xlsx files in a temp folder for reader tests.</summary>
internal sealed class TestWorkbook : IDisposable
{
    public static readonly string[] StandardHeaders =
        ["Ngày", "Mã NV", "Họ tên", "Bộ phận", "Sản lượng", "Mục tiêu", "Ghi chú"];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DisplayBoardTests", Guid.NewGuid().ToString("N"));

    public TestWorkbook()
    {
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Writes a workbook whose first row is <paramref name="headers"/> followed by <paramref name="rows"/>.</summary>
    public string Create(string[] headers, IEnumerable<object?[]> rows, string sheetName = "DATA", Action<IXLWorksheet>? customize = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);

        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        var rowNumber = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                sheet.Cell(rowNumber, c + 1).Value = ToCellValue(row[c]);
            }

            rowNumber++;
        }

        customize?.Invoke(sheet);

        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.xlsx");
        workbook.SaveAs(path);
        return path;
    }

    public string CreateStandard(params object?[][] rows) => Create(StandardHeaders, rows);

    public string WriteRaw(byte[] content)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.xlsx");
        File.WriteAllBytes(path, content);
        return path;
    }

    public string MissingPath() => Path.Combine(_directory, "khong-ton-tai.xlsx");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of temp files.
        }
    }

    private static XLCellValue ToCellValue(object? value) => value switch
    {
        null => Blank.Value,
        string s => s,
        int i => i,
        double d => d,
        decimal m => (double)m,
        DateTime dt => dt,
        bool b => b,
        _ => throw new ArgumentException($"Unsupported test value type {value.GetType()}"),
    };
}
