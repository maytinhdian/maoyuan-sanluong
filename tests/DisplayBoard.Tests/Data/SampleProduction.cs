using DisplayBoard.Core.Data;

namespace DisplayBoard.Tests.Data;

/// <summary>Dữ liệu thử hai tháng (09–10/2026) đủ các trường hợp khó: ca 11H30 lẻ giờ, ngày thiếu dòng, mã toàn số, lịch nghỉ/làm bù.</summary>
internal static class SampleProduction
{
    public static readonly DateOnly LastDay = new(2026, 10, 14);

    public static ProductionData Create(int seed = 7)
    {
        var random = new Random(seed);
        var shifts = new List<ShiftDef>
        {
            new("8H", "8 giờ", [new(new(7, 30), new(11, 30)), new(new(12, 30), new(16, 30))]),
            new("9H", "9 giờ", [new(new(7, 30), new(11, 30)), new(new(12, 30), new(17, 30))]),
            new("10H", "10 giờ", [new(new(7, 30), new(11, 30)), new(new(12, 30), new(18, 30))]),
            new("11H30", "11 giờ 30 phút", [new(new(7, 30), new(11, 30)), new(new(12, 30), new(16, 30)), new(new(17, 0), new(20, 30))]),
            new("6H", "Ca cũ", [new(new(7, 0), new(13, 0))], Active: false),
        };
        var lines = Enumerable.Range(1, 6).Select(i => new LineDef($"CH0{i}", $"Chuyền {i}", i == 2 ? "Chị Lan" : null)).ToList();
        lines.Insert(3, new LineDef("CH07", "Chuyền 7", Active: false));   // chuyền ngừng: không hiện trên TV
        var products = new List<ProductDef>
        {
            new("ĐAI LƯNG", "Đai lưng"), new("BAO TAY XANH"), new("883"), new("567", Note: "Test"), new("MÃ 9H"), new("CŨ", Active: false),
        };
        var calendar = new List<CalendarDay>
        {
            new(new(2026, 9, 2), CalendarKinds.Off, "Quốc khánh"),
            new(new(2026, 9, 13), CalendarKinds.Extra, "Làm bù CN"),
            new(new(2026, 10, 10), CalendarKinds.Off),
            new(new(2026, 10, 18), CalendarKinds.Extra),
            new(new(2026, 10, 25), CalendarKinds.Off, "CN ghi nghỉ: không đổi gì"),
        };
        var targets = new List<MonthTarget>
        {
            new(new(2026, 9, 1), "ĐAI LƯNG", 21000), new(new(2026, 9, 1), "BAO TAY XANH", 30000), new(new(2026, 9, 1), "883", 40000),
            new(new(2026, 9, 1), "567", 5000),
            new(new(2026, 10, 1), "ĐAI LƯNG", 26000, "Tháng 10"), new(new(2026, 10, 1), "BAO TAY XANH", 22000), new(new(2026, 10, 1), "883", 52000),
            new(new(2026, 10, 1), "MÃ 9H", 9000),
        };
        string[] lineProducts = ["ĐAI LƯNG", "BAO TAY XANH", "883", "567", "MÃ 9H", "883"];
        string[] lineShifts = ["8H", "10H", "11H30", "9H", "9H", "10H"];
        string[] reasons = ["Thiếu vật tư", "Hư máy", "Thiếu người"];
        string[] defectTypes = ["Rách đường may", "Bung chỉ", "Lem màu"];

        var entries = new List<DayEntry>();
        var defects = new List<DefectRow>();
        for (var day = new DateOnly(2026, 9, 1); day <= LastDay; day = day.AddDays(1))
        {
            var working = DisplayCalculator.WorkingDays(calendar, day, day) == 1;
            for (var i = 0; i < 6; i++)
            {
                var line = lines.First(l => l.Code == $"CH0{i + 1}");
                if (!working && !(i == 0 && day.DayOfWeek == DayOfWeek.Sunday && day.Day == 4))
                    continue;
                if (random.NextDouble() < 0.08)
                    continue;   // chuyền nghỉ ngày này: ngày làm trước phải nhảy qua
                var product = random.NextDouble() < 0.15 ? products[random.Next(5)].Code : lineProducts[i];
                var shift = random.NextDouble() < 0.1 ? lineShifts[(i + 1) % 6] : lineShifts[i];
                var shiftDef = shifts.First(s => s.Code == shift);
                decimal? hourlyTarget = random.Next(8, 25) * 10 + (i == 2 ? 5 : 0);
                if (random.NextDouble() < 0.03)
                    hourlyTarget = null;      // thiếu mục tiêu giờ
                var slots = (int)Math.Ceiling(shiftDef.Hours);
                var filled = day == LastDay ? random.Next(0, slots) : random.NextDouble() < 0.05 ? 0 : slots;
                var hours = new decimal?[DayEntry.MaxHours];
                for (var h = 0; h < filled; h++)
                    hours[h] = hourlyTarget is null ? random.Next(80, 120) : Math.Round(hourlyTarget.Value * (decimal)(0.7 + random.NextDouble() * 0.45));
                if (filled > 3 && random.NextDouble() < 0.1)
                    hours[1] = null;          // bỏ trống một giờ giữa ca
                entries.Add(new DayEntry
                {
                    Date = day,
                    LineCode = line.Code,
                    ProductCode = random.NextDouble() < 0.02 ? null : product,
                    ShiftCode = random.NextDouble() < 0.02 ? "KHÔNG CÓ" : shift,
                    HourlyTarget = hourlyTarget,
                    Hours = hours,
                    Status = random.NextDouble() < 0.5 ? "Đang chạy" : null,
                    Note = random.NextDouble() < 0.1 ? "Ghi chú \"thử\" & <xml>" : null,
                    Workers = random.NextDouble() < 0.8 ? random.Next(10, 30) : null,
                    Reason = random.NextDouble() < 0.2 ? reasons[random.Next(reasons.Length)] : null,
                    DowntimeMinutes = random.NextDouble() < 0.2 ? random.Next(5, 90) : null,
                });
                var defectCount = random.NextDouble() < 0.3 ? random.Next(1, 3) : 0;
                for (var k = 0; k < defectCount; k++)
                    defects.Add(new DefectRow
                    {
                        Id = defects.Count + 1,
                        Date = day,
                        Time = new TimeOnly(8 + random.Next(9), random.Next(60)),
                        LineCode = line.Code,
                        DefectType = defectTypes[random.Next(defectTypes.Length)],
                        Quantity = random.Next(1, 15),
                        ImageFiles = random.NextDouble() < 0.5 ? $"ch{i + 1}_{day:yyyyMMdd}_{k + 1}.jpg" : random.NextDouble() < 0.3 ? "a.jpg; b.png" : null,
                        Note = random.NextDouble() < 0.2 ? "QC" : null,
                    });
            }
        }
        // Hàng lỗi của chuyền không có dòng nhập liệu ngày đó (mã sản phẩm trống).
        defects.Add(new DefectRow { Id = defects.Count + 1, Date = LastDay, Time = new(9, 5), LineCode = "CH07", DefectType = "Lem màu", Quantity = 2 });

        return new ProductionData
        {
            Shifts = shifts,
            Lines = lines,
            Products = products,
            Reasons = reasons.Select(r => new ListItem(r)).ToList(),
            DefectTypes = defectTypes.Select(r => new ListItem(r, r == "Lem màu" ? "Màu" : null)).ToList(),
            Calendar = calendar,
            MonthTargets = targets,
            Entries = entries,
            Defects = defects,
        };
    }
}
