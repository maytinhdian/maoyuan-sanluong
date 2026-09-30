using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DisplayBoard.App.ViewModels;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Excel;
using DisplayBoard.Core.Models;
using DisplayBoard.Core.Processing;

namespace DisplayBoard.App.Tests;

/// <summary>
/// Dựng từng view với dữ liệu của file Excel mẫu, render ra PNG và bắt lỗi binding.
/// Chỉ chạy được trên Windows (CI: windows-latest). Ảnh lưu ở TestResults/screenshots.
/// </summary>
public class RenderViewsTests
{
    public static TheoryData<string> ViewIdsData() =>
    [
        ViewIds.Overview, ViewIds.Ranking, ViewIds.ProductProgress, ViewIds.TopProducts,
        ViewIds.NotMet, ViewIds.Notice, ViewIds.Detail, ViewIds.MonthProgress, ViewIds.Lines, ViewIds.Hourly
    ];

    [Theory]
    [MemberData(nameof(ViewIdsData))]
    public void View_renders_without_binding_errors(string viewId)
    {
        var errors = RunSta(() =>
        {
            EnsureApplication();
            var listener = new BindingErrorListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            try
            {
                var snapshot = LoadSampleSnapshot();
                var viewModel = new DisplayViewFactory(new ClockViewModel()).Create(viewId);
                viewModel.Update(snapshot);
                viewModel.OnActivated();

                var host = new ContentControl { Content = viewModel, Width = 1920, Height = 1080 };
                host.Measure(new Size(1920, 1080));
                host.Arrange(new Rect(0, 0, 1920, 1080));
                host.UpdateLayout();

                var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);
                Save(bitmap, viewId);
                viewModel.OnDeactivated();
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            }
            return listener.Errors;
        });

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static DisplayDataSnapshot LoadSampleSnapshot()
    {
        var samples = Path.Combine(RepoRoot(), "samples");
        DisplaySheet sheet;
        using (var stream = File.OpenRead(Path.Combine(samples, "Theo_doi_san_luong_V18_mau.xlsx")))
            sheet = DisplaySheetReader.Read(stream);
        ContentData content;
        using (var stream = File.OpenRead(Path.Combine(samples, "display-content.xlsx")))
            content = ContentWorkbookReader.Read(stream);
        var date = sheet.Date ?? DateOnly.FromDateTime(DateTime.Today);
        var now = new DateTimeOffset(date.ToDateTime(new TimeOnly(14, 30)));
        return new SnapshotBuilder().Build(sheet, content, now, Path.Combine(samples, "images"));
    }

    private static void Save(BitmapSource bitmap, string name)
    {
        var folder = Path.Combine(RepoRoot(), "TestResults", "screenshots");
        Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, $"{name}.png"));
        encoder.Save(file);
    }

    private static readonly object AppLock = new();

    private static void EnsureApplication()
    {
        lock (AppLock)
        {
            if (Application.Current is not null)
                return;
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
        }
    }

    // Một Application chỉ gắn với một Dispatcher, nên mọi test dùng chung một STA thread.
    private static readonly Lazy<(Thread Thread, System.Windows.Threading.Dispatcher Dispatcher)> Sta = new(() =>
    {
        System.Windows.Threading.Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return (thread, dispatcher!);
    });

    private static T RunSta<T>(Func<T> action) => Sta.Value.Dispatcher.Invoke(action);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DisplayBoard.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy thư mục gốc repo");
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message is not null)
                Errors.Add(message);
        }
    }
}
