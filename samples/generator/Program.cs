using ClosedXML.Excel;

// Generates samples/SanLuong.xlsx following spec §4: hourly incremental rows on DATA plus the optional sheets.
using var wb = new XLWorkbook();

var employees = new (string Code, string Name, string Dept, string Shift, int PerHour, int? Target)[]
{
    ("NV001", "Nguyễn Văn A", "Ép", "A", 160, 1200),
    ("NV002", "Trần Văn B", "Sơn", "A", 120, 1200),
    ("NV003", "Lê Thị C", "Ép", "A", 165, 1200),
    ("NV004", "Phạm Văn D", "Lắp ráp", "A", 140, 1100),
    ("NV005", "Hoàng Thị E", "Lắp ráp", "B", 105, 1100),
    ("NV006", "Võ Văn F", "Sơn", "B", 150, 1200),
    ("NV007", "Đặng Thị G", "Đóng gói", "B", 185, 1400),
    ("NV008", "Bùi Văn H", "Đóng gói", "B", 170, 1400),
    ("NV009", "Đỗ Thị I", "Ép", "A", 148, 1200),
    ("NV010", "Ngô Văn K", "Kiểm tra", "A", 80, null),
};

var ws = wb.AddWorksheet("DATA");
string[] headers = ["Ngày", "Giờ", "Ca", "Mã NV", "Họ tên", "Bộ phận", "Sản lượng", "Mục tiêu", "Ghi chú"];
for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

var date = new DateTime(2026, 9, 28);
int[] variation = [0, 8, -6, 4, -3];
var row = 2;
for (var hour = 8; hour <= 12; hour++)
{
    for (var e = 0; e < employees.Length; e++)
    {
        var emp = employees[e];
        ws.Cell(row, 1).Value = date;
        ws.Cell(row, 2).Value = TimeSpan.FromHours(hour);
        ws.Cell(row, 3).Value = emp.Shift;
        ws.Cell(row, 4).Value = emp.Code;
        ws.Cell(row, 5).Value = emp.Name;
        ws.Cell(row, 6).Value = emp.Dept;
        ws.Cell(row, 7).Value = emp.PerHour + variation[(hour + e) % variation.Length];
        if (emp.Target is int t) ws.Cell(row, 8).Value = t;
        if (hour == 8 && emp.Code == "NV005") ws.Cell(row, 9).Value = "Vào ca trễ";
        if (hour == 8 && emp.Code == "NV010") ws.Cell(row, 9).Value = "Không giao chỉ tiêu";
        row++;
    }
}
ws.Column(1).Style.DateFormat.Format = "dd/MM/yyyy";
ws.Column(2).Style.DateFormat.Format = "hh:mm";
ws.Range(2, 7, row - 1, 8).Style.NumberFormat.Format = "#,##0";
StyleHeader(ws, headers.Length);
ws.SheetView.FreezeRows(1);
ws.RangeUsed()!.SetAutoFilter();
ws.Columns().AdjustToContents();

var dm = wb.AddWorksheet("DANH_MUC");
WriteRow(dm, 1, "Mã NV", "Họ tên", "Bộ phận", "Trạng thái", "Ảnh");
for (var e = 0; e < employees.Length; e++)
    WriteRow(dm, e + 2, employees[e].Code, employees[e].Name, employees[e].Dept, "Đang làm", e == 0 ? "NV001.jpg" : "");
StyleHeader(dm, 5);

var bp = wb.AddWorksheet("BO_PHAN");
WriteRow(bp, 1, "Bộ phận", "Mục tiêu", "Màu", "Icon", "Thứ tự");
WriteRow(bp, 2, "Ép", 3600, "#2E86DE", "press", 1);
WriteRow(bp, 3, "Sơn", 2400, "#E67E22", "paint", 2);
WriteRow(bp, 4, "Lắp ráp", "", "#27AE60", "wrench", 3);
WriteRow(bp, 5, "Đóng gói", 2800, "#8E44AD", "box", 4);
WriteRow(bp, 6, "Kiểm tra", "", "", "search", 5);
StyleHeader(bp, 5);

var tb = wb.AddWorksheet("THONG_BAO");
WriteRow(tb, 1, "Tiêu đề", "Nội dung", "Ảnh nền", "Từ ngày", "Đến ngày", "Thứ tự", "Bật");
WriteRow(tb, 2, "THÔNG BÁO", "NĂNG SUẤT HÔM NAY TẠO NÊN THÀNH CÔNG NGÀY MAI", "factory.jpg", new DateTime(2026, 9, 1), new DateTime(2026, 12, 31), 1, "x");
WriteRow(tb, 3, "AN TOÀN", "ĐEO ĐẦY ĐỦ BẢO HỘ KHI VẬN HÀNH MÁY", "", "", "", 2, "x");
WriteRow(tb, 4, "NGHỈ LỄ", "Thông báo đã hết hạn, không hiển thị", "", new DateTime(2026, 1, 1), new DateTime(2026, 1, 3), 3, "x");
tb.Range(2, 4, 4, 5).Style.DateFormat.Format = "dd/MM/yyyy";
StyleHeader(tb, 7);

var kh = wb.AddWorksheet("KHAU_HIEU");
WriteRow(kh, 1, "Icon", "Dòng 1", "Dòng 2");
WriteRow(kh, 2, "trophy", "AN TOÀN", "LÀ SỐ 1");
WriteRow(kh, 3, "star", "CHẤT LƯỢNG", "LÀ DANH DỰ");
WriteRow(kh, 4, "clock", "ĐÚNG GIỜ", "ĐÚNG TIẾN ĐỘ");
WriteRow(kh, 5, "team", "ĐOÀN KẾT", "CÙNG PHÁT TRIỂN");
StyleHeader(kh, 3);

var ch = wb.AddWorksheet("CAU_HINH");
WriteRow(ch, 1, "Khóa", "Giá trị");
WriteRow(ch, 2, "GioBatDau", "07:00");
WriteRow(ch, 3, "GioKetThuc", "17:00");
WriteRow(ch, 4, "DonVi", "sản phẩm");
WriteRow(ch, 5, "TenCongTy", "MAOYUAN");
StyleHeader(ch, 2);

foreach (var sheet in wb.Worksheets) sheet.Columns().AdjustToContents();
wb.SaveAs(args[0]);

static void WriteRow(IXLWorksheet sheet, int row, params object[] values)
{
    for (var i = 0; i < values.Length; i++)
    {
        sheet.Cell(row, i + 1).Value = values[i] switch
        {
            "" => Blank.Value,
            string s => s,
            int n => n,
            DateTime d => d,
            _ => throw new ArgumentException(),
        };
    }
}

static void StyleHeader(IXLWorksheet sheet, int columns)
{
    var header = sheet.Range(1, 1, 1, columns);
    header.Style.Font.Bold = true;
    header.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
}
