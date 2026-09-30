using DisplayBoard.Core.Models;
using DisplayBoard.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisplayBoard.Tests;

public sealed class ConfigurationServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"displayboard-{Guid.NewGuid():N}");

    private ConfigurationService CreateService() =>
        new(Path.Combine(_directory, ConfigurationService.FileName), NullLogger<ConfigurationService>.Instance);

    [Fact]
    public async Task Missing_file_gives_defaults()
    {
        var configuration = await CreateService().LoadAsync();

        Assert.Equal(DisplayConfiguration.Default, configuration);
        Assert.Equal(800, configuration.DebounceMilliseconds);
    }

    [Fact]
    public async Task Round_trips_configuration()
    {
        var service = CreateService();
        var saved = new DisplayConfiguration
        {
            ExcelFile = @"D:\Data\SanLuong.xlsx",
            DisplayMode = DisplayMode.Independent,
            Screens = [new ScreenAssignment("DISPLAY2", "overview"), new ScreenAssignment("DISPLAY3", "ranking")],
        };

        await service.SaveAsync(saved);
        var loaded = await service.LoadAsync();

        Assert.Equal(saved.ExcelFile, loaded.ExcelFile);
        Assert.Equal(DisplayMode.Independent, loaded.DisplayMode);
        Assert.Equal(saved.Screens, loaded.Screens);
    }

    [Fact]
    public async Task Reads_the_json_shape_from_the_spec()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, ConfigurationService.FileName), """
            {
              "excelFile": "D:\\Data\\SanLuong.xlsx",
              "displayMode": "Mirror",
              "autoReload": true,
              "debounceMilliseconds": 800,
              "screens": [
                { "monitorId": "DISPLAY2", "viewId": "overview" },
                { "monitorId": "DISPLAY3", "viewId": "overview" }
              ]
            }
            """);

        var loaded = await CreateService().LoadAsync();

        Assert.Equal(@"D:\Data\SanLuong.xlsx", loaded.ExcelFile);
        Assert.Equal(DisplayMode.Mirror, loaded.DisplayMode);
        Assert.Equal(2, loaded.Screens.Count);
    }

    [Fact]
    public async Task Corrupt_file_falls_back_to_defaults()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, ConfigurationService.FileName), "{ not json");

        Assert.Equal(DisplayConfiguration.Default, await CreateService().LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
