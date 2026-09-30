using DisplayBoard.Core.Excel;

namespace DisplayBoard.Tests;

public class ExcelWorkbookReaderTests
{
    private static readonly DateTime Day = new(2026, 9, 28);

    [Fact]
    public void Reads_required_and_optional_columns()
    {
        using var stream = WorkbookFactory.Create(wb => wb.AddTable("DATA", WorkbookFactory.DataHeaders,
            [Day, TimeSpan.FromHours(8), "A", "NV001", "Nguyễn Văn A", "Ép", 150, 1200, "ghi chú"]));

        var data = ExcelWorkbookReader.Read(stream);

        var record = Assert.Single(data.Records);
        Assert.Equal(new DateOnly(2026, 9, 28), record.Date);
        Assert.Equal(new TimeOnly(8, 0), record.Time);
        Assert.Equal("A", record.Shift);
        Assert.Equal("NV001", record.EmployeeCode);
        Assert.Equal(150m, record.Quantity);
        Assert.Equal(1200m, record.Target);
        Assert.Equal("ghi chú", record.Note);
        Assert.Empty(data.Warnings);
    }

    [Fact]
    public void Headers_are_case_and_accent_insensitive()
    {
        using var stream = WorkbookFactory.Create(wb => wb.AddTable("data",
            ["  NGÀY ", "ma nv", "Họ Tên", "BỘ PHẬN", "sản lượng"],
            [Day, "NV001", "A", "Ép", 10]));

        Assert.Single(ExcelWorkbookReader.Read(stream).Records);
    }

    [Fact]
    public void Text_cells_are_parsed()
    {
        using var stream = WorkbookFactory.Create(wb => wb.AddTable("DATA", WorkbookFactory.DataHeaders,
            ["28/09/2026", "8:30", "B", "NV001", "A", "Ép", "1.250", "1200", null]));

        var record = Assert.Single(ExcelWorkbookReader.Read(stream).Records);
        Assert.Equal(new DateOnly(2026, 9, 28), record.Date);
        Assert.Equal(new TimeOnly(8, 30), record.Time);
        Assert.Equal(1250m, record.Quantity);
    }

    [Fact]
    public void Missing_data_sheet_throws_clear_error()
    {
        using var stream = WorkbookFactory.Create(wb => wb.AddWorksheet("Sheet1"));

        var ex = Assert.Throws<ExcelValidationException>(() => ExcelWorkbookReader.Read(stream));
        Assert.Contains("DATA", ex.Message);
    }

    [Fact]
    public void Missing_required_columns_are_listed()
    {
        using var stream = WorkbookFactory.Create(wb => wb.AddTable("DATA", ["Ngày", "Mã NV", "Họ tên"]));

        var ex = Assert.Throws<ExcelValidationException>(() => ExcelWorkbookReader.Read(stream));
        Assert.Contains("Bộ phận", ex.Message);
        Assert.Contains("Sản lượng", ex.Message);
    }

    [Fact]
    public void Bad_row_is_skipped_with_row_number_warning()
    {
        using var stream = WorkbookFactory.Create(wb => wb.AddTable("DATA", WorkbookFactory.DataHeaders,
            [Day, null, null, "NV001", "A", "Ép", 100, null, null],
            [Day, null, null, "NV002", "B", "Ép", "abc", null, null],
            [null, null, null, null, null, null, null, null, null],
            [Day, "không phải giờ", null, "NV003", "C", "Ép", 50, null, null]));

        var data = ExcelWorkbookReader.Read(stream);

        Assert.Equal(["NV001", "NV003"], data.Records.Select(r => r.EmployeeCode));
        Assert.Contains(data.Warnings, w => w.Contains("dòng 3") && w.Contains("Sản lượng"));
        Assert.Contains(data.Warnings, w => w.Contains("dòng 5") && w.Contains("Giờ"));
        Assert.Null(data.Records[1].Time);
    }

    [Fact]
    public void Reads_optional_sheets()
    {
        using var stream = WorkbookFactory.Create(wb =>
        {
            wb.AddTable("DATA", WorkbookFactory.DataHeaders, [Day, null, null, "NV001", "A", "Ép", 1, null, null]);
            wb.AddTable("DANH_MUC", ["Mã NV", "Họ tên", "Bộ phận", "Trạng thái", "Ảnh"], ["NV001", "A", "Ép", "Đang làm", "a.png"]);
            wb.AddTable("BO_PHAN", ["Bộ phận", "Mục tiêu", "Màu", "Icon", "Thứ tự"], ["Ép", 5000, "#123456", "factory", 2]);
            wb.AddTable("THONG_BAO", ["Tiêu đề", "Nội dung", "Ảnh nền", "Từ ngày", "Đến ngày", "Thứ tự", "Bật"],
                ["T", "Nội dung", "bg.jpg", "01/09/2026", Day, 1, "x"],
                ["T2", "Tắt", null, null, null, 2, null]);
            wb.AddTable("KHAU_HIEU", ["Icon", "Dòng 1", "Dòng 2"], ["trophy", "AN TOÀN", "LÀ SỐ 1"]);
            wb.AddTable("CAU_HINH", ["Khóa", "Giá trị"], ["GioBatDau", TimeSpan.FromHours(6)], ["GioKetThuc", "18:00"]);
        });

        var data = ExcelWorkbookReader.Read(stream);

        Assert.Equal("a.png", Assert.Single(data.Employees).PhotoFile);
        var dept = Assert.Single(data.Departments);
        Assert.Equal(5000m, dept.Target);
        Assert.Equal(2, dept.Order);
        Assert.Equal(2, data.Notices.Count);
        Assert.True(data.Notices[0].Enabled);
        Assert.Equal(new DateOnly(2026, 9, 1), data.Notices[0].FromDate);
        Assert.False(data.Notices[1].Enabled);
        Assert.Equal("LÀ SỐ 1", Assert.Single(data.Slogans).Line2);
        Assert.Equal("06:00", data.Settings["giobatdau"]);
        Assert.Equal("18:00", data.Settings["gioketthuc"]);
    }

    [Fact]
    public void Sample_generator_layout_is_readable()
    {
        var sample = Path.Combine(FindRepoRoot(), "samples", "SanLuong-mau.xlsx");
        using var stream = File.OpenRead(sample);

        var data = ExcelWorkbookReader.Read(stream);

        Assert.Equal(120, data.Records.Count);
        Assert.Empty(data.Warnings);
        Assert.Equal(4, data.Departments.Count);
        Assert.Equal(4, data.Slogans.Count);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DisplayBoard.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy thư mục gốc repo");
    }
}
