using System.ComponentModel;
using System.Windows;
using DisplayBoard.App.ViewModels;

namespace DisplayBoard.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Khi true, đóng cửa sổ sẽ ẩn xuống khay thay vì thoát (đang trình chiếu).</summary>
    public Func<bool>? HideOnClose { get; set; }

    public event EventHandler? HiddenToTray;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (HideOnClose?.Invoke() == true)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnClosing(e);
    }

    // Mở website của đơn vị phát triển bằng trình duyệt mặc định.
    private void OnRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
