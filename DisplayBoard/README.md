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
