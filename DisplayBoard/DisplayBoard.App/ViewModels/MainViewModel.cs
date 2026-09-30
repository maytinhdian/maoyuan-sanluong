using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Configuration;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.App.ViewModels;

public sealed partial class MainViewModel(IConfigurationService configurationService, ILogger<MainViewModel> logger)
    : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExcelFileDisplay))]
    private string? _excelFile;

    [ObservableProperty]
    private DisplayMode _displayMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private DataSourceStatus _status = DataSourceStatus.Ready;

    public string ExcelFileDisplay => ExcelFile ?? "(chưa chọn file)";

    public string StatusText => Status switch
    {
        DataSourceStatus.Ready => "Sẵn sàng",
        DataSourceStatus.Reading => "Đang đọc",
        DataSourceStatus.Updated => "Đã cập nhật",
        DataSourceStatus.FileInUse => "File đang được sử dụng",
        DataSourceStatus.InvalidData => "Dữ liệu không hợp lệ",
        DataSourceStatus.FileNotFound => "Không tìm thấy file",
        _ => Status.ToString(),
    };

    public string ConfigurationFile => configurationService.FilePath;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var configuration = await configurationService.LoadAsync(ct);
        ExcelFile = configuration.ExcelFile;
        DisplayMode = configuration.DisplayMode;
        logger.LogInformation("Main view model initialised (mode {DisplayMode}, excel file set: {HasFile})",
            DisplayMode, ExcelFile is not null);
    }
}
