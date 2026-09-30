# Display Board

Windows app that reads production output from an Excel file and shows dashboards on one or more TVs.
Specification: `display-board-app-spec.md` in the project files.

## Projects

| Project | Target | Purpose |
|---|---|---|
| `DisplayBoard.App` | `net10.0-windows` (WPF) | Main window, display windows, DI host, Serilog |
| `DisplayBoard.Core` | `net10.0` | Models, interfaces, services (no WPF dependency) |
| `DisplayBoard.Tests` | `net10.0` (xUnit) | Tests for Core |

## Build and test

```
dotnet build DisplayBoard.sln
dotnet test DisplayBoard.sln
dotnet run --project DisplayBoard.App   # Windows only
```

## Runtime files

Stored per user under `%LOCALAPPDATA%\DisplayBoard\`:

- `display-config.json` — Excel path, display mode, screen/view assignment
- `logs\display-board-YYYYMMDD.log` — rolling daily log, kept 30 days

## Excel data

`ExcelDataReader` reads the `DATA` sheet read-only with ClosedXML, sharing the file so it works while Excel has it open.
A sample workbook lives at `samples/SanLuong.xlsx`.

- Headers are matched ignoring case, extra spaces and column order; unknown columns are ignored.
- Required: `Ngày`, `Mã NV`, `Họ tên`, `Bộ phận`, `Sản lượng`. Optional: `Mục tiêu`, `Ghi chú`.
- A missing file, missing `DATA` sheet, missing required columns or a duplicated header throws `ExcelValidationException`.
- A bad row (empty required cell, invalid date, non-numeric or negative number) is skipped and reported in
  `ExcelReadResult.Warnings` with its Excel row number. Blank rows are ignored.
- Dates may be real Excel dates or text like `28/09/2026`. Numbers stored as text accept `1250.5` or `1250,5`;
  `1.250` / `1,250` is rejected as ambiguous.
