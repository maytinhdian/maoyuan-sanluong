using DisplayBoard.Core.Configuration;
using DisplayBoard.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisplayBoard.Tests.Configuration;

public sealed class ConfigurationServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DisplayBoardTests", Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_directory, ConfigurationOptions.FileName);

    private ConfigurationService CreateService() =>
        new(new ConfigurationOptions { FilePath = ConfigPath }, NullLogger<ConfigurationService>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_WhenFileMissing_ReturnsDefaults()
    {
        var configuration = await CreateService().LoadAsync();

        Assert.Null(configuration.ExcelFile);
        Assert.Equal(DisplayMode.Mirror, configuration.DisplayMode);
        Assert.True(configuration.AutoReload);
        Assert.Equal(800, configuration.DebounceMilliseconds);
        Assert.Empty(configuration.Screens);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var service = CreateService();
        var saved = new DisplayConfiguration
        {
            ExcelFile = @"D:\Data\SanLuong.xlsx",
            DisplayMode = DisplayMode.Independent,
            AutoReload = false,
            DebounceMilliseconds = 1000,
            Screens = [new("DISPLAY2", "overview"), new("DISPLAY3", "ranking")],
        };

        await service.SaveAsync(saved);
        var loaded = await service.LoadAsync();

        Assert.Equal(saved.ExcelFile, loaded.ExcelFile);
        Assert.Equal(saved.DisplayMode, loaded.DisplayMode);
        Assert.Equal(saved.AutoReload, loaded.AutoReload);
        Assert.Equal(saved.DebounceMilliseconds, loaded.DebounceMilliseconds);
        Assert.Equal(saved.Screens, loaded.Screens);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_WritesSpecJsonShape()
    {
        await CreateService().SaveAsync(new DisplayConfiguration
        {
            ExcelFile = @"D:\Data\SanLuong.xlsx",
            Screens = [new("DISPLAY2", "overview")],
        });

        var json = await File.ReadAllTextAsync(ConfigPath);

        Assert.Contains("\"excelFile\"", json);
        Assert.Contains("\"displayMode\": \"Mirror\"", json);
        Assert.Contains("\"debounceMilliseconds\": 800", json);
        Assert.Contains("\"monitorId\": \"DISPLAY2\"", json);
        Assert.Contains("\"viewId\": \"overview\"", json);
    }

    [Fact]
    public async Task LoadAsync_ReadsSampleFromSpec()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(ConfigPath, """
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

        var configuration = await CreateService().LoadAsync();

        Assert.Equal(@"D:\Data\SanLuong.xlsx", configuration.ExcelFile);
        Assert.Equal(DisplayMode.Mirror, configuration.DisplayMode);
        Assert.Equal(2, configuration.Screens.Count);
        Assert.Equal(new ScreenAssignment("DISPLAY3", "overview"), configuration.Screens[1]);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("")]
    public async Task LoadAsync_WhenFileInvalid_ReturnsDefaults(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(ConfigPath, content);

        var configuration = await CreateService().LoadAsync();

        Assert.Same(DisplayConfiguration.Default, configuration);
    }

    [Fact]
    public async Task LoadAsync_NormalizesBadValues()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(ConfigPath, """
            { "excelFile": "  ", "debounceMilliseconds": -5,
              "screens": [ { "monitorId": "", "viewId": "overview" }, { "monitorId": "DISPLAY2", "viewId": "ranking" } ] }
            """);

        var configuration = await CreateService().LoadAsync();

        Assert.Null(configuration.ExcelFile);
        Assert.Equal(DisplayConfiguration.DefaultDebounceMilliseconds, configuration.DebounceMilliseconds);
        Assert.Equal([new ScreenAssignment("DISPLAY2", "ranking")], configuration.Screens);
    }
}
