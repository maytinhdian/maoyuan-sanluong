using System.IO;
using System.Windows;
using System.Windows.Threading;
using DisplayBoard.App.Services;
using DisplayBoard.App.ViewModels;
using DisplayBoard.App.Views;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DisplayBoard.App;

public partial class App : Application
{
    private IHost? _host;

    /// <summary>%LOCALAPPDATA%\DisplayBoard</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayBoard");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(DataDirectory, "logs", "display-board-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();

        RegisterGlobalExceptionHandlers();
        Log.Information("Display Board starting");

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(ConfigureServices)
            .Build();
        await _host.StartAsync();

        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();

        await viewModel.InitializeAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("Display Board shutting down");
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IExcelDataReader, ExcelDataReader>();
        services.AddSingleton<IDataProcessor, DataProcessor>();
        services.AddSingleton<IConfigurationService>(sp => new ConfigurationService(
            Path.Combine(DataDirectory, ConfigurationService.FileName),
            sp.GetRequiredService<ILogger<ConfigurationService>>()));
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<MainWindowViewModel>();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }
}
