using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;

namespace DisplayBoard.Tests;

public class ProductProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 42, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 30)));
    private static int _row = 2;

    private static ProductRecord P(string code, decimal target, decimal actual, decimal? monthTarget = null, decimal? monthCum = null) =>
        new(++_row, code, 10, target / 10, target, actual, monthTarget, monthCum);

    private static ProductionSheet Sheet(params ProductRecord[] records) =>
        new("Sheet2", new DateOnly(2026, 9, 30), records, []);

    [Fact]
    public void Customer_numbers_match_excel()
    {
        var snapshot = new ProductProcessor().Build(Sheet(
            P("ĐAI LƯNG", 1000, 1000, 40000, 38000),
            P("BAO TAY XANH", 1000, 1000, 40000, 39000),
            P("883", 1850, 1850, 50000, 45000),
            P("700", 1850, 1850, 50000, 45000),
            P("972", 1850, 2000, 50000, 55000),
            P("959", 1850, 1850, 50000, 45000)), ContentData.Empty, Now, null);

        var p972 = snapshot.Products.Single(p => p.ProductCode == "972");
        Assert.Equal(108m, p972.DailyRate);            // Excel hiện 108%
        Assert.Equal(150m, p972.DailyVariance);
        Assert.Equal(110m, p972.MonthRate);            // 110%
        Assert.Equal(5000m, p972.MonthVariance);
        Assert.Equal(ProgressStatus.Met, p972.MonthStatus);
        Assert.Equal(1, p972.Rank);

        var bao = snapshot.Products.Single(p => p.ProductCode == "BAO TAY XANH");
        Assert.Equal(98m, bao.MonthRate);              // 97.5% làm tròn như Excel "0%"
        Assert.Equal(ProgressStatus.Near, bao.MonthStatus);
        Assert.Equal(-1000m, bao.MonthVariance);

        var s = snapshot.Summary;
        Assert.Equal(9400m, s.DailyTarget);
        Assert.Equal(9550m, s.DailyActual);
        Assert.Equal(102m, s.DailyRate);
        Assert.Equal(280000m, s.MonthTarget);
        Assert.Equal(267000m, s.MonthCumulative);
        Assert.Equal(95m, s.MonthRate);
        Assert.Equal(-13000m, s.MonthVariance);
        Assert.Equal(6, s.MetCount);
        Assert.Equal(0, s.NotMetCount);
        Assert.True(s.IsToday);
        Assert.Equal("PCS", snapshot.Unit);
    }

    [Fact]
    public void Status_thresholds()
    {
        Assert.Equal(ProgressStatus.Met, ProductProcessor.Status(100));
        Assert.Equal(ProgressStatus.Near, ProductProcessor.Status(90));
        Assert.Equal(ProgressStatus.Behind, ProductProcessor.Status(89));
        Assert.Equal(ProgressStatus.None, ProductProcessor.Status(null));
    }

    [Fact]
    public void Ranks_by_daily_rate_then_actual_then_file_order()
    {
        var snapshot = new ProductProcessor().Build(Sheet(
            P("A", 1000, 900),
            P("B", 2000, 1800),
            P("C", 1000, 1100),
            P("D", 0, 500)), ContentData.Empty, Now, null);

        Assert.Equal(["A", "B", "C", "D"], snapshot.Products.Select(p => p.ProductCode)); // giữ thứ tự file
        Assert.Equal([3, 2, 1, 4], snapshot.Products.Select(p => p.Rank));
        Assert.Equal(ProgressStatus.None, snapshot.Products[3].DailyStatus);
        Assert.Equal(2, snapshot.Summary.NotMetCount);
    }

    [Fact]
    public void Duplicate_codes_are_summed_with_warning()
    {
        var snapshot = new ProductProcessor().Build(Sheet(P("A", 100, 50), P("A", 100, 70)), ContentData.Empty, Now, null);

        var a = Assert.Single(snapshot.Products);
        Assert.Equal(200m, a.DailyTarget);
        Assert.Equal(120m, a.DailyActual);
        Assert.Contains(snapshot.Warnings, w => w.Contains("A") && w.Contains("2 lần"));
    }

    [Fact]
    public void Content_sets_names_colors_order_notices_and_unit()
    {
        var content = new ContentData(
            [new NoticeRow("T", "hiện", null, null, null, 1, true), new NoticeRow("T", "tắt", null, null, null, 2, false)],
            [new Slogan("trophy", "AN TOÀN", "LÀ SỐ 1")],
            [new ProductInfo("883", "Găng tay 883", "#123456", null, 1)],
            new Dictionary<string, string> { ["donvi"] = "đôi", ["tencongty"] = "MAOYUAN" },
            []);

        var snapshot = new ProductProcessor().Build(Sheet(P("ĐAI LƯNG", 1, 1), P("883", 1, 1)), content, Now, null);

        Assert.Equal("883", snapshot.Products[0].ProductCode);
        Assert.Equal("Găng tay 883", snapshot.Products[0].DisplayName);
        Assert.Equal("#123456", snapshot.Products[0].Color);
        Assert.Equal("ĐAI LƯNG", snapshot.Products[1].DisplayName);
        Assert.Equal(["hiện"], snapshot.Notices.Select(n => n.Content));
        Assert.Equal("đôi", snapshot.Unit);
        Assert.Equal("MAOYUAN", snapshot.CompanyName);
    }

    [Fact]
    public void Sheet_date_other_than_today_is_flagged()
    {
        var sheet = new ProductionSheet("S", new DateOnly(2026, 9, 29), [P("A", 1, 1)], []);

        var snapshot = new ProductProcessor().Build(sheet, ContentData.Empty, Now, null);

        Assert.Equal(new DateOnly(2026, 9, 29), snapshot.Summary.Date);
        Assert.False(snapshot.Summary.IsToday);
    }

    [Fact]
    public void Product_without_month_data_is_excluded_from_month_totals()
    {
        var snapshot = new ProductProcessor().Build(Sheet(P("A", 100, 100, 1000, 500), P("B", 100, 100)), ContentData.Empty, Now, null);

        Assert.Equal(1000m, snapshot.Summary.MonthTarget);
        Assert.Equal(500m, snapshot.Summary.MonthCumulative);
        Assert.Null(snapshot.Products[1].MonthRate);
    }

    [Fact]
    public void Resolves_product_image_by_code()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "883.png"), [1]);
            var snapshot = new ProductProcessor().Build(Sheet(P("883", 1, 1), P("700", 1, 1)), ContentData.Empty, Now, folder.FullName);

            Assert.EndsWith("883.png", snapshot.Products[0].ImagePath);
            Assert.Null(snapshot.Products[1].ImagePath);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void Generated_samples_build_snapshot()
    {
        var root = TestPaths.RepoRoot();
        using var data = File.OpenRead(Path.Combine(root, "samples", "SanLuong-khach-mau.xlsx"));
        using var content = File.OpenRead(Path.Combine(root, "samples", "display-content.xlsx"));

        var snapshot = new ProductProcessor().Build(ProductionWorkbookReader.Read(data), ContentWorkbookReader.Read(content), Now, null);

        Assert.Equal(24, snapshot.Products.Count);
        Assert.Equal(6, snapshot.Lines.Count);
        Assert.Equal(2, snapshot.Notices.Count);
        Assert.Equal(4, snapshot.Slogans.Count);
        Assert.Contains(snapshot.Products, p => p.DisplayName == "Găng tay 883");
        Assert.Contains(snapshot.Products, p => p.DailyStatus == ProgressStatus.Behind);
        Assert.Empty(snapshot.Warnings);
    }
}
