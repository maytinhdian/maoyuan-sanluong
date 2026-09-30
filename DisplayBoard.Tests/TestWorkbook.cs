using ClosedXML.Excel;

namespace DisplayBoard.Tests;

/// <summary>Creates throwaway .xlsx files for tests.</summary>
internal sealed class TestWorkbook : IDisposable
{
    public static readonly object?[] StandardHeader =
        ["Ngày", "Mã NV", "Họ tên", "Bộ phận", "Sản lượng", "Mục tiêu", "Ghi chú"];

    public TestWorkbook(string sheetName, params object?[][] rows)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"displayboard-{Guid.NewGuid():N}.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = sheet.Cell(r + 1, c + 1);
                cell.Value = rows[r][c] switch
                {
                    null => Blank.Value,
                    DateTime d => d,
                    string s => s,
                    int i => i,
                    double d => d,
                    decimal m => (double)m,
                    var other => other.ToString(),
                };
            }
        }

        workbook.SaveAs(Path);
    }

    public string Path { get; }

    public static TestWorkbook Data(params object?[][] dataRows) =>
        new("DATA", [StandardHeader, .. dataRows]);

    public void Dispose() => File.Delete(Path);
}
