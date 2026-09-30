# Display Board – Bảng sản lượng trên TV

App Windows đọc file Excel sản lượng và trình chiếu dashboard lên 2 TV phụ. Nhân viên vẫn mở, sửa và lưu Excel như bình thường; app chỉ đọc và tự cập nhật sau khi file được lưu.

## Yêu cầu

- Windows 10/11
- .NET 10 SDK (để build) hoặc .NET 10 Desktop Runtime (để chạy bản đã build)

## Chạy thử

```powershell
dotnet run --project src/DisplayBoard.App
```

1. Bấm **Chọn file…** và chọn `samples/SanLuong-mau.xlsx`.
2. Chọn màn hình cho TV1/TV2, tick các nội dung muốn chiếu, sắp thứ tự bằng ↑↓.
3. Xem ở tab **Xem trước**, rồi bấm **Bắt đầu trình chiếu**.
4. Khi đang trình chiếu, bấm X chỉ ẩn cửa sổ xuống khay hệ thống; thoát hẳn bằng menu **Thoát** ở khay.

Tạo lại file mẫu (dữ liệu theo ngày hôm nay):

```powershell
dotnet run --project tools/DisplayBoard.SampleGenerator -- samples/SanLuong-mau.xlsx
```

## Cấu trúc file Excel

| Sheet | Bắt buộc | Cột |
|---|---|---|
| `DATA` | Có | Ngày, Giờ*, Ca*, Mã NV, Họ tên, Bộ phận, Sản lượng, Mục tiêu*, Ghi chú* |
| `DANH_MUC` | Không | Mã NV, Họ tên, Bộ phận, Trạng thái, Ảnh |
| `BO_PHAN` | Không | Bộ phận, Mục tiêu, Màu, Icon, Thứ tự |
| `THONG_BAO` | Không | Tiêu đề, Nội dung, Ảnh nền, Từ ngày, Đến ngày, Thứ tự, Bật |
| `KHAU_HIEU` | Không | Icon, Dòng 1, Dòng 2 |
| `CAU_HINH` | Không | Khóa, Giá trị (`GioBatDau`, `GioKetThuc`, `DonVi`, `TenCongTy`) |

\* không bắt buộc.

- Mỗi dòng `Sản lượng` là **số làm được trong lần nhập đó**; app tự cộng các dòng cùng ngày của một nhân viên.
- `Mục tiêu` là mục tiêu cả ngày; nhập nhiều dòng thì app lấy giá trị lớn nhất, không cộng dồn.
- Dòng lỗi được bỏ qua và báo ở tab **Nhật ký** kèm số dòng.
- Icon có sẵn: `factory`, `paint`, `box`, `search`, `gear`, `trophy`, `people`, `chart`, `trend`, `star`, `tool`, `truck`.

Ảnh nhân viên và ảnh nền thông báo đặt trong thư mục `images` cạnh file Excel (hoặc thư mục chọn trong **Cài đặt**). Ảnh nhân viên đặt tên theo Mã NV, ví dụ `NV001.jpg`.

## 8 nội dung hiển thị

| Id | Nội dung |
|---|---|
| `overview` | Sản lượng hôm nay: tổng, mục tiêu, % hoàn thành, biểu đồ theo bộ phận |
| `ranking` | Bảng xếp hạng top 10 nhân viên |
| `department-progress` | Tiến độ từng bộ phận |
| `top-performers` | Top 5 nhân viên xuất sắc (có ảnh) |
| `not-met` | Nhân viên chưa đạt |
| `notice` | Thông báo / thông điệp |
| `detail` | Bảng chi tiết, tự lật trang |
| `trend` | Biểu đồ xu hướng lũy kế theo giờ |

Mỗi TV có một danh sách nội dung tự xoay (mặc định 15 giây/trang). Nội dung không có dữ liệu được tự bỏ qua.

## Cấu hình và log

- Cấu hình: `%AppData%\DisplayBoard\display-config.json`
- Log: `%LocalAppData%\DisplayBoard\logs\display-board-YYYYMMDD.log`

## Cấu trúc mã nguồn

```text
src/DisplayBoard.Core      Đọc Excel, tổng hợp dữ liệu, snapshot, theo dõi file, xoay trang (không phụ thuộc WPF)
src/DisplayBoard.App       WPF: cửa sổ chính, cửa sổ TV, 8 view, khay hệ thống
tests/DisplayBoard.Tests   Unit test cho Core (chạy được trên mọi hệ điều hành)
tests/DisplayBoard.App.Tests  Render từng view ra PNG và bắt lỗi binding (chỉ Windows)
tools/DisplayBoard.SampleGenerator  Tạo file Excel mẫu
```

Đặc tả chi tiết: xem `display-board-app-spec.md` trong project.
