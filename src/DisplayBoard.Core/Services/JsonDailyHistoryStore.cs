using System.Text.Json;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Services;

/// <summary>Lưu lịch sử ở %AppData%\DisplayBoard\daily-history.json, giữ <see cref="KeepDays"/> ngày gần nhất.</summary>
public sealed class JsonDailyHistoryStore : IDailyHistoryStore
{
    public const int KeepDays = 62;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _path;
    private readonly ILogger<JsonDailyHistoryStore> _logger;
    private readonly object _lock = new();
    private SortedDictionary<DateOnly, DayHistory>? _days;

    public JsonDailyHistoryStore(ILogger<JsonDailyHistoryStore> logger, string? path = null)
    {
        _logger = logger;
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayBoard", "daily-history.json");
    }

    public void Save(DayHistory day)
    {
        lock (_lock)
        {
            var days = Days();
            if (days.TryGetValue(day.Date, out var existing) && existing.Products.SequenceEqual(day.Products))
                return;
            days[day.Date] = day;
            while (days.Count > KeepDays)
                days.Remove(days.Keys.First());
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(days.Values, Json));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Không lưu được thì vẫn giữ trong bộ nhớ, lần đọc sau thử lại.
                _logger.LogError(ex, "Không lưu được lịch sử sản lượng {Path}", _path);
            }
        }
    }

    public DayHistory? GetPreviousDay(DateOnly date)
    {
        lock (_lock)
        {
            return Days().Where(d => d.Key < date).Select(d => d.Value).LastOrDefault();
        }
    }

    private SortedDictionary<DateOnly, DayHistory> Days()
    {
        if (_days is not null)
            return _days;
        _days = [];
        try
        {
            if (File.Exists(_path))
            {
                foreach (var day in JsonSerializer.Deserialize<List<DayHistory>>(File.ReadAllText(_path), Json) ?? [])
                    _days[day.Date] = day;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Không đọc được lịch sử sản lượng {Path}, bắt đầu lại từ trống", _path);
        }
        return _days;
    }
}
