using System.Windows;
using System.Windows.Threading;
using DisplayBoard.App.Services;
using DisplayBoard.App.ViewModels;
using DisplayBoard.App.Views;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Processing;
using DisplayBoard.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DisplayBoard.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private bool _trayHintShown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigureLogging();
        Log.Information("Khởi động Display Board");

        _services = ConfigureServices();

        // Startup flow: cấu hình → màn hình → Excel → snapshot → watcher → cửa sổ chính.
        var config = _services.GetRequiredService<IConfigurationService>().Current;
        var snapshots = _services.GetRequiredService<ISnapshotService>();
        var watcher = _services.GetRequiredService<IExcelWatcher>();
        watcher.FileChanged += async (_, _) =>
        {
            try
            {
                await snapshots.ReloadAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Lỗi khi đọc lại Excel");
            }
        };
        if (!string.IsNullOrWhiteSpace(config.ExcelFile) && config.AutoReload)
            watcher.Watch(config.WatchedFiles(), config.DebounceMilliseconds);
        await snapshots.ReloadAsync();

        var display = _services.GetRequiredService<IDisplayManager>();
        var viewModel = _services.GetRequiredService<MainViewModel>();
        _mainWindow = new MainWindow(viewModel) { HideOnClose = () => display.IsRunning };
        _mainWindow.HiddenToTray += (_, _) =>
        {
            if (_trayHintShown)
                return;
            _trayHintShown = true;
            _tray?.ShowBalloon("Vẫn đang trình chiếu. Mở lại từ biểu tượng ở khay hệ thống.");
        };
        _mainWindow.Closed += (_, _) => Shutdown();

        _tray = new TrayService(
            open: ShowMainWindow,
            start: () => viewModel.StartPresentationCommand.Execute(null),
            stop: () => viewModel.StopPresentationCommand.Execute(null),
            exit: () =>
            {
                display.Stop();
                _mainWindow.HideOnClose = null;
                _mainWindow.Close();
            });
        display.RunningChanged += (_, _) => _tray.SetRunning(display.IsRunning);

        _mainWindow.Show();
        viewModel.PreviewCommand.Execute(null);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
            return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IConfigurationService>(sp => new ConfigurationService(sp.GetRequiredService<ILogger<ConfigurationService>>()));
        services.AddSingleton<IExcelDataReader, ExcelDataReader>();
        services.AddSingleton<ProductProcessor>();
        services.AddSingleton<IDailyHistoryStore>(sp => new JsonDailyHistoryStore(sp.GetRequiredService<ILogger<JsonDailyHistoryStore>>()));
        services.AddSingleton<ISnapshotService, SnapshotService>();
        services.AddSingleton<IExcelWatcher, ExcelWatcher>();
        services.AddSingleton<IScreenManager, ScreenManager>();
        services.AddSingleton<IDisplayManager, DisplayManager>();
        foreach (var view in ViewCatalog.CreateDefault())
            services.AddSingleton(view);

        services.AddSingleton<ClockViewModel>();
        services.AddSingleton<DisplayViewFactory>();
        services.AddTransient<DisplayHostViewModel>();
        services.AddSingleton<Func<DisplayHostViewModel>>(sp => () => sp.GetRequiredService<DisplayHostViewModel>());
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider();
    }

    private void ConfigureLogging()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayBoard", "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(folder, "display-board-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
            .CreateLogger();

        // Không để lỗi bất ngờ làm tắt app: ghi log rồi tiếp tục, TV giữ nội dung đang có.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Lỗi không xử lý trên UI thread");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Lỗi không xử lý (terminating={Terminating})", args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Task lỗi không được xử lý");
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Tắt Display Board");
        _tray?.Dispose();
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
