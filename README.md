# Display Board – Bảng sản lượng trên TV

App Windows đọc file Excel sản lượng của khách (theo sản phẩm) và trình chiếu dashboard lên 2 TV phụ. Nhân viên vẫn mở, sửa và lưu Excel như bình thường; app chỉ đọc và tự cập nhật sau khi file được lưu.

## Yêu cầu

- Windows 10/11
- .NET 10 SDK (để build) hoặc .NET 10 Desktop Runtime (để chạy bản đã build)

## Chạy thử

```powershell
dotnet run --project src/DisplayBoard.App
```

1. Bấm **Chọn file…** và chọn `samples/SanLuong-khach-mau.xlsx` (bản mô phỏng file của khách).
2. Chọn màn hình cho TV1/TV2, tick các nội dung muốn chiếu, sắp thứ tự bằng ↑↓.
3. Xem ở tab **Xem trước**, rồi bấm **Bắt đầu trình chiếu**.
4. Khi đang trình chiếu, bấm X chỉ ẩn cửa sổ xuống khay hệ thống; thoát hẳn bằng menu **Thoát** ở khay.

Tạo lại file mẫu (tham số thứ hai là ngày ghi ở ô A1):

```powershell
dotnet run --project tools/DisplayBoard.SampleGenerator -- samples 2026/09/30
```

## File Excel của khách

App đọc thẳng file sản lượng của khách, **không bao giờ ghi vào file đó**. Mỗi dòng là một sản phẩm:

| Cột | Tiêu đề (Trung / Việt) | Ghi chú |
|---|---|---|
| A | 产品代码 / MÃ SẢN PHẨM | chữ hoặc số |
| B | 每日工时数 / THỜI GIAN LÊN CA | giờ của ca làm hôm đó |
| C | 每小时目标产量 / MỤC TIÊU MỖI GIỜ | |
| D | 每日目标产量 / MỤC TIÊU TRONG NGÀY | trống thì app tính B × C |
| E | 每日实际产量 / THỰC TẾ | nhân viên nhập |
| G | 当月总目标产量 / TỔNG SẢN LƯỢNG TRONG THÁNG | mục tiêu tháng |
| H | 当月累计产能 / LŨY KẾ | |

- Ngày lấy từ ô A1 (`日期：2026/09/30`). Cột được nhận theo tiêu đề (tiếng Trung trước, rồi tiếng Việt), nên đổi thứ tự cột vẫn đọc được.
- Dòng tổng cuối bảng (`TỔNG`, `TỔNG CỘNG`, `合计`, `Total`) được bỏ qua, không tính là một sản phẩm.
- %, chênh lệch và trạng thái do app tự tính: Đạt ≥ 100%, Gần đạt 90–99%, Chậm < 90%.
- **Mỗi sheet là một chuyền** (khách có 6 chuyền). App đọc mọi sheet có ngày mới nhất; sheet của ngày cũ còn sót lại bị bỏ qua. Cùng một mã sản phẩm ở 2 chuyền được tính riêng. Ghi tên sheet vào ô **Sheet** ở màn hình chính nếu chỉ muốn chiếu một chuyền.
- **Phần thiếu hôm trước**: mỗi lần đọc file, app lưu kết quả của ngày đó vào `%AppData%\DisplayBoard\daily-history.json`. Hôm sau các màn hình hiện số còn thiếu của ngày làm việc trước (bỏ qua ngày nghỉ), tính riêng theo từng chuyền và sản phẩm.

### File nội dung phụ (không bắt buộc)

`display-content.xlsx` đặt cạnh file khách (hoặc chọn file khác ở màn hình chính):

| Sheet | Cột |
|---|---|
| `SAN_PHAM` | Mã sản phẩm, Tên hiển thị, Màu, Ảnh, Thứ tự |
| `THONG_BAO` | Tiêu đề, Nội dung, Ảnh nền, Từ ngày, Đến ngày, Thứ tự, Bật |
| `KHAU_HIEU` | Icon, Dòng 1, Dòng 2 |
| `CAU_HINH` | Khóa, Giá trị (`DonVi`, `TenCongTy`) |

Ảnh sản phẩm và ảnh nền thông báo đặt trong thư mục `images` cạnh file Excel. Ảnh sản phẩm đặt tên theo mã, ví dụ `883.jpg`.

## 9 nội dung hiển thị

| Id | Nội dung |
|---|---|
| `overview` | Sản lượng hôm nay: tổng, mục tiêu, % hoàn thành, cột theo chuyền (hoặc theo sản phẩm nếu chỉ 1 chuyền), phần thiếu hôm trước |
| `ranking` | Xếp hạng sản phẩm theo % hoàn thành |
| `product-progress` | Thẻ tiến độ từng sản phẩm (ngày và tháng) |
| `top-products` | Sản phẩm vượt mục tiêu |
| `not-met` | Sản phẩm chưa đạt |
| `notice` | Thông báo / thông điệp |
| `detail` | Bảng chi tiết giống file khách, tự lật trang |
| `month-progress` | Lũy kế tháng so với mục tiêu tháng |
| `lines` | So sánh các chuyền: thực tế/mục tiêu, %, số sản phẩm đạt, phần thiếu hôm trước |

Mỗi TV có một danh sách nội dung tự xoay (mặc định 15 giây/trang). Nội dung không có dữ liệu được tự bỏ qua.

## Cấu hình và log

- Cấu hình: `%AppData%\DisplayBoard\display-config.json`
- Lịch sử từng ngày: `%AppData%\DisplayBoard\daily-history.json`
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
