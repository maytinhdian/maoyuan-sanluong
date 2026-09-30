using ClosedXML.Excel;

// Tạo file Excel mẫu có đủ dữ liệu cho cả 8 view.
// Cách dùng: dotnet run --project tools/DisplayBoard.SampleGenerator -- [đường dẫn xuất] [ngày dd/MM/yyyy]
var output = args.Length > 0 ? args[0] : Path.Combine("samples", "SanLuong-mau.xlsx");
var date = args.Length > 1
    ? DateTime.ParseExact(args[1], "dd/MM/yyyy", null)
    : DateTime.Today;

var employees = new (string Code, string Name, string Dept, string Shift, int Total)[]
{
    ("NV001", "Nguyễn Văn A", "Ép", "A", 1250),
    ("NV002", "Trần Văn B", "Sơn", "A", 980),
    ("NV003", "Lê Văn C", "Đóng gói", "A", 1430),
    ("NV004", "Phạm Thị D", "Kiểm tra", "A", 1100),
    ("NV005", "Hoàng Văn E", "Ép", "A", 1560),
    ("NV006", "Đỗ Thị F", "Sơn", "B", 870),
    ("NV007", "Bùi Văn G", "Đóng gói", "B", 1320),
    ("NV008", "Ngô Thị H", "Kiểm tra", "B", 1050),
    ("NV009", "Trịnh Văn I", "Ép", "B", 1410),
    ("NV010", "Võ Thị K", "Sơn", "B", 1200),
    ("NV011", "Đặng Văn L", "Ép", "A", 1180),
    ("NV012", "Lý Thị M", "Đóng gói", "B", 1260),
};

using var workbook = new XLWorkbook();

var data = workbook.AddWorksheet("DATA");
string[] headers = ["Ngày", "Giờ", "Ca", "Mã NV", "Họ tên", "Bộ phận", "Sản lượng", "Mục tiêu", "Ghi chú"];
for (var i = 0; i < headers.Length; i++)
    data.Cell(1, i + 1).Value = headers[i];

// Mỗi nhân viên nhập 1 dòng mỗi giờ từ 08:00 đến 17:00; sản lượng mỗi dòng là phần tăng thêm.
var hours = Enumerable.Range(8, 10).ToArray();
var row = 2;
var random = new Random(42);
foreach (var e in employees)
{
    var remaining = e.Total;
    for (var h = 0; h < hours.Length; h++)
    {
        var isLast = h == hours.Length - 1;
        var share = isLast ? remaining : Math.Min(remaining, (int)Math.Round(e.Total / (double)hours.Length * (0.8 + random.NextDouble() * 0.4)));
        remaining -= share;
        data.Cell(row, 1).Value = date;
        data.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
        data.Cell(row, 2).Value = TimeSpan.FromHours(hours[h]);
        data.Cell(row, 2).Style.DateFormat.Format = "HH:mm";
        data.Cell(row, 3).Value = e.Shift;
        data.Cell(row, 4).Value = e.Code;
        data.Cell(row, 5).Value = e.Name;
        data.Cell(row, 6).Value = e.Dept;
        data.Cell(row, 7).Value = share;
        data.Cell(row, 8).Value = 1200;
        row++;
    }
}
data.Row(1).Style.Font.Bold = true;
data.SheetView.FreezeRows(1);
data.Columns().AdjustToContents();

var directory = workbook.AddWorksheet("DANH_MUC");
string[] directoryHeaders = ["Mã NV", "Họ tên", "Bộ phận", "Trạng thái", "Ảnh"];
for (var i = 0; i < directoryHeaders.Length; i++)
    directory.Cell(1, i + 1).Value = directoryHeaders[i];
for (var i = 0; i < employees.Length; i++)
{
    directory.Cell(i + 2, 1).Value = employees[i].Code;
    directory.Cell(i + 2, 2).Value = employees[i].Name;
    directory.Cell(i + 2, 3).Value = employees[i].Dept;
    directory.Cell(i + 2, 4).Value = "Đang làm";
}
directory.Row(1).Style.Font.Bold = true;
directory.Columns().AdjustToContents();

var departments = workbook.AddWorksheet("BO_PHAN");
string[] deptHeaders = ["Bộ phận", "Mục tiêu", "Màu", "Icon", "Thứ tự"];
for (var i = 0; i < deptHeaders.Length; i++)
    departments.Cell(1, i + 1).Value = deptHeaders[i];
object[][] deptRows =
[
    ["Ép", 5000, "#1565C0", "factory", 1],
    ["Sơn", 3000, "#8D6E1F", "paint", 2],
    ["Đóng gói", 3000, "#7A5C12", "box", 3],
    ["Kiểm tra", 1000, "#0D47A1", "search", 4],
];
for (var r = 0; r < deptRows.Length; r++)
    for (var c = 0; c < deptRows[r].Length; c++)
        departments.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(deptRows[r][c]);
departments.Row(1).Style.Font.Bold = true;
departments.Columns().AdjustToContents();

var notices = workbook.AddWorksheet("THONG_BAO");
string[] noticeHeaders = ["Tiêu đề", "Nội dung", "Ảnh nền", "Từ ngày", "Đến ngày", "Thứ tự", "Bật"];
for (var i = 0; i < noticeHeaders.Length; i++)
    notices.Cell(1, i + 1).Value = noticeHeaders[i];
object?[][] noticeRows =
[
    ["THÔNG BÁO / THÔNG ĐIỆP", "“NĂNG SUẤT HÔM NAY TẠO NÊN THÀNH CÔNG NGÀY MAI”", "factory.jpg", null, null, 1, "x"],
    ["AN TOÀN LAO ĐỘNG", "Đeo đầy đủ bảo hộ trước khi vào xưởng", null, null, null, 2, "x"],
    ["THÔNG BÁO CŨ", "Dòng này đang tắt nên không hiển thị", null, null, null, 3, ""],
];
for (var r = 0; r < noticeRows.Length; r++)
    for (var c = 0; c < noticeRows[r].Length; c++)
        notices.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(noticeRows[r][c]);
notices.Row(1).Style.Font.Bold = true;
notices.Columns().AdjustToContents();

var slogans = workbook.AddWorksheet("KHAU_HIEU");
string[] sloganHeaders = ["Icon", "Dòng 1", "Dòng 2"];
for (var i = 0; i < sloganHeaders.Length; i++)
    slogans.Cell(1, i + 1).Value = sloganHeaders[i];
string[][] sloganRows =
[
    ["trophy", "AN TOÀN", "LÀ SỐ 1"],
    ["people", "LÀM VIỆC", "ĐOÀN KẾT"],
    ["chart", "NÂNG CAO", "NĂNG SUẤT"],
    ["gear", "CHẤT LƯỢNG", "ỔN ĐỊNH"],
];
for (var r = 0; r < sloganRows.Length; r++)
    for (var c = 0; c < sloganRows[r].Length; c++)
        slogans.Cell(r + 2, c + 1).Value = sloganRows[r][c];
slogans.Row(1).Style.Font.Bold = true;
slogans.Columns().AdjustToContents();

var settings = workbook.AddWorksheet("CAU_HINH");
settings.Cell(1, 1).Value = "Khóa";
settings.Cell(1, 2).Value = "Giá trị";
settings.Cell(1, 3).Value = "Ghi chú";
string[][] settingRows =
[
    ["GioBatDau", "07:00", "Giờ bắt đầu trên biểu đồ xu hướng"],
    ["GioKetThuc", "17:00", "Giờ kết thúc; mục tiêu được chia đều trong khoảng này"],
    ["DonVi", "sản phẩm", "Đơn vị hiển thị dưới tổng sản lượng"],
    ["TenCongTy", "MAOYUAN", "Hiện khi không có thông báo"],
];
for (var r = 0; r < settingRows.Length; r++)
    for (var c = 0; c < settingRows[r].Length; c++)
        settings.Cell(r + 2, c + 1).Value = settingRows[r][c];
settings.Row(1).Style.Font.Bold = true;
settings.Columns().AdjustToContents();

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
workbook.SaveAs(output);
Console.WriteLine($"Đã tạo {output} ({row - 2} dòng DATA, ngày {date:dd/MM/yyyy})");
