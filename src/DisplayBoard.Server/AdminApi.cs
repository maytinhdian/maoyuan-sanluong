using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using DisplayBoard.Core.Data;
using DisplayBoard.Core.Entry;
using DisplayBoard.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DisplayBoard.Server;

/// <summary>
/// Trang quản lý /quan-ly (bản 4.x): kế hoạch và số liệu mọi ngày, mục tiêu tháng, danh mục, nhật ký sửa, xuất Excel,
/// nhập dữ liệu từ file Excel cũ. Đăng nhập bằng mã PIN như /nhap; chỉ người được đánh dấu "Quản lý" mới vào được.
/// </summary>
internal sealed class AdminApi(EntryApi auth, DataMaintenance maintenance, TimeProvider time)
{
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, (ImportPreview Preview, DateTimeOffset At)> _imports = new(StringComparer.Ordinal);

    private ProductionStore Store => maintenance.Database.Store;

    public sealed record EntryRequest(string? Line, DateOnly Date, string? ProductCode, string? ShiftCode, decimal? HourlyTarget,
        List<decimal?>? Hours, decimal? Workers, string? Reason, decimal? DowntimeMinutes, string? Status, string? Note);
    public sealed record DefectRequest(long Id, DateOnly Date, string? Time, string? Line, string? DefectType, decimal? Quantity, string? Note);
    public sealed record TargetsRequest(DateOnly Month, List<TargetItem>? Targets);
    public sealed record TargetItem(string? ProductCode, decimal Target, string? Note);
    public sealed record ShiftItem(string? Code, string? Name, List<string?>? Periods, bool Active);
    public sealed record DisplayDateRequest(DateOnly? Date);

    public void Map(WebApplication app)
    {
        app.MapGet("/quan-ly", () => WebAssets.Result("quanly.html"));

        app.MapGet("/api/quanly/catalog", (HttpContext http, CancellationToken _) => Run(http, _ => Catalog(Store.Load())));

        app.MapGet("/api/quanly/day", (HttpContext http, DateOnly? date) => Run(http, _ =>
        {
            var data = Store.Load();
            var day = date ?? DisplayCalculator.DisplayDate(data) ?? Today();
            return Day(data, day);
        }));

        app.MapPost("/api/quanly/entry", (HttpContext http, EntryRequest request) => Run(http, user =>
        {
            var data = Store.Load();
            var line = data.FindLine(request.Line) ?? throw new EntryException($"Không có chuyền \"{request.Line}\".");
            var product = request.ProductCode is { Length: > 0 } p
                ? data.Products.FirstOrDefault(x => ProductionData.Same(x.Code, p))?.Code ?? throw new EntryException($"Không có mã sản phẩm \"{p}\".")
                : null;
            var shift = request.ShiftCode is { Length: > 0 } s
                ? data.Shifts.FirstOrDefault(x => ProductionData.Same(x.Code, s))?.Code ?? throw new EntryException($"Không có mã ca \"{s}\".")
                : null;
            var hours = (request.Hours ?? []).Take(DayEntry.MaxHours).ToList();
            while (hours.Count < DayEntry.MaxHours)
                hours.Add(null);
            if (hours.Any(h => h is < 0) || request.HourlyTarget is < 0 || request.Workers is < 0 || request.DowntimeMinutes is < 0)
                throw new EntryException("Số không được âm.");
            Store.SaveEntry(new DayEntry
            {
                Date = request.Date, LineCode = line.Code, ProductCode = product, ShiftCode = shift, HourlyTarget = request.HourlyTarget,
                Hours = hours, Workers = request.Workers, Reason = request.Reason, DowntimeMinutes = request.DowntimeMinutes,
                Status = request.Status, Note = request.Note,
            }, user.Name);
            return Day(Store.Load(), request.Date);
        }));

        app.MapDelete("/api/quanly/entry", (HttpContext http, DateOnly date, string line) => Run(http, user =>
        {
            var def = Store.Load().FindLine(line) ?? throw new EntryException($"Không có chuyền \"{line}\".");
            Store.DeleteEntry(date, def.Code, user.Name);
            return Day(Store.Load(), date);
        }));

        // Chép kế hoạch (mã sản phẩm, ca, mục tiêu) của ngày làm gần nhất sang ngày này cho các chuyền chưa có dòng.
        app.MapPost("/api/quanly/copy-plan", (HttpContext http, DateOnly date) => Run(http, user =>
        {
            var data = Store.Load();
            foreach (var line in data.Lines.Where(l => l.Active && DisplayCalculator.Entry(data, date, l.Code) is null))
            {
                var last = data.Entries.Where(e => e.Date < date && ProductionData.Same(e.LineCode, line.Code)
                        && e.ProductCode is not null && e.ShiftCode is not null && e.HourlyTarget is not null)
                    .MaxBy(e => e.Date);
                if (last is not null)
                    Store.SavePlan(date, line.Code, last.ProductCode!, last.ShiftCode!, last.HourlyTarget!.Value, user.Name);
            }
            return Day(Store.Load(), date);
        }));

        app.MapPost("/api/quanly/defect", (HttpContext http, DefectRequest request) => Run(http, user =>
        {
            var data = Store.Load();
            var line = data.FindLine(request.Line) ?? throw new EntryException($"Không có chuyền \"{request.Line}\".");
            if (request.Quantity is not > 0)
                throw new EntryException("Số lượng hàng lỗi phải lớn hơn 0.");
            TimeOnly? at = string.IsNullOrWhiteSpace(request.Time) ? null
                : TimeOnly.TryParse(request.Time, CultureInfo.InvariantCulture, out var t) ? t : throw new EntryException($"Giờ \"{request.Time}\" không đúng.");
            var old = data.Defects.FirstOrDefault(d => d.Id == request.Id);
            var row = new DefectRow
            {
                Id = request.Id, Date = request.Date, Time = at, LineCode = line.Code, DefectType = request.DefectType?.Trim(),
                Quantity = request.Quantity, Note = request.Note, ImageFiles = old?.ImageFiles,
            };
            if (old is null)
                Store.AddDefect(row, user.Name);
            else
                Store.UpdateDefect(row, user.Name);
            return Day(Store.Load(), request.Date);
        }));

        app.MapDelete("/api/quanly/defect/{id:long}", (HttpContext http, long id) => Run(http, user =>
        {
            var date = Store.Load().Defects.FirstOrDefault(d => d.Id == id)?.Date ?? throw new EntryException("Phiếu hàng lỗi không còn.");
            Store.DeleteDefect(id, user.Name);
            return Day(Store.Load(), date);
        }));

        app.MapGet("/api/quanly/targets", (HttpContext http, DateOnly month) => Run(http, _ => Targets(Store.Load(), FirstDay(month))));

        app.MapPost("/api/quanly/targets", (HttpContext http, TargetsRequest request) => Run(http, user =>
        {
            var month = FirstDay(request.Month);
            var data = Store.Load();
            var targets = new List<MonthTarget>();
            foreach (var t in request.Targets ?? [])
            {
                if (string.IsNullOrWhiteSpace(t.ProductCode))
                    continue;
                var code = data.Products.FirstOrDefault(p => ProductionData.Same(p.Code, t.ProductCode))?.Code
                    ?? throw new EntryException($"Không có mã sản phẩm \"{t.ProductCode}\".");
                if (t.Target < 0)
                    throw new EntryException("Mục tiêu tháng không được âm.");
                targets.Add(new MonthTarget(month, code, t.Target, t.Note));
            }
            Store.SaveMonthTargets(month, targets, user.Name);
            return Targets(Store.Load(), month);
        }));

        app.MapPost("/api/quanly/lines", (HttpContext http, List<LineDef> lines) => Run(http, user =>
        {
            Unique(lines.Select(l => l.Code), "mã chuyền");
            Unique(lines.Select(l => l.Name), "tên chuyền");
            Store.SaveLines(lines.Select(l => l with { Code = Need(l.Code, "mã chuyền"), Name = Need(l.Name, "tên chuyền") }).ToList(), user.Name);
            return Catalog(Store.Load());
        }));

        app.MapPost("/api/quanly/products", (HttpContext http, List<ProductDef> products) => Run(http, user =>
        {
            Unique(products.Select(p => p.Code), "mã sản phẩm");
            Store.SaveProducts(products.Select(p => p with { Code = Need(p.Code, "mã sản phẩm") }).ToList(), user.Name);
            return Catalog(Store.Load());
        }));

        app.MapPost("/api/quanly/shifts", (HttpContext http, List<ShiftItem> shifts) => Run(http, user =>
        {
            Unique(shifts.Select(s => s.Code), "mã ca");
            Store.SaveShifts(shifts.Select(ToShift).ToList(), user.Name);
            return Catalog(Store.Load());
        }));

        app.MapPost("/api/quanly/reasons", (HttpContext http, List<ListItem> items) => Run(http, user =>
        {
            Store.SaveReasons(Items(items), user.Name);
            return Catalog(Store.Load());
        }));

        app.MapPost("/api/quanly/defect-types", (HttpContext http, List<ListItem> items) => Run(http, user =>
        {
            Store.SaveDefectTypes(Items(items), user.Name);
            return Catalog(Store.Load());
        }));

        app.MapPost("/api/quanly/calendar", (HttpContext http, List<CalendarDay> days) => Run(http, user =>
        {
            if (days.FirstOrDefault(d => d.Kind is not (CalendarKinds.Off or CalendarKinds.Extra)) is { } bad)
                throw new EntryException($"Ngày {bad.Date:dd/MM/yyyy}: loại phải là \"{CalendarKinds.Off}\" hoặc \"{CalendarKinds.Extra}\".");
            if (days.GroupBy(d => d.Date).FirstOrDefault(g => g.Count() > 1) is { } twice)
                throw new EntryException($"Ngày {twice.Key:dd/MM/yyyy} có hai dòng.");
            Store.SaveCalendar(days, user.Name);
            return Catalog(Store.Load());
        }));

        app.MapPost("/api/quanly/display-date", (HttpContext http, DisplayDateRequest request) => Run(http, user =>
        {
            Store.SetDisplayDateOverride(request.Date, user.Name);
            return Catalog(Store.Load());
        }));

        app.MapGet("/api/quanly/audit", (HttpContext http, CancellationToken _) => Run(http, _ => Store.Audit(300)));

        app.MapGet("/api/quanly/export", (HttpContext http, DateOnly? date, DateOnly? month) =>
        {
            if (Manager(http) is null)
                return Error("Cần đăng nhập bằng PIN của quản lý.", StatusCodes.Status401Unauthorized);
            try
            {
                var file = month is { } m ? maintenance.ExportMonth(m) : maintenance.ExportDay(date ?? Today());
                return Results.File(file.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.FileName);
            }
            catch (InvalidOperationException ex)
            {
                return Error(ex.Message, StatusCodes.Status400BadRequest);
            }
        });

        // Nhập file Excel 3.x: bước 1 đọc và so số (chưa ghi), bước 2 xác nhận thì thay toàn bộ dữ liệu.
        app.MapPost("/api/quanly/import", (HttpContext http, CancellationToken ct) => Run(http, async user =>
        {
            if (!http.Request.HasFormContentType)
                throw new EntryException("Chưa chọn file Excel.");
            var form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
            var file = form.Files.FirstOrDefault() ?? throw new EntryException("Chưa chọn file Excel.");
            var path = Path.Combine(Path.GetTempPath(), $"displayboard-import-{Guid.NewGuid():N}.xlsx");
            await using (var stream = File.Create(path))
                await file.CopyToAsync(stream, http.RequestAborted).ConfigureAwait(false);
            ImportPreview preview;
            try
            {
                preview = DataMaintenance.PreviewImport(path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new EntryException($"Không đọc được file {file.FileName}: {ex.Message}");
            }
            finally
            {
                File.Delete(path);
            }
            preview = preview with { Path = file.FileName };
            foreach (var old in _imports.Where(i => i.Value.At + PreviewLifetime < time.GetUtcNow()).Select(i => i.Key).ToList())
                _imports.TryRemove(old, out _);
            var id = Guid.NewGuid().ToString("N");
            _imports[id] = (preview, time.GetUtcNow());
            return ImportSummary(id, preview);
        }));

        app.MapPost("/api/quanly/import/{id}", (HttpContext http, string id) => Run(http, user =>
        {
            if (!_imports.TryRemove(id, out var item))
                throw new EntryException("Bản đọc file đã hết hạn. Chọn lại file.");
            maintenance.ApplyImport(item.Preview, user.Name);
            return Catalog(Store.Load());
        }));
    }

    // ---------- Dữ liệu trả về ----------

    private object Catalog(ProductionData data) => new
    {
        Today = Today(),
        DisplayDate = DisplayCalculator.DisplayDate(data),
        data.DisplayDateOverride,
        Lines = data.Lines,
        Products = data.Products,
        Shifts = data.Shifts.Select(s => new
        {
            s.Code, s.Name, s.Active, s.Hours,
            Periods = s.Periods.Select(p => $"{p.Start:HH\\:mm}-{p.End:HH\\:mm}"),
        }),
        data.Reasons,
        data.DefectTypes,
        Calendar = data.Calendar.OrderByDescending(c => c.Date),
        Months = data.Entries.Select(e => FirstDay(e.Date)).Append(FirstDay(Today())).Distinct().OrderDescending(),
        maintenance.Database.FilePath,
    };

    private static object Day(ProductionData data, DateOnly date)
    {
        var sheet = DisplayCalculator.Compute(data with { Lines = data.Lines.Select(l => l with { Active = true }).ToList() }, date);
        return new
        {
            Date = date,
            Lines = data.Lines.Select(l =>
            {
                var e = DisplayCalculator.Entry(data, date, l.Code);
                var shown = sheet.Lines.FirstOrDefault(r => r.Line == l.Name);
                return new
                {
                    l.Code, l.Name, l.Active,
                    Entry = e is null ? null : new
                    {
                        e.ProductCode, e.ShiftCode, e.HourlyTarget, e.Hours, e.Workers, e.Reason, e.DowntimeMinutes, e.Status, e.Note,
                        DailyTarget = DisplayCalculator.DailyTarget(data, e),
                        Actual = e.HoursTotal,
                        Slots = data.Shifts.FirstOrDefault(s => ProductionData.Same(s.Code, e.ShiftCode))?.Slots()
                            .Select(s => $"{s.Start:HH\\:mm}-{s.End:HH\\:mm}"),
                    },
                    shown?.DailyRate,
                    shown?.Defects,
                };
            }),
            Defects = data.Defects.Where(d => d.Date == date).OrderBy(d => d.Id).Select(d => new
            {
                d.Id, d.Date, Time = d.Time?.ToString("HH:mm", CultureInfo.InvariantCulture), Line = data.LineName(d.LineCode),
                d.DefectType, d.Quantity, d.ImageFiles, d.Note,
            }),
        };
    }

    private static object Targets(ProductionData data, DateOnly month) => new
    {
        Month = month,
        Targets = data.MonthTargets.Where(t => t.Month == month).Select(t => new
        {
            t.ProductCode, t.Target, t.Note,
            Done = DisplayCalculator.ProductOutput(data, t.ProductCode, month, month.AddMonths(1).AddDays(-1)),
        }),
        WorkingDays = DisplayCalculator.WorkingDays(data.Calendar, month, month.AddMonths(1).AddDays(-1)),
    };

    private static object ImportSummary(string id, ImportPreview preview)
    {
        var data = preview.Result.Data;
        return new
        {
            Id = id,
            File = preview.Path,
            Lines = data.Lines.Count,
            Products = data.Products.Count,
            Entries = data.Entries.Count,
            Defects = data.Defects.Count,
            From = data.Entries.Count == 0 ? (DateOnly?)null : data.Entries.Min(e => e.Date),
            To = data.Entries.Count == 0 ? (DateOnly?)null : data.Entries.Max(e => e.Date),
            preview.Result.Warnings,
            preview.Check.HasExcelValues,
            preview.Check.Mismatches,
            Rows = preview.Check.Rows.Where(r => !r.Matches).Take(50),
            preview.Check.DisplayDifferences,
            Checked = preview.Check.Rows.Count,
        };
    }

    // ---------- Phụ ----------

    private EntryUser? Manager(HttpContext http) => auth.Authenticate(http) is { Manager: true } user ? user : null;

    private Task<IResult> Run(HttpContext http, Func<EntryUser, object> action) => Run(http, user => Task.FromResult(action(user)));

    private async Task<IResult> Run(HttpContext http, Func<EntryUser, Task<object>> action)
    {
        var user = auth.Authenticate(http);
        if (user is null)
            return Error("Phiên đăng nhập đã hết. Nhập lại mã PIN.", StatusCodes.Status401Unauthorized);
        if (!user.Manager)
            return Error($"{user.Name} không phải quản lý. Trang này chỉ dành cho quản lý.", StatusCodes.Status403Forbidden);
        try
        {
            var result = await action(user).ConfigureAwait(false);
            return Results.Text(JsonSerializer.Serialize(result, BoardServer.Json), "application/json; charset=utf-8");
        }
        catch (Exception ex) when (ex is EntryException or InvalidOperationException or ArgumentException)
        {
            return Error(ex.Message, StatusCodes.Status400BadRequest);
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    private static DateOnly FirstDay(DateOnly d) => new(d.Year, d.Month, 1);

    private static string Need(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new EntryException($"Có dòng chưa nhập {what}.") : value.Trim();

    private static void Unique(IEnumerable<string?> values, string what)
    {
        if (values.Where(v => !string.IsNullOrWhiteSpace(v)).GroupBy(v => v!.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new EntryException($"Trùng {what} \"{twice.Key}\".");
    }

    private static List<ListItem> Items(List<ListItem> items)
    {
        var list = items.Where(i => !string.IsNullOrWhiteSpace(i.Name)).Select(i => i with { Name = i.Name.Trim() }).ToList();
        Unique(list.Select(i => i.Name), "tên");
        return list;
    }

    /// <summary>Ca gửi lên dạng "07:30-11:30" cho từng đợt (tối đa 3 đợt).</summary>
    private static ShiftDef ToShift(ShiftItem item)
    {
        var code = Need(item.Code, "mã ca");
        var periods = new List<WorkPeriod>();
        foreach (var text in (item.Periods ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Take(3))
        {
            var parts = text!.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !TimeOnly.TryParse(parts[0], CultureInfo.InvariantCulture, out var start)
                || !TimeOnly.TryParse(parts[1], CultureInfo.InvariantCulture, out var end) || end <= start)
                throw new EntryException($"Ca {code}: đợt \"{text}\" phải dạng 07:30-11:30.");
            periods.Add(new WorkPeriod(start, end));
        }
        if (periods.Count == 0)
            throw new EntryException($"Ca {code} chưa có giờ làm.");
        return new ShiftDef(code, string.IsNullOrWhiteSpace(item.Name) ? null : item.Name.Trim(), periods, item.Active);
    }

    private static IResult Error(string message, int status) =>
        Results.Json(new { Error = message }, BoardServer.Json, statusCode: status);
}
