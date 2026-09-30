# Display Board

App Windows nhỏ dùng **file Excel làm nguồn dữ liệu** và trình chiếu bảng sản lượng lên TV.
Nhân viên vẫn mở, sửa và lưu Excel bình thường; app chỉ đọc. Chi tiết xem spec của project.

## Cấu trúc

| Project | Nội dung |
|---|---|
| `DisplayBoard.Core` | Models, đọc Excel (ClosedXML), tính tổng hợp, cấu hình `display-config.json`. Không phụ thuộc WPF. |
| `DisplayBoard.App` | WPF / MVVM (CommunityToolkit.Mvvm), DI (Microsoft.Extensions.Hosting), log Serilog. |
| `DisplayBoard.Tests` | xUnit cho Core. |
| `samples/SanLuong.xlsx` | Workbook mẫu đúng cấu trúc. |

## File Excel

Sheet `DATA`, dòng đầu là tiêu đề (không phân biệt hoa/thường, thứ tự tuỳ ý):

| Ngày | Mã NV | Họ tên | Bộ phận | Sản lượng | Mục tiêu | Ghi chú |
|---|---|---|---|---:|---:|---|
| bắt buộc | bắt buộc | bắt buộc | bắt buộc | bắt buộc | tuỳ chọn | tuỳ chọn |

- Thiếu sheet `DATA` hoặc thiếu cột bắt buộc: app báo lỗi, liệt kê cột thiếu.
- Dòng lỗi (sai ngày, sản lượng không phải số hoặc âm, thiếu ô bắt buộc): bỏ qua dòng đó và báo số dòng.
- Tổng sản lượng, % hoàn thành, xếp hạng do app tự tính.

## Chạy

Cần .NET 10 SDK.

```
dotnet test                              # chạy được trên mọi hệ điều hành
dotnet run --project DisplayBoard.App    # chỉ trên Windows
```

Cấu hình và log nằm ở `%LOCALAPPDATA%\DisplayBoard\`.

## Tiến độ (theo spec, mục 21)

- [x] Phase 1: solution, DI, MVVM, logging, config, models
- [x] Phase 2: đọc Excel, validate, workbook mẫu, test
- [ ] Phase 3: FileSystemWatcher, debounce/retry, last-good-snapshot
- [ ] Phase 4: phát hiện màn hình, DisplayWindow, Mirror/Independent
- [ ] Phase 5: OverviewView, RankingView, Preview
- [ ] Phase 6: Main Window đầy đủ, tray, lưu cấu hình màn hình
- [ ] Phase 7: hardening
