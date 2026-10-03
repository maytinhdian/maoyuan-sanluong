# Display Board – Bảng sản lượng trên TV

App Windows đọc file Excel theo dõi sản lượng (6 chuyền, sheet `HIEN_THI`) và trình chiếu dashboard lên 2 TV phụ. Nhân viên vẫn mở, sửa và lưu Excel như bình thường; app chỉ đọc và tự cập nhật sau khi file được lưu.

> **Ba phiên bản:** nhánh `master` là bản 1.x (TV nối thẳng vào máy có Excel). Nhánh `v2-lan` là bản 2.x: máy có Excel làm máy chủ, TV xem qua trình duyệt trong mạng LAN. Nhánh `v3` là bản 3.x: như 2.x và thêm trang nhập liệu cho tổ trưởng trên điện thoại. Bộ cài bản sau cài đè lên bản trước.

## Bản 3.x: nhập liệu qua trình duyệt

- Tổ trưởng mở `http://<IP máy chủ>:5080/nhap` trên điện thoại, đăng nhập bằng mã PIN (đặt ở tab **Nhập liệu** của app, mỗi người một PIN, có thể giới hạn chuyền).
- Tab **Sản lượng**: chọn chuyền, app tự chọn giờ đang làm theo ca (CAU_HINH_CA), nhập số rồi **Ghi vào Excel**: số vào ô `GIỜ n` của dòng hôm nay ở `NHAP_LIEU`. Chuyền chưa có dòng hôm nay thì app tự tạo dòng mới, chép mã sản phẩm, ca, mục tiêu giờ từ ngày làm trước (sửa bằng **Sửa kế hoạch**). Số lớn hơn 3 lần mục tiêu giờ bị chặn.
- Tab **Hàng lỗi**: loại lỗi, số lượng, tối đa 4 ảnh chụp bằng camera (thu nhỏ còn 1600px trên điện thoại). Ảnh lưu vào `images\hang_loi`, thêm một dòng vào `HANG_LOI`; nhiều ảnh ghi chung một ô `TÊN FILE ẢNH`, cách nhau bởi `; `.
- Tab **Đã gửi**: trạng thái từng phiếu (đang ghi, đang chờ Excel, đã ghi, lỗi).
- Máy chủ ghi qua **chính Microsoft Excel trên máy chủ** (COM), không sửa file trực tiếp: file đang mở thì ghi vào cửa sổ Excel đó rồi lưu, chưa mở thì mở ngầm, ghi, lưu, đóng. Nhờ vậy công thức tự tính lại và TV cập nhật ngay. Có người đang sửa ô trong Excel thì phiếu chờ và tự ghi khi họ bấm Enter/Esc (chờ tối đa 15 phút). Cần cài Excel bản desktop trên máy chủ.
- Chỉ ghi vào ô nhập tay (NGÀY, CHUYỀN, MÃ SẢN PHẨM, MÃ CA, MỤC TIÊU MỖI GIỜ, GIỜ 1–12; ở HANG_LOI: NGÀY, GIỜ, CHUYỀN, LOẠI LỖI, SỐ LƯỢNG, TÊN FILE ẢNH, GHI CHÚ). Cột tìm theo tiêu đề tiếng Việt nên dùng được file V20 trở lên.

## Bản 2.x: TV xem qua mạng LAN

- Máy chủ (máy có file Excel) chạy Display Board như bình thường. App mở máy chủ HTTP + WebSocket ở cổng 5080 (đổi ở tab **Máy chủ**).
- Tab **Trang chủ** là danh sách TV qua mạng: thêm/xoá TV, đặt tên, chọn nội dung riêng cho từng TV. Mỗi TV có địa chỉ riêng `http://<IP máy chủ>:5080/tv/<số>`; trang `http://<IP máy chủ>:5080/` liệt kê các TV.
- Bấm **Lưu & áp dụng** là TV đang mở nhận nội dung mới ngay. Cấu hình cũ từ 1.x tự chuyển thành TV1/TV2 qua mạng.
- TV cắm dây HDMI thẳng vào máy chủ vẫn dùng được ở tab **Chiếu trên máy này** (giống 1.x).
- Excel vừa lưu là máy chủ đẩy số mới sang TV ngay. Mất kết nối thì TV giữ số liệu gần nhất và tự kết nối lại.
- Có thể đặt **mã truy cập**: TV nhập một lần, hoặc mở địa chỉ có `?key=…`.
- Ảnh sản phẩm/thông báo được phát qua `/img/<mã>`; chỉ ảnh có trong dữ liệu mới được phát, không lộ đường dẫn trên máy.
- Giao diện web nằm trong `src/DisplayBoard.Server/wwwroot` (nhúng vào exe), vẽ lại đúng 10 màn hình của bản WPF ở khung 1920×1080 và co giãn theo TV.

## Yêu cầu

- Windows 10/11
- .NET 10 SDK (để build) hoặc .NET 10 Desktop Runtime (để chạy bản đã build)

## Chạy thử

```powershell
dotnet run --project src/DisplayBoard.App
```

1. Bấm **Chọn file…** và chọn `samples/Theo_doi_san_luong_V19_mau.xlsx` (file V19 với số liệu thử; file V18 vẫn đọc được).
2. Ở **TV XEM QUA MẠNG LAN**, chọn TV, tick các nội dung muốn chiếu, sắp thứ tự bằng ↑↓, bấm **Lưu & áp dụng**.
3. Bấm **Mở** để xem trang TV trong trình duyệt, hoặc xem ở tab **Xem trước**. TV nối dây: tab **Chiếu trên máy này** > **Bắt đầu trình chiếu**.
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

Bộ cài (`DisplayBoard-Setup-<phiên bản>.exe`, dùng Inno Setup, script ở `tools/installer/DisplayBoard.iss`) được tạo tự động và đưa lên trang **Releases**: vào Actions > Release > Run workflow và nhập số phiên bản, hoặc đẩy tag dạng `v1.0.1`.

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
| `defects` | Hàng lỗi hôm nay. File V20: lưới ảnh QC chụp (sheet HANG_LOI, ảnh trong `images\hang_loi`), 6 ảnh/trang, kèm loại lỗi, số lượng và tổng số lỗi từng chuyền. File V19: bảng số lỗi và tỷ lệ lỗi từng chuyền |
| `defects-4`, `defects-2` | Hàng lỗi như trên nhưng 4 hoặc 2 ảnh lớn/trang, ảnh chiếm cả màn hình, tổng số lỗi ghi ở chân trang |

Mỗi TV có một danh sách nội dung tự xoay (mặc định 15 giây/trang). Nội dung không có dữ liệu được tự bỏ qua.

## Cấu hình và log

- Cấu hình: `%AppData%\DisplayBoard\display-config.json`
- Tên ứng dụng: khách tự đặt ở tab **Cài đặt → Tên ứng dụng** (để trống = Display Board)
- Log: `%LocalAppData%\DisplayBoard\logs\display-board-YYYYMMDD.log`

## Cấu trúc mã nguồn

```text
src/DisplayBoard.Core      Đọc Excel (HIEN_THI), dựng snapshot, theo dõi file, xoay trang (không phụ thuộc WPF)
src/DisplayBoard.App       WPF: cửa sổ chính, cửa sổ TV, 10 view, khay hệ thống
src/DisplayBoard.Server    Máy chủ LAN (Kestrel): /tv/{n}, /api/state, /ws, /img, và giao diện web trong wwwroot
tests/DisplayBoard.Tests   Unit test cho Core (chạy được trên mọi hệ điều hành)
tests/DisplayBoard.Server.Tests  Test máy chủ LAN: trang TV, playlist, mã truy cập, WebSocket, ảnh, đổi cổng, trang nhập liệu
tests/DisplayBoard.App.Tests  Render từng view ra PNG và bắt lỗi binding (chỉ Windows)
tools/DisplayBoard.SampleGenerator  Tạo file nội dung phụ mẫu
```

Đặc tả chi tiết: xem `display-board-app-spec.md` trong project.
