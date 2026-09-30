# Display Board – Bảng sản lượng trên TV

App Windows đọc file Excel theo dõi sản lượng (6 chuyền, sheet `HIEN_THI`) và trình chiếu dashboard lên 2 TV phụ. Nhân viên vẫn mở, sửa và lưu Excel như bình thường; app chỉ đọc và tự cập nhật sau khi file được lưu.

## Yêu cầu

- Windows 10/11
- .NET 10 SDK (để build) hoặc .NET 10 Desktop Runtime (để chạy bản đã build)

## Chạy thử

```powershell
dotnet run --project src/DisplayBoard.App
```

1. Bấm **Chọn file…** và chọn `samples/Theo_doi_san_luong_V18_mau.xlsx` (file V18 với số liệu thử).
2. Chọn màn hình cho TV1/TV2, tick các nội dung muốn chiếu, sắp thứ tự bằng ↑↓.
3. Xem ở tab **Xem trước**, rồi bấm **Bắt đầu trình chiếu**.
4. Khi đang trình chiếu, bấm X chỉ ẩn cửa sổ xuống khay hệ thống; thoát hẳn bằng menu **Thoát** ở khay.

Tạo lại file nội dung phụ mẫu `samples/display-content.xlsx`:

```powershell
dotnet run --project tools/DisplayBoard.SampleGenerator -- samples
```

## Đóng gói để cài cho khách

```powershell
powershell -ExecutionPolicy Bypass -File tools/publish.ps1
```

Tạo `dist/DisplayBoard-win-x64.zip`: một file `DisplayBoard.exe` chạy được trên Windows 10/11 64-bit mà không cần cài .NET, kèm thư mục `samples` và `HUONG_DAN.txt`.

## File Excel sản lượng

**Mọi con số đều do công thức Excel tính**, để nhân viên dùng chính file đó kiểm tra. App chỉ đọc sheet `HIEN_THI` (bảng `tblHienThi`) và **không bao giờ ghi vào file**, không tự tính %, chênh lệch hay phần thiếu.

- 6 dòng dữ liệu, mỗi dòng một chuyền, và dòng **TỔNG CỘNG** ở cuối.
- Cột được nhận theo tiêu đề tiếng Việt (hàng 3): CHUYỀN, MÃ HÀNG, MỤC TIÊU TRONG NGÀY, THỰC TẾ TRONG NGÀY, % ĐẠT NGÀY, CÒN THIẾU, THIẾU NGÀY TRƯỚC, lũy kế và % tháng, GIỜ 1–12… Thiếu cột CHUYỀN, MỤC TIÊU TRONG NGÀY hoặc THỰC TẾ TRONG NGÀY thì app báo lỗi rõ tên cột.
- Ngày lấy từ cột NGÀY (hoặc ô ngày ở hàng tiêu đề).
- Trạng thái màu theo % Excel tính: Đạt ≥ 100%, Gần đạt 90–99%, Chậm < 90%.
- Dòng TỔNG CỘNG không cộng mục tiêu/lũy kế tháng theo mã hàng (mã có thể trùng giữa các chuyền), nên màn hình tháng hiện % theo từng chuyền và tổng lũy kế tháng của các chuyền.
- **File phải được Excel tính và lưu ít nhất một lần.** App đọc giá trị Excel đã tính sẵn trong file; file tạo bằng công cụ khác chưa mở bằng Excel sẽ bị báo "File chưa được Excel tính công thức".

### File nội dung phụ (không bắt buộc)

`display-content.xlsx` đặt cạnh file sản lượng (hoặc chọn file khác ở màn hình chính):

| Sheet | Cột |
|---|---|
| `SAN_PHAM` | Mã hàng, Tên hiển thị, Màu, Ảnh, Thứ tự |
| `THONG_BAO` | Tiêu đề, Nội dung, Ảnh nền, Từ ngày, Đến ngày, Thứ tự, Bật |
| `KHAU_HIEU` | Icon, Dòng 1, Dòng 2 |
| `CAU_HINH` | Khóa, Giá trị (`DonVi`, `TenCongTy`) |

Ảnh sản phẩm và ảnh nền thông báo đặt trong thư mục `images` cạnh file Excel. Ảnh sản phẩm đặt tên theo mã, ví dụ `883.jpg`.

## 10 nội dung hiển thị

| Id | Nội dung |
|---|---|
| `overview` | Sản lượng hôm nay: tổng, mục tiêu, % hoàn thành, cột theo chuyền, phần thiếu hôm trước |
| `ranking` | Xếp hạng chuyền theo % hoàn thành |
| `product-progress` | Thẻ tiến độ từng chuyền (ngày và tháng) |
| `top-products` | Chuyền dẫn đầu / vượt mục tiêu |
| `not-met` | Chuyền chưa đạt |
| `notice` | Thông báo / thông điệp |
| `detail` | Bảng chi tiết từng chuyền, tự lật trang |
| `month-progress` | Lũy kế và % tháng từng chuyền |
| `lines` | Thẻ từng chuyền: mã hàng, thực tế/mục tiêu, %, tiến độ theo giờ, phần thiếu hôm trước |
| `hourly` | Sản lượng từng giờ (GIỜ 1–12) của mỗi chuyền, tô màu so với mục tiêu mỗi giờ, kèm % tiến độ theo giờ |

Mỗi TV có một danh sách nội dung tự xoay (mặc định 15 giây/trang). Nội dung không có dữ liệu được tự bỏ qua.

## Cấu hình và log

- Cấu hình: `%AppData%\DisplayBoard\display-config.json`
- Tên ứng dụng: khách tự đặt ở tab **Cài đặt → Tên ứng dụng** (để trống = Display Board)
- Log: `%LocalAppData%\DisplayBoard\logs\display-board-YYYYMMDD.log`

## Cấu trúc mã nguồn

```text
src/DisplayBoard.Core      Đọc Excel (HIEN_THI), dựng snapshot, theo dõi file, xoay trang (không phụ thuộc WPF)
src/DisplayBoard.App       WPF: cửa sổ chính, cửa sổ TV, 10 view, khay hệ thống
tests/DisplayBoard.Tests   Unit test cho Core (chạy được trên mọi hệ điều hành)
tests/DisplayBoard.App.Tests  Render từng view ra PNG và bắt lỗi binding (chỉ Windows)
tools/DisplayBoard.SampleGenerator  Tạo file nội dung phụ mẫu
```

Đặc tả chi tiết: xem `display-board-app-spec.md` trong project.
