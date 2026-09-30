using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayBoard.App.Services;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;
using Microsoft.Extensions.Logging;

namespace DisplayBoard.App.ViewModels;

public sealed partial class MainWindowViewModel(
    IExcelDataReader reader,
    IDataProcessor processor,
    IConfigurationService configurationService,
    IFileDialogService fileDialog,
    ILogger<MainWindowViewModel> logger) : ObservableObject
{
    private DisplayConfiguration _configuration = DisplayConfiguration.Default;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private string? _excelFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private DataStatus _status = DataStatus.Ready;

    [ObservableProperty]
    private string? _statusDetail;

    [ObservableProperty]
    private DateTimeOffset? _lastUpdated;

    /// <summary>Last snapshot that loaded successfully. Kept when a later reload fails.</summary>
    [ObservableProperty]
    private DisplayDataSnapshot? _snapshot;

    public string StatusText => Status.ToDisplayText();

    public ObservableCollection<RowWarning> Warnings { get; } = [];

    public async Task InitializeAsync()
    {
        _configuration = await configurationService.LoadAsync();
        ExcelFile = _configuration.ExcelFile;
        if (!string.IsNullOrEmpty(ExcelFile))
        {
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task SelectFileAsync()
    {
        var path = fileDialog.PickExcelFile(ExcelFile);
        if (path is null)
        {
            return;
        }

        logger.LogInformation("Excel file selected: {File}", path);
        ExcelFile = path;
        _configuration = _configuration with { ExcelFile = path };
        await configurationService.SaveAsync(_configuration);
        await ReloadAsync();
    }

    private bool CanReload() => !string.IsNullOrEmpty(ExcelFile);

    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync()
    {
        Status = DataStatus.Reading;
        StatusDetail = null;
        var started = DateTimeOffset.Now;

        try
        {
            var result = await reader.ReadAsync(ExcelFile!);
            Snapshot = processor.BuildSnapshot(result.Records);
            LastUpdated = Snapshot.GeneratedAt;

            Warnings.Clear();
            foreach (var warning in result.Warnings)
            {
                Warnings.Add(warning);
            }

            Status = DataStatus.Updated;
            StatusDetail = result.Warnings.Count > 0 ? $"{result.Warnings.Count} dòng lỗi đã bị bỏ qua" : null;
            logger.LogInformation("Reload finished in {Elapsed} ms", (DateTimeOffset.Now - started).TotalMilliseconds);
        }
        catch (ExcelValidationException ex)
        {
            // Keep the last good snapshot on screen; only the status changes.
            logger.LogWarning(ex, "Reload failed: {Error}", ex.Error);
            Status = ex.Error switch
            {
                ExcelValidationError.FileNotFound => DataStatus.FileNotFound,
                ExcelValidationError.FileLocked => DataStatus.FileLocked,
                _ => DataStatus.InvalidData,
            };
            StatusDetail = ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while reloading {File}", ExcelFile);
            Status = DataStatus.InvalidData;
            StatusDetail = ex.Message;
        }
    }
}
