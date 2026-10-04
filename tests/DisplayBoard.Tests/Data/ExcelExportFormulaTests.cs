using ClosedXML.Excel;
using DisplayBoard.Core.Data;
using DisplayBoard.Core.Excel;
using Xunit.Abstractions;

namespace DisplayBoard.Tests.Data;

/// <summary>
/// Bài test quan trọng nhất của bản 4.x: số app tính phải khớp từng ô với công thức V20.
/// Xuất dữ liệu thử ra file Excel (cột tính là công thức), cho LibreOffice tính lại, đọc sheet HIEN_THI như bản 3.x
/// rồi so với <see cref="DisplayCalculator"/>.
/// </summary>
public sealed class ExcelExportFormulaTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("db-export-").FullName;

    public static TheoryData<string> Days => new()
    {
        "2026-09-01",   // ngày đầu: chưa có ngày làm trước, chưa có tháng trước
        "2026-09-14",   // sau Chủ nhật làm bù
        "2026-09-30",   // cuối tháng
        "2026-10-01",   // đầu tháng mới: thiếu tháng trước, thiếu hôm trước từ tháng trước
        "2026-10-12",   // sau ngày nghỉ T7 10/10
        "2026-10-14",   // hôm nay: đang nhập dở giờ
    };

    [Theory]
    [MemberData(nameof(Days))]
    public void Computed_display_matches_excel_formulas(string day)
    {
        if (!LibreOffice.Available(out var reason))
        {
            output.WriteLine("Bỏ qua: " + reason);
            return;
        }
        var date = DateOnly.Parse(day);
        var data = SampleProduction.Create();

        var file = Path.Combine(_dir, ExcelExporter.FileName(date));
        using (var stream = File.Create(file))
            ExcelExporter.Export(data, date, stream);
        var recalculated = LibreOffice.Recalculate(file);

        using var workbook = new XLWorkbook(recalculated);
        var excel = DisplaySheetReader.Read(workbook);
        var app = DisplayCalculator.Compute(data, date);

        Assert.Equal(6, app.Lines.Count);
        Assert.Contains(app.Lines, l => l.DailyActual is not null);
        var differences = DisplayComparer.Compare(excel, app);
        foreach (var d in differences)
            output.WriteLine(d);
        Assert.Empty(differences);
    }

    [Fact]
    public void Imported_export_has_no_differences_after_excel_calculates()
    {
        if (!LibreOffice.Available(out var reason))
        {
            output.WriteLine("Bỏ qua: " + reason);
            return;
        }
        var data = SampleProduction.Create(11);
        var file = Path.Combine(_dir, "import.xlsx");
        using (var stream = File.Create(file))
            ExcelExporter.Export(data, SampleProduction.LastDay, stream);
        var recalculated = LibreOffice.Recalculate(file);

        var imported = V20Importer.Read(recalculated);
        using var workbook = new XLWorkbook(recalculated);
        var check = V20Importer.Check(workbook, imported.Data);
        foreach (var row in check.Rows.Where(r => !r.Matches))
            output.WriteLine(row.ToString());
        foreach (var d in check.DisplayDifferences)
            output.WriteLine(d);
        Assert.True(check.HasExcelValues);
        Assert.Equal(data.Entries.Count, check.Rows.Count);
        Assert.Equal(0, check.Mismatches);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }
}
