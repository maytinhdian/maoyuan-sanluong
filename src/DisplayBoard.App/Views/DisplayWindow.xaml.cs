using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.Views;

/// <summary>Cửa sổ fullscreen trên một TV. Đặt vị trí bằng pixel thật để đúng cả khi các màn hình khác DPI.</summary>
public partial class DisplayWindow : Window
{
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private readonly MonitorInfo _monitor;

    public DisplayWindow(MonitorInfo monitor)
    {
        _monitor = monitor;
        InitializeComponent();
        // Vị trí gần đúng trước khi có handle (tính theo DIP của màn hình chính), sau đó chỉnh chính xác trong SourceInitialized.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = monitor.X;
        Top = monitor.Y;
        Width = monitor.Width;
        Height = monitor.Height;
        SourceInitialized += (_, _) => ApplyBounds();
        DpiChanged += (_, _) => ApplyBounds();
    }

    public void ApplyBounds()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            SetWindowPos(handle, IntPtr.Zero, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, SwpNoZOrder | SwpNoActivate | SwpShowWindow);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
