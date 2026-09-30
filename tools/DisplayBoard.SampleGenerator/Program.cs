using ClosedXML.Excel;

// Tạo file mẫu theo đúng bố cục file sản lượng của khách (theo sản phẩm, header song ngữ, ngày trong ô chữ A1)
// và file nội dung phụ display-content.xlsx.
// Cách dùng: dotnet run --project tools/DisplayBoard.SampleGenerator -- [thư mục xuất] [ngày yyyy/MM/dd]
var folder = args.Length > 0 ? args[0] : "samples";
var date = args.Length > 1 ? DateTime.ParseExact(args[1], "yyyy/MM/dd", null) : DateTime.Today;
Directory.CreateDirectory(folder);

// Khách có 6 chuyền, mỗi chuyền một sheet cùng ngày.
var lines = new (string Name, (object Code, double Hours, double Hourly, double Actual, double MonthTarget, double MonthCumulative)[] Products)[]
{
    ("Chuyền 1", [("ĐAI LƯNG", 10, 100, 1000, 40000, 38000), ("BAO TAY XANH", 10, 100, 1000, 40000, 39000), (883, 10, 185, 1850, 50000, 45000)]),
    ("Chuyền 2", [(700, 10, 185, 1850, 50000, 45000), (972, 10, 185, 2000, 50000, 55000), (959, 10, 185, 1850, 50000, 45000)]),
    ("Chuyền 3", [(1201, 8, 150, 1020, 30000, 24500), (1305, 8, 150, 1150, 30000, 28800), (1306, 8, 150, 1180, 30000, 29900), (1310, 8, 150, 1090, 30000, 27100)]),
    ("Chuyền 4", [("GĂNG TAY TRẮNG", 10, 120, 1310, 36000, 36500), (640, 10, 200, 1700, 60000, 51000), (641, 10, 200, 2050, 60000, 61200)]),
    ("Chuyền 5", [(515, 9, 160, 1500, 42000, 40100), (516, 9, 160, 1390, 42000, 37800), (520, 9, 160, 1440, 42000, 41500), (883, 9, 160, 1460, 42000, 40900)]),
    ("Chuyền 6", [(730, 10, 180, 1810, 52000, 50400), (731, 10, 180, 1620, 52000, 46800), (735, 10, 180, 1790, 52000, 51900), (740, 10, 180, 1400, 52000, 43000), (745, 10, 180, 1850, 52000, 52800), (750, 10, 180, 1705, 52000, 49600), (755, 10, 180, 1830, 52000, 50800)]),
};

using (var workbook = new XLWorkbook())
{
    foreach (var line in lines)
        WriteSheet(workbook.AddWorksheet(line.Name), date, line.Products);
    // Sheet ngày cũ còn sót lại: app phải bỏ qua vì khác ngày.
    WriteSheet(workbook.AddWorksheet("Cũ"), date.AddDays(-1), lines[0].Products);
    workbook.SaveAs(Path.Combine(folder, "SanLuong-khach-mau.xlsx"));
}

using (var content = new XLWorkbook())
{
    AddTable(content, "THONG_BAO", ["Tiêu đề", "Nội dung", "Ảnh nền", "Từ ngày", "Đến ngày", "Thứ tự", "Bật"],
        ["THÔNG BÁO / THÔNG ĐIỆP", "“NĂNG SUẤT HÔM NAY TẠO NÊN THÀNH CÔNG NGÀY MAI”", "factory.jpg", null, null, 1, "x"],
        ["AN TOÀN LAO ĐỘNG", "Đeo đầy đủ bảo hộ trước khi vào xưởng", null, null, null, 2, "x"],
        ["THÔNG BÁO CŨ", "Dòng này đang tắt nên không hiển thị", null, null, null, 3, ""]);
    AddTable(content, "KHAU_HIEU", ["Icon", "Dòng 1", "Dòng 2"],
        ["trophy", "AN TOÀN", "LÀ SỐ 1"], ["people", "LÀM VIỆC", "ĐOÀN KẾT"],
        ["chart", "NÂNG CAO", "NĂNG SUẤT"], ["gear", "CHẤT LƯỢNG", "ỔN ĐỊNH"]);
    AddTable(content, "SAN_PHAM", ["Mã sản phẩm", "Tên hiển thị", "Màu", "Ảnh", "Thứ tự"],
        ["883", "Găng tay 883", null, null, null],
        ["ĐAI LƯNG", null, "#1565C0", null, null]);
    AddTable(content, "CAU_HINH", ["Khóa", "Giá trị", "Ghi chú"],
        ["DonVi", "PCS", "Đơn vị hiển thị"],
        ["TenCongTy", "MAOYUAN", "Hiện khi không có thông báo"]);
    content.SaveAs(Path.Combine(folder, "display-content.xlsx"));
}

Console.WriteLine($"Đã tạo {folder}/SanLuong-khach-mau.xlsx ({lines.Length} chuyền, {lines.Sum(l => l.Products.Length)} sản phẩm, ngày {date:yyyy/MM/dd}) và {folder}/display-content.xlsx");

static void WriteSheet(IXLWorksheet sheet, DateTime date, (object Code, double Hours, double Hourly, double Actual, double MonthTarget, double MonthCumulative)[] rows)
{
    sheet.Range("A1:B1").Merge();
    sheet.Cell("A1").Value = $"日期：{date:yyyy/MM/dd}\nNGÀY THÁNG";
    string[] headers =
    [
        "产品代码\nMÃ SẢN PHẨM",
        "每日工时数\nTHỜI GIAN LÊN CA",
        "每小时目标产量PCS\nSẢN LƯỢNG MỤC TIÊU MỖI GIỜ",
        "每日目标产量PCS\nMỤC TIỆU TRONG NGÀY",
        "每日实际产量PCS\nSẢN LƯỢNG THỰC TẾ TRONG NGÀY",
        "当日达成率%\nTỶ LỆ ĐẠT TRONG NGÀY",
        "当月总目标产量PCS\nTỔNG SẢN LƯỢNG TRONG THÁNG",
        "当月累计产能PCS\n LŨY KẾ SẢN LƯỢNG TRONG THÁNG",
        "当月累计差异PCS\nLŨY KẾ CHÊNH LỆCH TRONG THÁNG",
        "当月总达成率\nTỔNG ĐẠT % TRONG THÁNG",
    ];
    for (var i = 0; i < headers.Length; i++)
    {
        sheet.Cell(2, i + 1).Value = headers[i];
        sheet.Cell(2, i + 1).Style.Alignment.WrapText = true;
    }
    var r = 3;
    foreach (var p in rows)
    {
        sheet.Cell(r, 1).Value = XLCellValue.FromObject(p.Code);
        sheet.Cell(r, 2).Value = p.Hours;
        sheet.Cell(r, 3).Value = p.Hourly;
        sheet.Cell(r, 4).FormulaA1 = $"B{r}*C{r}";
        sheet.Cell(r, 5).Value = p.Actual;
        sheet.Cell(r, 6).FormulaA1 = $"+E{r}/D{r}";
        sheet.Cell(r, 7).Value = p.MonthTarget;
        sheet.Cell(r, 8).Value = p.MonthCumulative;
        sheet.Cell(r, 9).FormulaA1 = $"+H{r}-G{r}";
        sheet.Cell(r, 10).FormulaA1 = $"+H{r}/G{r}";
        sheet.Cell(r, 6).Style.NumberFormat.Format = "0%";
        sheet.Cell(r, 10).Style.NumberFormat.Format = "0%";
        foreach (var c in new[] { 4, 5, 7, 8, 9 })
            sheet.Cell(r, c).Style.NumberFormat.Format = "#,##0";
        r++;
    }
    // Hai dòng trống có kẻ khung như file thật.
    var table = sheet.Range(2, 1, r + 1, 11);
    table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    sheet.Columns(1, 11).Width = 16;
}

static void AddTable(XLWorkbook workbook, string name, string[] headers, params object?[][] rows)
{
    var sheet = workbook.AddWorksheet(name);
    for (var c = 0; c < headers.Length; c++)
        sheet.Cell(1, c + 1).Value = headers[c];
    for (var r = 0; r < rows.Length; r++)
        for (var c = 0; c < rows[r].Length; c++)
            sheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
    sheet.Row(1).Style.Font.Bold = true;
    sheet.Columns().AdjustToContents();
}
