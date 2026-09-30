using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Services;

namespace DisplayBoard.Tests.Excel;

public sealed class ExcelDataReaderTests : IDisposable
{
    private static readonly DateTime Sep28 = new(2026, 9, 28);

    private readonly TestWorkbook _workbooks = new();
    private readonly ExcelDataReader _reader = new();

    public void Dispose() => _workbooks.Dispose();

    [Fact]
    public async Task Reads_sample_workbook()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", "SanLuong.xlsx");

        var result = await _reader.ReadAsync(path);

        Assert.Empty(result.Warnings);
        Assert.Equal(10, result.Records.Count);
        Assert.Equal(
            new ProductionRecord(new DateOnly(2026, 9, 28), "NV001", "Nguyễn Văn A", "Ép", 1250m, 1200m, null),
            result.Records[0]);
        Assert.Contains(result.Records, r => r is { EmployeeCode: "NV010", Target: null, Note: "Không giao chỉ tiêu" });
    }

    [Fact]
    public async Task Reads_valid_row_with_all_columns()
    {
        var path = _workbooks.CreateStandard([Sep28, "NV001", "Nguyễn Văn A", "Ép", 1250, 1200, "Ca đêm"]);

        var result = await _reader.ReadAsync(path);

        var record = Assert.Single(result.Records);
        Assert.Equal(new ProductionRecord(new DateOnly(2026, 9, 28), "NV001", "Nguyễn Văn A", "Ép", 1250m, 1200m, "Ca đêm"), record);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Matches_headers_ignoring_case_whitespace_and_order()
    {
        string[] headers = ["  sản   LƯỢNG ", "HỌ TÊN", "Cột lạ", "mã nv", "BỘ PHẬN", " ngày"];
        var path = _workbooks.Create(headers, [[980, "Trần Văn B", "bỏ qua", "NV002", "Sơn", Sep28]]);

        var result = await _reader.ReadAsync(path);

        var record = Assert.Single(result.Records);
        Assert.Equal("NV002", record.EmployeeCode);
        Assert.Equal("Trần Văn B", record.EmployeeName);
        Assert.Equal("Sơn", record.Department);
        Assert.Equal(980m, record.Quantity);
    }

    [Fact]
    public async Task Matches_decomposed_unicode_headers()
    {
        string[] headers = [.. TestWorkbook.StandardHeaders.Select(h => h.Normalize(System.Text.NormalizationForm.FormD))];
        var path = _workbooks.Create(headers, [[Sep28, "NV001", "A", "Ép", 1, 1, null]]);

        var result = await _reader.ReadAsync(path);

        Assert.Single(result.Records);
    }

    [Fact]
    public async Task Optional_columns_may_be_absent()
    {
        string[] headers = ["Ngày", "Mã NV", "Họ tên", "Bộ phận", "Sản lượng"];
        var path = _workbooks.Create(headers, [[Sep28, "NV001", "A", "Ép", 100]]);

        var result = await _reader.ReadAsync(path);

        var record = Assert.Single(result.Records);
        Assert.Null(record.Target);
        Assert.Null(record.Note);
    }

    [Fact]
    public async Task Finds_data_sheet_case_insensitively_among_other_sheets()
    {
        var path = _workbooks.Create(
            TestWorkbook.StandardHeaders,
            [[Sep28, "NV001", "A", "Ép", 100, null, null]],
            sheetName: "data",
            customize: sheet => sheet.Workbook.AddWorksheet("DANH_MUC").Cell(1, 1).Value = "Mã NV");

        var result = await _reader.ReadAsync(path);

        Assert.Single(result.Records);
    }

    [Fact]
    public async Task Missing_data_sheet_throws()
    {
        var path = _workbooks.Create(TestWorkbook.StandardHeaders, [], sheetName: "Sheet1");

        var ex = await Assert.ThrowsAsync<ExcelValidationException>(() => _reader.ReadAsync(path));

        Assert.Contains("DATA", ex.Message);
    }

    [Fact]
    public async Task Missing_required_columns_are_listed()
    {
        string[] headers = ["Ngày", "Họ tên", "Mục tiêu", "Ghi chú"];
        var path = _workbooks.Create(headers, [[Sep28, "A", 100, null]]);

        var ex = await Assert.ThrowsAsync<ExcelValidationException>(() => _reader.ReadAsync(path));

        Assert.Equal(["Mã NV", "Bộ phận", "Sản lượng"], ex.MissingColumns);
        Assert.Contains("Mã NV, Bộ phận, Sản lượng", ex.Message);
    }

    [Fact]
    public async Task Duplicate_header_throws()
    {
        string[] headers = ["Ngày", "Mã NV", "Họ tên", "Bộ phận", "Sản lượng", "sản lượng"];
        var path = _workbooks.Create(headers, []);

        var ex = await Assert.ThrowsAsync<ExcelValidationException>(() => _reader.ReadAsync(path));

        Assert.Contains("Sản lượng", ex.Message);
    }

    [Fact]
    public async Task Empty_data_sheet_throws()
    {
        var path = _workbooks.Create([], []);

        var ex = await Assert.ThrowsAsync<ExcelValidationException>(() => _reader.ReadAsync(path));

        Assert.Equal(5, ex.MissingColumns.Count);
    }

    [Fact]
    public async Task Header_only_sheet_returns_no_records()
    {
        var path = _workbooks.CreateStandard();

        var result = await _reader.ReadAsync(path);

        Assert.Empty(result.Records);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Missing_file_throws_validation_error()
    {
        var ex = await Assert.ThrowsAsync<ExcelValidationException>(() => _reader.ReadAsync(_workbooks.MissingPath()));

        Assert.Contains("Không tìm thấy file", ex.Message);
    }

    [Fact]
    public async Task Non_xlsx_file_throws_validation_error()
    {
        var path = _workbooks.WriteRaw("không phải excel"u8.ToArray());

        var ex = await Assert.ThrowsAsync<ExcelValidationException>(() => _reader.ReadAsync(path));

        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task Invalid_rows_are_skipped_with_row_numbers()
    {
        var path = _workbooks.CreateStandard(
            [Sep28, "NV001", "A", "Ép", 100, 100, null],        // row 2 ok
            [Sep28, null, "B", "Ép", 100, 100, null],           // row 3 missing code
            ["30/02/2026", "NV003", "C", "Ép", 100, 100, null], // row 4 bad date
            [Sep28, "NV004", "D", "Ép", -5, 100, null],         // row 5 negative
            [Sep28, "NV005", "E", "Ép", "nhiều", 100, null],    // row 6 not a number
            [Sep28, "NV006", "F", "Ép", 100, "abc", null],      // row 7 bad optional target
            [Sep28, "NV007", "G", "Ép", "1.250", null, null],   // row 8 ambiguous grouping
            [Sep28, "NV008", "H", null, 100, null, null],       // row 9 missing department
            [Sep28, "NV009", "I", "Sơn", 200, null, null]);     // row 10 ok

        var result = await _reader.ReadAsync(path);

        Assert.Equal(["NV001", "NV009"], result.Records.Select(r => r.EmployeeCode));
        Assert.Equal(
            [
                (3, "Mã NV"),
                (4, "Ngày"),
                (5, "Sản lượng"),
                (6, "Sản lượng"),
                (7, "Mục tiêu"),
                (8, "Sản lượng"),
                (9, "Bộ phận"),
            ],
            result.Warnings.Select(w => (w.RowNumber, w.Column!)));
        Assert.StartsWith("Dòng 3, cột 'Mã NV':", result.Warnings[0].ToString());
    }

    [Fact]
    public async Task Blank_rows_are_ignored_silently()
    {
        var path = _workbooks.CreateStandard(
            [Sep28, "NV001", "A", "Ép", 100, null, null],
            [null, null, null, null, null, null, null],
            ["   ", null, "", null, null, null, null],
            [Sep28, "NV002", "B", "Ép", 200, null, null]);

        var result = await _reader.ReadAsync(path);

        Assert.Equal(2, result.Records.Count);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("28/09/2026")]
    [InlineData("28/9/2026")]
    [InlineData("2026-09-28")]
    [InlineData(" 28-09-2026 ")]
    public async Task Parses_dates_stored_as_text(string text)
    {
        var path = _workbooks.CreateStandard([text, "NV001", "A", "Ép", 100, null, null]);

        var result = await _reader.ReadAsync(path);

        Assert.Equal(new DateOnly(2026, 9, 28), Assert.Single(result.Records).Date);
    }

    [Fact]
    public async Task Parses_dates_stored_as_serial_numbers()
    {
        var path = _workbooks.CreateStandard([Sep28.ToOADate(), "NV001", "A", "Ép", 100, null, null]);

        var result = await _reader.ReadAsync(path);

        Assert.Equal(new DateOnly(2026, 9, 28), Assert.Single(result.Records).Date);
    }

    [Theory]
    [InlineData("1250", 1250)]
    [InlineData(" 1250.5 ", 1250.5)]
    [InlineData("1250,5", 1250.5)]
    [InlineData("0", 0)]
    [InlineData("0.250", 0.25)]
    public async Task Parses_numbers_stored_as_text(string text, double expected)
    {
        var path = _workbooks.CreateStandard([Sep28, "NV001", "A", "Ép", text, text, null]);

        var result = await _reader.ReadAsync(path);

        var record = Assert.Single(result.Records);
        Assert.Equal((decimal)expected, record.Quantity);
        Assert.Equal((decimal)expected, record.Target);
    }

    [Fact]
    public async Task Numeric_employee_code_becomes_text_and_text_is_trimmed()
    {
        var path = _workbooks.CreateStandard([Sep28, 1001, "  Nguyễn Văn A ", " Ép ", 100, null, "  "]);

        var result = await _reader.ReadAsync(path);

        var record = Assert.Single(result.Records);
        Assert.Equal("1001", record.EmployeeCode);
        Assert.Equal("Nguyễn Văn A", record.EmployeeName);
        Assert.Equal("Ép", record.Department);
        Assert.Null(record.Note);
    }

    [Fact]
    public async Task Evaluates_formula_cells()
    {
        var path = _workbooks.Create(
            TestWorkbook.StandardHeaders,
            [[Sep28, "NV001", "A", "Ép", null, null, null]],
            customize: sheet =>
            {
                sheet.Cell(2, 5).FormulaA1 = "1000+250";
                sheet.Cell(2, 6).FormulaA1 = "E2-50";
            });

        var result = await _reader.ReadAsync(path);

        var record = Assert.Single(result.Records);
        Assert.Equal(1250m, record.Quantity);
        Assert.Equal(1200m, record.Target);
    }

    [Fact]
    public async Task Reads_file_while_another_process_holds_it_open_for_writing()
    {
        var path = _workbooks.CreateStandard([Sep28, "NV001", "A", "Ép", 100, null, null]);

        // Excel keeps the workbook open with shared read access while it is being edited.
        await using var excelHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var result = await _reader.ReadAsync(path);

        Assert.Single(result.Records);
    }

    [Fact]
    public async Task Honours_cancellation()
    {
        var path = _workbooks.CreateStandard([Sep28, "NV001", "A", "Ép", 100, null, null]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _reader.ReadAsync(path, cts.Token));
    }

    [Fact]
    public void Read_from_stream_does_not_require_a_file()
    {
        var path = _workbooks.CreateStandard([Sep28, "NV001", "A", "Ép", 100, null, null]);
        using var stream = new MemoryStream(File.ReadAllBytes(path));

        var result = _reader.Read(stream);

        Assert.Single(result.Records);
    }
}
