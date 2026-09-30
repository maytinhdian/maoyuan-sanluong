using System.Text.Json;
using System.Text.Json.Serialization;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.Core.Services;

/// <summary>Lưu cấu hình ở %AppData%\DisplayBoard\display-config.json (không lưu vào Excel).</summary>
public sealed class ConfigurationService : IConfigurationService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly ILogger<ConfigurationService> _logger;

    public ConfigurationService(ILogger<ConfigurationService> logger, string? path = null)
    {
        _logger = logger;
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayBoard", "display-config.json");
        Current = Load();
    }

    public DisplayConfiguration Current { get; private set; }

    public DisplayConfiguration Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var config = JsonSerializer.Deserialize<DisplayConfiguration>(File.ReadAllText(_path), Json);
                if (config is not null)
                    return Current = config;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Không đọc được cấu hình {Path}, dùng cấu hình mặc định", _path);
        }
        return Current = new DisplayConfiguration();
    }

    public void Save(DisplayConfiguration configuration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Ghi file tạm rồi thay thế để không làm hỏng cấu hình nếu app tắt giữa chừng.
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(configuration, Json));
        File.Move(temp, _path, overwrite: true);
        Current = configuration;
        _logger.LogInformation("Đã lưu cấu hình {Path}", _path);
    }
}
