using ClosedXML.Excel;

// Tạo file nội dung phụ display-content.xlsx (thông báo, khẩu hiệu, tên/màu sản phẩm, cấu hình).
// File sản lượng mẫu samples/Theo_doi_san_luong_V18_mau.xlsx là bản V18 đã được Excel tính sẵn, không tạo ở đây.
// Cách dùng: dotnet run --project tools/DisplayBoard.SampleGenerator -- [thư mục xuất]
var folder = args.Length > 0 ? args[0] : "samples";
Directory.CreateDirectory(folder);

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

Console.WriteLine($"Đã tạo {folder}/display-content.xlsx");

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
