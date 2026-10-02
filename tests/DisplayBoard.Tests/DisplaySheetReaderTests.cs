using DisplayBoard.Core.Display;
using ClosedXML.Excel;
using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;

namespace DisplayBoard.Tests;

public class DisplaySheetReaderTests
{
    private static DisplaySheet ReadSample()
    {
        using var stream = File.OpenRead(Path.Combine(TestPaths.RepoRoot(), "samples", "Theo_doi_san_luong_V18_mau.xlsx"));
        return DisplaySheetReader.Read(stream);
    }

    [Fact]
    public void Reads_values_excel_calculated_on_hien_thi()
    {
        var sheet = ReadSample();

        Assert.Equal("HIEN_THI", sheet.SheetName);
        Assert.Equal(new DateOnly(2026, 9, 30), sheet.Date);
        Assert.Equal(["Chuyền 1", "Chuyền 2", "Chuyền 3", "Chuyền 4", "Chuyền 5", "Chuyền 6"], sheet.Lines.Select(l => l.Line));

        var line1 = sheet.Lines[0];
        Assert.Equal("ĐAI LƯNG", line1.ProductCode);
        Assert.Equal("8H", line1.ShiftCode);
        Assert.Equal(8m, line1.ShiftHours);
        Assert.Equal(800m, line1.DailyTarget);
        Assert.Equal(300m, line1.DailyActual);
        Assert.Equal(37.5m, line1.DailyRate);          // Excel lưu 0.375 (định dạng %)
        Assert.Equal(-500m, line1.DailyVariance);
        Assert.Equal(500m, line1.Remaining);
        Assert.Equal(3m, line1.HoursEntered);
        Assert.Equal(100m, line1.HourlyProgress);
        Assert.Equal(new DateOnly(2026, 9, 29), line1.PreviousDay);
        Assert.Equal(40m, line1.CarriedShortfall);
        Assert.Equal(40000m, line1.MonthTarget);
        Assert.Equal(1060m, line1.MonthCumulative);
        Assert.Equal(38940m, line1.MonthRemaining);
        Assert.Equal("Đang chạy", line1.Status);
        Assert.Equal(12, line1.Hourly.Count);
        Assert.Equal([100m, 90m, 110m, null], line1.Hourly.Take(4));

        Assert.Equal("883", sheet.Lines[2].ProductCode);   // mã số lưu dạng chữ
        Assert.Null(sheet.Lines[3].DailyActual);            // chuyền 4 chưa nhập

        var total = Assert.IsType<LineRecord>(sheet.Total);
        Assert.Equal(5308m, total.DailyTarget);
        Assert.Equal(1425m, total.DailyActual);
        Assert.Equal(40m, total.CarriedShortfall);
        Assert.Equal(6810m, total.LineMonthCumulative);
        Assert.Empty(sheet.Warnings);

        // V18 chưa có các cột của V19.
        Assert.Null(line1.NeededPerDay);
        Assert.Null(line1.WorkingDaysLeft);
        Assert.Null(line1.PreviousMonthShortfall);
        Assert.Null(line1.Defects);
    }

    [Fact]
    public void Reads_v19_needed_per_day_days_left_and_previous_month_shortfall()
    {
        using var stream = File.OpenRead(Path.Combine(TestPaths.RepoRoot(), "samples", "Theo_doi_san_luong_V19_mau.xlsx"));
        var sheet = DisplaySheetReader.Read(stream);

        var line1 = sheet.Lines[0];
        Assert.Equal("ĐAI LƯNG", line1.ProductCode);
        Assert.Equal(42000m, line1.MonthTarget);
        Assert.Equal(26m, line1.WorkingDaysLeft);
        Assert.Equal(1585m, line1.NeededPerDay);           // ROUNDUP((42.000 − 800) / 26)
        Assert.Equal(38940m, line1.PreviousMonthShortfall);
        Assert.Null(sheet.Lines[2].NeededPerDay);          // chuyền 3 chưa có kế hoạch
        Assert.Empty(sheet.Warnings);

        var snapshot = new SnapshotBuilder().Build(sheet, ContentData.Empty, DateTimeOffset.Now, null);
        Assert.Equal(26m, snapshot.Summary.WorkingDaysLeft);
        Assert.Equal(1504m, snapshot.Products[1].NeededPerDay);
    }

    [Fact]
    public void Reads_v19_defects_per_line_and_total()
    {
        using var stream = File.OpenRead(Path.Combine(TestPaths.RepoRoot(), "samples", "Theo_doi_san_luong_V19_mau.xlsx"));
        var sheet = DisplaySheetReader.Read(stream);

        Assert.Null(sheet.Lines[0].Defects);                // chuyền 1 chưa nhập số lỗi
        Assert.Equal(1m, sheet.Lines[1].Defects);
        Assert.Equal(0.4167m, Math.Round(sheet.Lines[1].DefectRate!.Value, 4)); // 1 / 240, Excel tính
        Assert.Equal(1m, sheet.Total!.Defects);

        var snapshot = new SnapshotBuilder().Build(sheet, ContentData.Empty, DateTimeOffset.Now, null);
        Assert.True(snapshot.HasDefectData);
        Assert.Equal(ProgressStatus.Met, snapshot.Products[1].DefectStatus);
        Assert.Equal(1m, snapshot.Summary.Defects);
        Assert.Equal(ProgressStatus.None, snapshot.Products[0].DefectStatus);
    }

    [Fact]
    public void Reads_v20_defect_photos_of_the_shown_day()
    {
        var samples = Path.Combine(TestPaths.RepoRoot(), "samples");
        using var stream = File.OpenRead(Path.Combine(samples, "Theo_doi_san_luong_V20_mau.xlsx"));
        var sheet = DisplaySheetReader.Read(stream);

        Assert.Equal(6, sheet.DefectLog.Count);                     // HANG_LOI: các dòng HIỆN TRÊN TV = CÓ
        var first = sheet.DefectLog[0];
        Assert.Equal("Chuyền 3", first.Line);
        Assert.Equal("883", first.ProductCode);                    // Excel tự lấy từ NHAP_LIEU
        Assert.Equal("Bung chỉ", first.DefectType);
        Assert.Equal(4m, first.Quantity);
        Assert.Equal(new TimeOnly(8, 50), first.Time);
        Assert.Equal("bung_chi_c3.png", first.ImageFile);
        Assert.Equal(85m, sheet.Lines[1].Defects);                  // SỐ LỖI = tổng HANG_LOI của chuyền 2
        Assert.Equal(125m, sheet.Total!.Defects);

        var snapshot = new SnapshotBuilder().Build(sheet, ContentData.Empty, DateTimeOffset.Now, Path.Combine(samples, "images"));
        Assert.Equal(5, snapshot.DefectPhotos.Count);               // dòng không có tên file ảnh thì không chiếu
        Assert.Equal("Lem màu", snapshot.DefectPhotos[0].DefectType); // 13:40, mới nhất lên trước
        Assert.All(snapshot.DefectPhotos, p => Assert.True(File.Exists(p.ImagePath)));
        Assert.Equal(1, DefectsViewDefinition.PageCount(snapshot, 6));
        Assert.Equal(2, DefectsViewDefinition.PageCount(snapshot, 4));
        Assert.Equal(3, DefectsViewDefinition.PageCount(snapshot, 2));
    }

    [Fact]
    public void Missing_defect_photo_is_reported_but_still_listed()
    {
        var samples = Path.Combine(TestPaths.RepoRoot(), "samples");
        using var stream = File.OpenRead(Path.Combine(samples, "Theo_doi_san_luong_V20_mau.xlsx"));
        var sheet = DisplaySheetReader.Read(stream);

        var snapshot = new SnapshotBuilder().Build(sheet, ContentData.Empty, DateTimeOffset.Now, Path.Combine(samples, "khong_co"));
        Assert.Equal(5, snapshot.DefectPhotos.Count);
        Assert.All(snapshot.DefectPhotos, p => Assert.Null(p.ImagePath));
        Assert.Contains(snapshot.Warnings, w => w.Contains("rach_c2.png"));
    }

    [Fact]
    public void File_never_calculated_by_excel_gives_clear_error()
    {
        // Giống file V18 vừa tạo: có công thức nhưng chưa có giá trị Excel đã tính.
        using var stream = WorkbookFactory.Create(wb =>
        {
            var ws = wb.AddWorksheet("HIEN_THI");
            ws.Cell("B3").Value = "CHUYỀN";
            ws.Cell("G3").Value = "MỤC TIÊU TRONG NGÀY";
            ws.Cell("H3").Value = "THỰC TẾ TRONG NGÀY";
            ws.Cell("B5").FormulaA1 = "IF(X1=\"\",\"\",X1)";
            ws.Cell("B7").Value = "TỔNG CỘNG";
        });

        var ex = Assert.Throws<ExcelValidationException>(() => DisplaySheetReader.Read(stream));
        Assert.Contains("Lưu", ex.Message);
    }

    [Fact]
    public void Missing_required_column_is_reported()
    {
        using var stream = WorkbookFactory.Create(wb =>
        {
            var ws = wb.AddWorksheet("HIEN_THI");
            ws.Cell("B3").Value = "CHUYỀN";
            ws.Cell("G3").Value = "MỤC TIÊU TRONG NGÀY";
        });

        var ex = Assert.Throws<ExcelValidationException>(() => DisplaySheetReader.Read(stream));
        Assert.Contains("THỰC TẾ TRONG NGÀY", ex.Message);
    }

    [Fact]
    public void Missing_sheet_is_reported()
    {
        using var stream = WorkbookFactory.Create(wb => wb.AddWorksheet("Sheet1"));

        var ex = Assert.Throws<ExcelValidationException>(() => DisplaySheetReader.Read(stream));
        Assert.Contains("HIEN_THI", ex.Message);
    }

    [Fact]
    public void Snapshot_uses_excel_numbers_without_recalculating()
    {
        ContentData content;
        using (var stream = File.OpenRead(Path.Combine(TestPaths.RepoRoot(), "samples", "display-content.xlsx")))
            content = ContentWorkbookReader.Read(stream);
        var now = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 30)));

        var snapshot = new SnapshotBuilder().Build(ReadSample(), content, now, null);

        Assert.True(snapshot.Summary.IsToday);
        Assert.Equal(1425m, snapshot.Summary.DailyActual);
        Assert.Equal(5308m, snapshot.Summary.DailyTarget);
        Assert.Equal(40m, snapshot.Summary.CarriedShortfall);
        Assert.Equal(new DateOnly(2026, 9, 29), snapshot.Summary.CarriedFromDate);
        Assert.Equal(1, snapshot.Summary.CarriedProductCount);
        Assert.True(snapshot.HasMultipleLines);
        Assert.True(snapshot.HasHourlyData);

        var line6 = snapshot.Products.Single(p => p.Line == "Chuyền 6");
        Assert.Equal(1, line6.Rank);                        // 50% là cao nhất
        Assert.Equal(ProgressStatus.Behind, line6.DailyStatus);
        Assert.Equal(ProgressStatus.Met, line6.HourlyStatus); // đúng tiến độ theo giờ (100%)
        Assert.Contains(snapshot.Products, p => p.DisplayName == "Găng tay 883");  // tên từ display-content.xlsx
        Assert.False(snapshot.Products.Single(p => p.Line == "Chuyền 4").HasActual);
    }
}
