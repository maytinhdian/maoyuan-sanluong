using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DisplayBoard.Core.Data;

/// <summary>Một dòng nhật ký: ai sửa gì, lúc nào, số cũ và số mới.</summary>
public sealed record AuditEntry(long Id, DateTimeOffset At, string User, string Action, string? Before, string? After);

/// <summary>Thông tin thêm của một chuyền trong ngày (các cột nhập tay ngoài kế hoạch và sản lượng giờ).</summary>
public sealed record DayDetails(decimal? Workers, string? Reason, decimal? DowntimeMinutes, string? Status, string? Note);

/// <summary>
/// Cơ sở dữ liệu SQLite của bản 4.x (file sanluong.db). Chỉ chương trình trên máy chủ mở file này; mọi lần ghi
/// đi qua đây, chạy lần lượt trong transaction và ghi nhật ký. Chế độ WAL nên đọc không phải chờ ghi.
/// Dữ liệu nhỏ nên <see cref="Load"/> đọc hết vào bộ nhớ và giữ lại đến lần ghi sau.
/// </summary>
public sealed class ProductionStore : IDisposable
{
    public const string DefaultFileName = "sanluong.db";
    private const int SchemaVersion = 1;

    private readonly SqliteConnection _db;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private ProductionData? _cache;

    public ProductionStore(string path, TimeProvider? time = null)
    {
        FilePath = Path.GetFullPath(path);
        _time = time ?? TimeProvider.System;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FilePath, Pooling = false }.ToString());
        _db.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        Migrate();
    }

    public string FilePath { get; }

    /// <summary>Tăng sau mỗi lần ghi.</summary>
    public long Version { get; private set; }

    /// <summary>Phát sau mỗi lần ghi thành công (TV đọc lại, web cập nhật).</summary>
    public event EventHandler? Changed;

    // ---------- Đọc ----------

    public ProductionData Load()
    {
        lock (_lock)
            return _cache ??= ReadAll();
    }

    public bool IsEmpty => Load() is { Lines.Count: 0, Entries.Count: 0, Shifts.Count: 0 };

    public IReadOnlyList<AuditEntry> Audit(int max = 200)
    {
        lock (_lock)
        {
            using var cmd = Command("SELECT id, luc, nguoi, viec, truoc, sau FROM nhat_ky ORDER BY id DESC LIMIT $max", ("$max", max));
            using var r = cmd.ExecuteReader();
            var list = new List<AuditEntry>();
            while (r.Read())
                list.Add(new AuditEntry(r.GetInt64(0), DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture), r.GetString(2), r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
            return list;
        }
    }

    // ---------- Ghi: nhập liệu hằng ngày ----------

    /// <summary>Kế hoạch của chuyền trong ngày (mã sản phẩm, ca, mục tiêu mỗi giờ). Chưa có dòng thì tạo.</summary>
    public void SavePlan(DateOnly date, string lineCode, string productCode, string shiftCode, decimal hourlyTarget, string user)
    {
        Write(user, $"Kế hoạch {lineCode} {Day(date)}", tx =>
        {
            var before = DescribeEntry(date, lineCode);
            Execute("""
                INSERT INTO nhap_lieu(ngay, ma_chuyen, ma_san_pham, ma_ca, muc_tieu_gio) VALUES($ngay, $chuyen, $sp, $ca, $mt)
                ON CONFLICT(ngay, ma_chuyen) DO UPDATE SET ma_san_pham = excluded.ma_san_pham, ma_ca = excluded.ma_ca, muc_tieu_gio = excluded.muc_tieu_gio
                """, ("$ngay", Day(date)), ("$chuyen", lineCode), ("$sp", productCode), ("$ca", shiftCode), ("$mt", hourlyTarget));
            return (before, DescribeEntry(date, lineCode));
        });
    }

    /// <summary>Sản lượng một giờ (1..12). Null = xoá số đã nhập. Chuyền phải có kế hoạch ngày đó trước.</summary>
    public void SetHour(DateOnly date, string lineCode, int hour, decimal? quantity, string user)
    {
        if (hour is < 1 or > DayEntry.MaxHours)
            throw new ArgumentOutOfRangeException(nameof(hour));
        Write(user, $"Giờ {hour} {lineCode} {Day(date)}", _ =>
        {
            var before = Scalar($"SELECT gio_{hour} FROM nhap_lieu WHERE ngay = $ngay AND ma_chuyen = $chuyen", ("$ngay", Day(date)), ("$chuyen", lineCode));
            var changed = Execute($"UPDATE nhap_lieu SET gio_{hour} = $sl WHERE ngay = $ngay AND ma_chuyen = $chuyen",
                ("$ngay", Day(date)), ("$chuyen", lineCode), ("$sl", quantity));
            if (changed == 0)
                throw new InvalidOperationException($"Chuyền {lineCode} chưa có kế hoạch ngày {date:dd/MM/yyyy}.");
            return (Text(before), Text(quantity));
        });
    }

    public void SaveDayDetails(DateOnly date, string lineCode, DayDetails details, string user)
    {
        Write(user, $"Thông tin ngày {lineCode} {Day(date)}", _ =>
        {
            var before = DescribeEntry(date, lineCode);
            var changed = Execute("""
                UPDATE nhap_lieu SET so_cong_nhan = $cn, ly_do = $ld, phut_dung_may = $pd, trang_thai = $tt, ghi_chu = $gc
                WHERE ngay = $ngay AND ma_chuyen = $chuyen
                """, ("$ngay", Day(date)), ("$chuyen", lineCode), ("$cn", details.Workers), ("$ld", Clean(details.Reason)),
                ("$pd", details.DowntimeMinutes), ("$tt", Clean(details.Status)), ("$gc", Clean(details.Note)));
            if (changed == 0)
                throw new InvalidOperationException($"Chuyền {lineCode} chưa có kế hoạch ngày {date:dd/MM/yyyy}.");
            return (before, DescribeEntry(date, lineCode));
        });
    }

    /// <summary>Xoá cả dòng của chuyền trong ngày (kế hoạch và sản lượng các giờ).</summary>
    public void DeleteEntry(DateOnly date, string lineCode, string user)
    {
        Write(user, $"Xoá dòng {lineCode} {Day(date)}", _ =>
        {
            var before = DescribeEntry(date, lineCode);
            Execute("DELETE FROM nhap_lieu WHERE ngay = $ngay AND ma_chuyen = $chuyen", ("$ngay", Day(date)), ("$chuyen", lineCode));
            return (before, null);
        });
    }

    public long AddDefect(DefectRow row, string user)
    {
        long id = 0;
        Write(user, $"Hàng lỗi {row.LineCode} {Day(row.Date)}", _ =>
        {
            Execute("""
                INSERT INTO hang_loi(ngay, gio, ma_chuyen, loai_loi, so_luong, ten_file_anh, ghi_chu)
                VALUES($ngay, $gio, $chuyen, $loai, $sl, $anh, $gc)
                """, ("$ngay", Day(row.Date)), ("$gio", row.Time?.ToString("HH:mm", CultureInfo.InvariantCulture)), ("$chuyen", row.LineCode),
                ("$loai", Clean(row.DefectType)), ("$sl", row.Quantity), ("$anh", Clean(row.ImageFiles)), ("$gc", Clean(row.Note)));
            id = (long)Scalar("SELECT last_insert_rowid()")!;
            return (null, $"#{id} {row.DefectType} · {Text(row.Quantity)}" + (row.ImageFiles is null ? "" : $" · {row.ImageFiles}"));
        });
        return id;
    }

    public void UpdateDefect(DefectRow row, string user)
    {
        Write(user, $"Sửa hàng lỗi #{row.Id}", _ =>
        {
            var before = DescribeDefect(row.Id);
            var changed = Execute("""
                UPDATE hang_loi SET ngay = $ngay, gio = $gio, ma_chuyen = $chuyen, loai_loi = $loai, so_luong = $sl, ten_file_anh = $anh, ghi_chu = $gc
                WHERE id = $id
                """, ("$id", row.Id), ("$ngay", Day(row.Date)), ("$gio", row.Time?.ToString("HH:mm", CultureInfo.InvariantCulture)), ("$chuyen", row.LineCode),
                ("$loai", Clean(row.DefectType)), ("$sl", row.Quantity), ("$anh", Clean(row.ImageFiles)), ("$gc", Clean(row.Note)));
            if (changed == 0)
                throw new InvalidOperationException($"Không có dòng hàng lỗi #{row.Id}.");
            return (before, DescribeDefect(row.Id));
        });
    }

    public void DeleteDefect(long id, string user)
    {
        Write(user, $"Xoá hàng lỗi #{id}", _ =>
        {
            var before = DescribeDefect(id);
            Execute("DELETE FROM hang_loi WHERE id = $id", ("$id", id));
            return (before, null);
        });
    }

    // ---------- Ghi: danh mục ----------

    public void SaveShifts(IReadOnlyList<ShiftDef> shifts, string user) =>
        Write(user, "Danh sách ca", _ => ReplaceList("ca", () => InsertShifts(shifts), shifts.Count));

    public void SaveLines(IReadOnlyList<LineDef> lines, string user) =>
        Write(user, "Danh sách chuyền", _ =>
        {
            var used = Strings("SELECT DISTINCT ma_chuyen FROM nhap_lieu UNION SELECT DISTINCT ma_chuyen FROM hang_loi");
            var missing = used.Where(u => !lines.Any(l => ProductionData.Same(l.Code, u))).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"Chuyền {string.Join(", ", missing)} đã có số liệu nên không xoá được; hãy chuyển sang Ngừng sử dụng.");
            return ReplaceList("chuyen", () => InsertLines(lines), lines.Count);
        });

    public void SaveProducts(IReadOnlyList<ProductDef> products, string user) =>
        Write(user, "Danh sách sản phẩm", _ => ReplaceList("san_pham", () => InsertProducts(products), products.Count));

    public void SaveReasons(IReadOnlyList<ListItem> items, string user) =>
        Write(user, "Danh sách lý do không đạt", _ => ReplaceList("ly_do", () => InsertItems("ly_do", items), items.Count));

    public void SaveDefectTypes(IReadOnlyList<ListItem> items, string user) =>
        Write(user, "Danh sách loại lỗi", _ => ReplaceList("loai_loi", () => InsertItems("loai_loi", items), items.Count));

    public void SaveCalendar(IReadOnlyList<CalendarDay> days, string user) =>
        Write(user, "Lịch làm việc", _ => ReplaceList("lich_lam_viec", () => InsertCalendar(days), days.Count));

    /// <summary>Thay toàn bộ mục tiêu của một tháng.</summary>
    public void SaveMonthTargets(DateOnly month, IReadOnlyList<MonthTarget> targets, string user)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        Write(user, $"Mục tiêu tháng {first:MM/yyyy}", _ =>
        {
            var before = Count("SELECT COUNT(*) FROM muc_tieu_thang WHERE thang = $t", ("$t", Day(first)));
            Execute("DELETE FROM muc_tieu_thang WHERE thang = $t", ("$t", Day(first)));
            InsertMonthTargets(targets.Select(t => t with { Month = first }));
            return ($"{before} mã", $"{targets.Count} mã");
        });
    }

    /// <summary>Ngày TV hiển thị (ô J1 của HIEN_THI ở bản Excel). Null = ngày mới nhất có dữ liệu.</summary>
    public void SetDisplayDateOverride(DateOnly? date, string user) =>
        Write(user, "Ngày hiển thị", _ =>
        {
            var before = Scalar("SELECT gia_tri FROM cai_dat WHERE khoa = 'ngay_hien_thi'");
            Execute("INSERT INTO cai_dat(khoa, gia_tri) VALUES('ngay_hien_thi', $v) ON CONFLICT(khoa) DO UPDATE SET gia_tri = excluded.gia_tri",
                ("$v", date is null ? null : Day(date.Value)));
            return (Text(before) ?? "mới nhất", date is null ? "mới nhất" : Day(date.Value));
        });

    /// <summary>Thay toàn bộ dữ liệu (nhập từ file Excel V20). Nhật ký cũ giữ nguyên.</summary>
    public void ReplaceAll(ProductionData data, string user, string? source = null) =>
        Write(user, "Nhập toàn bộ dữ liệu" + (source is null ? "" : " từ " + source), _ =>
        {
            Execute("DELETE FROM hang_loi; DELETE FROM nhap_lieu; DELETE FROM muc_tieu_thang; DELETE FROM lich_lam_viec; DELETE FROM loai_loi; DELETE FROM ly_do; DELETE FROM san_pham; DELETE FROM chuyen; DELETE FROM ca; DELETE FROM cai_dat WHERE khoa = 'ngay_hien_thi';");
            InsertShifts(data.Shifts);
            InsertLines(data.Lines);
            InsertProducts(data.Products);
            InsertItems("ly_do", data.Reasons);
            InsertItems("loai_loi", data.DefectTypes);
            InsertCalendar(data.Calendar);
            InsertMonthTargets(data.MonthTargets);
            foreach (var e in data.Entries)
            {
                var args = new List<(string, object?)>
                {
                    ("$ngay", Day(e.Date)), ("$chuyen", e.LineCode), ("$sp", Clean(e.ProductCode)), ("$ca", Clean(e.ShiftCode)), ("$mt", e.HourlyTarget),
                    ("$tt", Clean(e.Status)), ("$gc", Clean(e.Note)), ("$cn", e.Workers), ("$ld", Clean(e.Reason)), ("$pd", e.DowntimeMinutes),
                };
                for (var h = 0; h < DayEntry.MaxHours; h++)
                    args.Add(($"$g{h + 1}", e.Hours.ElementAtOrDefault(h)));
                Execute($"""
                    INSERT INTO nhap_lieu(ngay, ma_chuyen, ma_san_pham, ma_ca, muc_tieu_gio, trang_thai, ghi_chu, so_cong_nhan, ly_do, phut_dung_may, {HourColumns})
                    VALUES($ngay, $chuyen, $sp, $ca, $mt, $tt, $gc, $cn, $ld, $pd, {string.Join(", ", Enumerable.Range(1, DayEntry.MaxHours).Select(h => $"$g{h}"))})
                    """, [.. args]);
            }
            foreach (var d in data.Defects)
                Execute("""
                    INSERT INTO hang_loi(ngay, gio, ma_chuyen, loai_loi, so_luong, ten_file_anh, ghi_chu)
                    VALUES($ngay, $gio, $chuyen, $loai, $sl, $anh, $gc)
                    """, ("$ngay", Day(d.Date)), ("$gio", d.Time?.ToString("HH:mm", CultureInfo.InvariantCulture)), ("$chuyen", d.LineCode),
                    ("$loai", Clean(d.DefectType)), ("$sl", d.Quantity), ("$anh", Clean(d.ImageFiles)), ("$gc", Clean(d.Note)));
            if (data.DisplayDateOverride is { } shown)
                Execute("INSERT INTO cai_dat(khoa, gia_tri) VALUES('ngay_hien_thi', $v)", ("$v", Day(shown)));
            return (null, $"{data.Lines.Count} chuyền, {data.Products.Count} sản phẩm, {data.Entries.Count} dòng nhập liệu, {data.Defects.Count} dòng hàng lỗi");
        });

    /// <summary>Chép cơ sở dữ liệu ra file khác khi đang chạy (dùng để sao lưu).</summary>
    public void Backup(string targetPath)
    {
        lock (_lock)
        {
            if (File.Exists(targetPath))
                File.Delete(targetPath);
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = targetPath, Pooling = false }.ToString());
            target.Open();
            _db.BackupDatabase(target);
        }
    }

    public void Dispose()
    {
        lock (_lock)
            _db.Dispose();
    }

    // ---------- Bên trong ----------

    private static readonly string HourColumns = string.Join(", ", Enumerable.Range(1, DayEntry.MaxHours).Select(h => $"gio_{h}"));

    private void Write(string user, string action, Func<SqliteTransaction, (string? Before, string? After)> change)
    {
        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            _tx = tx;
            try
            {
                var (before, after) = change(tx);
                Execute("INSERT INTO nhat_ky(luc, nguoi, viec, truoc, sau) VALUES($luc, $nguoi, $viec, $truoc, $sau)",
                    ("$luc", _time.GetLocalNow().ToString("o", CultureInfo.InvariantCulture)), ("$nguoi", string.IsNullOrWhiteSpace(user) ? "?" : user.Trim()),
                    ("$viec", action), ("$truoc", before), ("$sau", after));
                tx.Commit();
            }
            finally
            {
                _tx = null;
            }
            _cache = null;
            Version++;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private SqliteTransaction? _tx;

    private (string?, string?) ReplaceList(string table, Action insert, int count)
    {
        var before = Count($"SELECT COUNT(*) FROM {table}");
        Execute($"DELETE FROM {table}");
        insert();
        return ($"{before} dòng", $"{count} dòng");
    }

    private void InsertShifts(IEnumerable<ShiftDef> shifts)
    {
        var order = 0;
        foreach (var s in shifts)
        {
            string? P(int i, bool start) => i < s.Periods.Count ? (start ? s.Periods[i].Start : s.Periods[i].End).ToString("HH:mm", CultureInfo.InvariantCulture) : null;
            Execute("""
                INSERT INTO ca(ma, ten, dot1_bd, dot1_kt, dot2_bd, dot2_kt, dot3_bd, dot3_kt, dang_dung, thu_tu)
                VALUES($ma, $ten, $a, $b, $c, $d, $e, $f, $dd, $tt)
                """, ("$ma", s.Code.Trim()), ("$ten", Clean(s.Name)), ("$a", P(0, true)), ("$b", P(0, false)), ("$c", P(1, true)), ("$d", P(1, false)),
                ("$e", P(2, true)), ("$f", P(2, false)), ("$dd", s.Active), ("$tt", order++));
        }
    }

    private void InsertLines(IEnumerable<LineDef> lines)
    {
        var order = 0;
        foreach (var l in lines)
            Execute("INSERT INTO chuyen(ma, ten, truong_chuyen, dang_dung, thu_tu) VALUES($ma, $ten, $tc, $dd, $tt)",
                ("$ma", l.Code.Trim()), ("$ten", l.Name.Trim()), ("$tc", Clean(l.Leader)), ("$dd", l.Active), ("$tt", order++));
    }

    private void InsertProducts(IEnumerable<ProductDef> products)
    {
        var order = 0;
        foreach (var p in products)
            Execute("INSERT INTO san_pham(ma, ten, ghi_chu, dang_dung, thu_tu) VALUES($ma, $ten, $gc, $dd, $tt)",
                ("$ma", p.Code.Trim()), ("$ten", Clean(p.Name)), ("$gc", Clean(p.Note)), ("$dd", p.Active), ("$tt", order++));
    }

    private void InsertItems(string table, IEnumerable<ListItem> items)
    {
        var order = 0;
        foreach (var i in items)
            Execute($"INSERT INTO {table}(ten, ghi_chu, thu_tu) VALUES($ten, $gc, $tt)", ("$ten", i.Name.Trim()), ("$gc", Clean(i.Note)), ("$tt", order++));
    }

    private void InsertCalendar(IEnumerable<CalendarDay> days)
    {
        foreach (var d in days)
            Execute("INSERT INTO lich_lam_viec(ngay, loai, ghi_chu) VALUES($ngay, $loai, $gc)", ("$ngay", Day(d.Date)), ("$loai", d.Kind), ("$gc", Clean(d.Note)));
    }

    private void InsertMonthTargets(IEnumerable<MonthTarget> targets)
    {
        foreach (var t in targets)
            Execute("INSERT INTO muc_tieu_thang(thang, ma_san_pham, muc_tieu, ghi_chu) VALUES($t, $sp, $mt, $gc)",
                ("$t", Day(new DateOnly(t.Month.Year, t.Month.Month, 1))), ("$sp", t.ProductCode.Trim()), ("$mt", t.Target), ("$gc", Clean(t.Note)));
    }

    private string? DescribeEntry(DateOnly date, string lineCode)
    {
        using var cmd = Command($"SELECT ma_san_pham, ma_ca, muc_tieu_gio, so_cong_nhan, ly_do, phut_dung_may, trang_thai, ghi_chu, {HourColumns} FROM nhap_lieu WHERE ngay = $ngay AND ma_chuyen = $chuyen",
            ("$ngay", Day(date)), ("$chuyen", lineCode));
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        var parts = new List<string>();
        string? V(int i) => r.IsDBNull(i) ? null : Text(r.GetValue(i));
        parts.Add($"{V(0)} · ca {V(1)} · {V(2)}/giờ");
        var hours = Enumerable.Range(0, DayEntry.MaxHours).Select(h => V(8 + h) ?? "-").ToList();
        parts.Add("giờ: " + string.Join(" ", hours));
        if (V(3) is { } cn) parts.Add($"{cn} công nhân");
        if (V(4) is { } ld) parts.Add($"lý do: {ld}");
        if (V(5) is { } pd) parts.Add($"dừng {pd} phút");
        if (V(6) is { } tt) parts.Add(tt);
        if (V(7) is { } gc) parts.Add($"ghi chú: {gc}");
        return string.Join(" · ", parts);
    }

    private string? DescribeDefect(long id)
    {
        using var cmd = Command("SELECT ngay, gio, ma_chuyen, loai_loi, so_luong, ten_file_anh, ghi_chu FROM hang_loi WHERE id = $id", ("$id", id));
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return string.Join(" · ", Enumerable.Range(0, 7).Select(i => r.IsDBNull(i) ? null : Text(r.GetValue(i))).OfType<string>());
    }

    private ProductionData ReadAll()
    {
        var shifts = Rows("SELECT ma, ten, dot1_bd, dot1_kt, dot2_bd, dot2_kt, dot3_bd, dot3_kt, dang_dung FROM ca ORDER BY thu_tu, ma", r =>
        {
            var periods = new List<WorkPeriod>();
            for (var i = 0; i < 3; i++)
            {
                var start = OptTime(r, 2 + i * 2);
                var end = OptTime(r, 3 + i * 2);
                if (start is not null && end is not null)
                    periods.Add(new WorkPeriod(start.Value, end.Value));
            }
            return new ShiftDef(r.GetString(0), OptString(r, 1), periods, r.GetBoolean(8));
        });
        var lines = Rows("SELECT ma, ten, truong_chuyen, dang_dung FROM chuyen ORDER BY thu_tu, ma",
            r => new LineDef(r.GetString(0), r.GetString(1), OptString(r, 2), r.GetBoolean(3)));
        var products = Rows("SELECT ma, ten, ghi_chu, dang_dung FROM san_pham ORDER BY thu_tu, ma",
            r => new ProductDef(r.GetString(0), OptString(r, 1), OptString(r, 2), r.GetBoolean(3)));
        var reasons = Rows("SELECT ten, ghi_chu FROM ly_do ORDER BY thu_tu, ten", r => new ListItem(r.GetString(0), OptString(r, 1)));
        var defectTypes = Rows("SELECT ten, ghi_chu FROM loai_loi ORDER BY thu_tu, ten", r => new ListItem(r.GetString(0), OptString(r, 1)));
        var calendar = Rows("SELECT ngay, loai, ghi_chu FROM lich_lam_viec ORDER BY ngay",
            r => new CalendarDay(DateOnly.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), r.GetString(1), OptString(r, 2)));
        var targets = Rows("SELECT thang, ma_san_pham, muc_tieu, ghi_chu FROM muc_tieu_thang ORDER BY thang, rowid",
            r => new MonthTarget(DateOnly.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), r.GetString(1), (decimal)r.GetDouble(2), OptString(r, 3)));
        var entries = Rows($"SELECT ngay, ma_chuyen, ma_san_pham, ma_ca, muc_tieu_gio, trang_thai, ghi_chu, so_cong_nhan, ly_do, phut_dung_may, {HourColumns} FROM nhap_lieu ORDER BY ngay, ma_chuyen",
            r => new DayEntry
            {
                Date = DateOnly.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                LineCode = r.GetString(1),
                ProductCode = OptString(r, 2),
                ShiftCode = OptString(r, 3),
                HourlyTarget = OptNumber(r, 4),
                Status = OptString(r, 5),
                Note = OptString(r, 6),
                Workers = OptNumber(r, 7),
                Reason = OptString(r, 8),
                DowntimeMinutes = OptNumber(r, 9),
                Hours = Enumerable.Range(0, DayEntry.MaxHours).Select(h => OptNumber(r, 10 + h)).ToArray(),
            });
        var defects = Rows("SELECT id, ngay, gio, ma_chuyen, loai_loi, so_luong, ten_file_anh, ghi_chu FROM hang_loi ORDER BY ngay, id",
            r => new DefectRow
            {
                Id = r.GetInt64(0),
                Date = DateOnly.ParseExact(r.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Time = OptTime(r, 2),
                LineCode = r.GetString(3),
                DefectType = OptString(r, 4),
                Quantity = OptNumber(r, 5),
                ImageFiles = OptString(r, 6),
                Note = OptString(r, 7),
            });
        var shown = Scalar("SELECT gia_tri FROM cai_dat WHERE khoa = 'ngay_hien_thi'") as string;
        return new ProductionData
        {
            Shifts = shifts,
            Lines = lines,
            Products = products,
            Reasons = reasons,
            DefectTypes = defectTypes,
            Calendar = calendar,
            MonthTargets = targets,
            Entries = entries,
            Defects = defects,
            DisplayDateOverride = shown is null ? null : DateOnly.ParseExact(shown, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }

    private void Migrate()
    {
        var version = Convert.ToInt32(Scalar("PRAGMA user_version"), CultureInfo.InvariantCulture);
        if (version > SchemaVersion)
            throw new InvalidOperationException($"File dữ liệu {Path.GetFileName(FilePath)} được tạo bởi phiên bản mới hơn. Hãy cập nhật chương trình.");
        if (version == SchemaVersion)
            return;
        using var tx = _db.BeginTransaction();
        _tx = tx;
        Execute($"""
            CREATE TABLE ca(
                ma TEXT PRIMARY KEY COLLATE NOCASE, ten TEXT,
                dot1_bd TEXT, dot1_kt TEXT, dot2_bd TEXT, dot2_kt TEXT, dot3_bd TEXT, dot3_kt TEXT,
                dang_dung INTEGER NOT NULL DEFAULT 1, thu_tu INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE chuyen(
                ma TEXT PRIMARY KEY COLLATE NOCASE, ten TEXT NOT NULL UNIQUE COLLATE NOCASE, truong_chuyen TEXT,
                dang_dung INTEGER NOT NULL DEFAULT 1, thu_tu INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE san_pham(
                ma TEXT PRIMARY KEY COLLATE NOCASE, ten TEXT, ghi_chu TEXT,
                dang_dung INTEGER NOT NULL DEFAULT 1, thu_tu INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE ly_do(ten TEXT PRIMARY KEY COLLATE NOCASE, ghi_chu TEXT, thu_tu INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE loai_loi(ten TEXT PRIMARY KEY COLLATE NOCASE, ghi_chu TEXT, thu_tu INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE lich_lam_viec(ngay TEXT PRIMARY KEY, loai TEXT NOT NULL, ghi_chu TEXT);
            CREATE TABLE muc_tieu_thang(
                thang TEXT NOT NULL, ma_san_pham TEXT NOT NULL COLLATE NOCASE, muc_tieu REAL NOT NULL, ghi_chu TEXT,
                PRIMARY KEY(thang, ma_san_pham));
            CREATE TABLE nhap_lieu(
                ngay TEXT NOT NULL, ma_chuyen TEXT NOT NULL COLLATE NOCASE,
                ma_san_pham TEXT, ma_ca TEXT, muc_tieu_gio REAL,
                {string.Join(" ", Enumerable.Range(1, DayEntry.MaxHours).Select(h => $"gio_{h} REAL,"))}
                trang_thai TEXT, ghi_chu TEXT, so_cong_nhan REAL, ly_do TEXT, phut_dung_may REAL,
                PRIMARY KEY(ngay, ma_chuyen));
            CREATE TABLE hang_loi(
                id INTEGER PRIMARY KEY AUTOINCREMENT, ngay TEXT NOT NULL, gio TEXT, ma_chuyen TEXT NOT NULL COLLATE NOCASE,
                loai_loi TEXT, so_luong REAL, ten_file_anh TEXT, ghi_chu TEXT);
            CREATE INDEX hang_loi_ngay ON hang_loi(ngay, ma_chuyen);
            CREATE TABLE nhat_ky(id INTEGER PRIMARY KEY AUTOINCREMENT, luc TEXT NOT NULL, nguoi TEXT NOT NULL, viec TEXT NOT NULL, truoc TEXT, sau TEXT);
            CREATE TABLE cai_dat(khoa TEXT PRIMARY KEY, gia_tri TEXT);
            PRAGMA user_version = {SchemaVersion};
            """);
        tx.Commit();
        _tx = null;
    }

    // ---------- SQL ----------

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] args)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _tx;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value switch
            {
                null => DBNull.Value,
                decimal m => (double)m,
                bool b => b ? 1 : 0,
                _ => value
            });
        return cmd;
    }

    private int Execute(string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(sql, args);
        return cmd.ExecuteNonQuery();
    }

    private object? Scalar(string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(sql, args);
        var value = cmd.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    private long Count(string sql, params (string Name, object? Value)[] args) => Convert.ToInt64(Scalar(sql, args), CultureInfo.InvariantCulture);

    private List<string> Strings(string sql) => Rows(sql, r => r.GetString(0));

    private List<T> Rows<T>(string sql, Func<SqliteDataReader, T> map)
    {
        using var cmd = Command(sql);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read())
            list.Add(map(r));
        return list;
    }

    private static string? OptString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static decimal? OptNumber(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : (decimal)r.GetDouble(i);

    private static TimeOnly? OptTime(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : TimeOnly.ParseExact(r.GetString(i), "HH:mm", CultureInfo.InvariantCulture);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? Text(object? value) => value switch
    {
        null or DBNull => null,
        double d => ((decimal)d).ToString("0.##", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.##", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };
}
