using DisplayBoard.Core.Models;
using DisplayBoard.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisplayBoard.Tests;

public class ExcelDataReaderTests
{
    private readonly ExcelDataReader _reader = new(NullLogger<ExcelDataReader>.Instance);

    [Fact]
    public async Task Reads_valid_rows()
    {
        using var file = TestWorkbook.Data(
            [new DateTime(2026, 9, 28), "NV001", "Nguyễn Văn A", "Ép", 1250, 1200, null],
            [new DateTime(2026, 9, 28), "NV002", "Trần Văn B", "Sơn", 980, null, "Máy hỏng"]);

        var result = await _reader.ReadAsync(file.Path);

        Assert.Empty(result.Warnings);
        Assert.Equal(
            [
                new ProductionRecord(new DateOnly(2026, 9, 28), "NV001", "Nguyễn Văn A", "Ép", 1250, 1200, null),
                new ProductionRecord(new DateOnly(2026, 9, 28), "NV002", "Trần Văn B", "Sơn", 980, null, "Máy hỏng"),
            ],
            result.Records);
    }

    [Fact]
    public async Task Reads_the_sample_workbook()
    {
        var result = await _reader.ReadAsync(Path.Combine(AppContext.BaseDirectory, "samples", "SanLuong.xlsx"));

        Assert.Empty(result.Warnings);
        Assert.Equal(8, result.Records.Count);
        Assert.All(result.Records, r => Assert.Equal(new DateOnly(2026, 9, 28), r.Date));
    }

    [Fact]
    public async Task Matches_headers_ignoring_case_whitespace_and_order()
    {
        using var file = new TestWorkbook(
            " data ",
            ["  SẢN LƯỢNG ", "mã nv", "Họ  tên", "BỘ PHẬN", "ngày"],
            [10, "NV1", "A", "Ép", new DateTime(2026, 1, 2)]);

        var result = await _reader.ReadAsync(file.Path);

        var record = Assert.Single(result.Records);
        Assert.Equal(new ProductionRecord(new DateOnly(2026, 1, 2), "NV1", "A", "Ép", 10, null, null), record);
    }

    [Fact]
    public async Task Matches_headers_typed_with_decomposed_accents()
    {
        var decomposed = TestWorkbook.StandardHeader
            .Select(h => ((string)h!).Normalize(System.Text.NormalizationForm.FormD))
            .ToArray<object?>();
        using var file = new TestWorkbook("DATA", decomposed, [new DateTime(2026, 1, 2), "NV1", "A", "Ép", 10, 20, null]);

        var result = await _reader.ReadAsync(file.Path);

        Assert.Single(result.Records);
    }

    [Theory]
    [InlineData("28/09/2026")]
    [InlineData("2026-09-28")]
    [InlineData("28-9-2026")]
    public async Task Parses_dates_stored_as_text(string text)
    {
        using var file = TestWorkbook.Data([text, "NV1", "A", "Ép", 10, null, null]);

        var result = await _reader.ReadAsync(file.Path);

        Assert.Equal(new DateOnly(2026, 9, 28), Assert.Single(result.Records).Date);
    }

    [Theory]
    [InlineData("1250", 1250)]
    [InlineData("12.5", 12.5)]
    [InlineData("12,5", 12.5)]
    public async Task Parses_numbers_stored_as_text(string text, double expected)
    {
        using var file = TestWorkbook.Data([new DateTime(2026, 1, 1), "NV1", "A", "Ép", text, null, null]);

        var result = await _reader.ReadAsync(file.Path);

        Assert.Equal((decimal)expected, Assert.Single(result.Records).Quantity);
    }

    [Fact]
    public async Task Reads_numeric_employee_codes_as_text()
    {
        using var file = TestWorkbook.Data([new DateTime(2026, 1, 1), 1001, "A", "Ép", 5, null, null]);

        var result = await _reader.ReadAsync(file.Path);

        Assert.Equal("1001", Assert.Single(result.Records).EmployeeCode);
    }

    [Fact]
    public async Task Skips_invalid_rows_with_row_number_and_keeps_the_rest()
    {
        using var file = TestWorkbook.Data(
            [new DateTime(2026, 1, 1), "NV1", "A", "Ép", 10, null, null],
            ["không phải ngày", "NV2", "B", "Ép", "abc", null, null],
            [new DateTime(2026, 1, 1), null, "C", "Ép", -5, null, null],
            [new DateTime(2026, 1, 1), "NV4", "D", "Sơn", 30, null, null]);

        var result = await _reader.ReadAsync(file.Path);

        Assert.Equal(["NV1", "NV4"], result.Records.Select(r => r.EmployeeCode));
        Assert.Collection(
            result.Warnings,
            w =>
            {
                Assert.Equal(3, w.RowNumber);
                Assert.Contains("Ngày không hợp lệ", w.Message);
                Assert.Contains("Sản lượng không phải số", w.Message);
            },
            w =>
            {
                Assert.Equal(4, w.RowNumber);
                Assert.Contains("thiếu Mã NV", w.Message);
                Assert.Contains("Sản lượng không được âm", w.Message);
            });
    }

    [Fact]
    public async Task Ignores_blank_rows()
    {
        using var file = TestWorkbook.Data(
            [new DateTime(2026, 1, 1), "NV1", "A", "Ép", 10, null, null],
            [null, null, null, null, null, null, null],
            [new DateTime(2026, 1, 1), "NV2", "B", "Ép", 20, null, null]);

        var result = await _reader.ReadAsync(file.Path);

        Assert.Equal(2, result.Records.Count);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Missing_file_is_reported()
    {
        var ex = await Assert.ThrowsAsync<ExcelValidationException>(
            () => _reader.ReadAsync(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx")));

        Assert.Equal(ExcelValidationError.FileNotFound, ex.Error);
    }

    [Fact]
    public async Task Non_workbook_file_is_reported()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "not a workbook");

            var ex = await Assert.ThrowsAsync<ExcelValidationException>(() => _reader.ReadAsync(path));

            Assert.Equal(ExcelValidationError.InvalidWorkbook, ex.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Missing_DATA_sheet_is_reported()
    {
        using var file = new TestWorkbook("Sheet1", TestWorkbook.StandardHeader);

        var ex = await Assert.ThrowsAsync<ExcelValidationException>(() => _reader.ReadAsync(file.Path));

        Assert.Equal(ExcelValidationError.MissingDataSheet, ex.Error);
    }

    [Fact]
    public async Task Missing_required_columns_are_listed()
    {
        using var file = new TestWorkbook("DATA", ["Ngày", "Họ tên", "Mục tiêu"]);

        var ex = await Assert.ThrowsAsync<ExcelValidationException>(() => _reader.ReadAsync(file.Path));

        Assert.Equal(ExcelValidationError.MissingColumns, ex.Error);
        Assert.Equal(["Mã NV", "Bộ phận", "Sản lượng"], ex.MissingColumns);
    }

    [Fact]
    public async Task Reads_while_another_process_holds_the_file_open_for_writing()
    {
        using var file = TestWorkbook.Data([new DateTime(2026, 1, 1), "NV1", "A", "Ép", 10, null, null]);

        // Excel keeps the workbook open with write access and shares read access.
        await using var held = new FileStream(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var result = await _reader.ReadAsync(file.Path);

        Assert.Single(result.Records);
    }
}
