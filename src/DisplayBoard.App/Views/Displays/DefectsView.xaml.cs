using System.Windows;
using System.Windows.Controls;

namespace DisplayBoard.App.Views.Displays;

public partial class DefectsView : UserControl
{
    public DefectsView() => InitializeComponent();

    // Video hàng lỗi trên TV: phát ngay khi ô hiện ra, hết thì quay lại đầu (MediaElement không có sẵn chế độ lặp).
    private void Video_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MediaElement { Source: not null } video)
            video.Play();
    }

    private void Video_Ended(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement video)
            return;
        video.Position = TimeSpan.Zero;
        video.Play();
    }

    // Windows thiếu bộ giải mã (vd. HEVC, WebM trên máy cũ): báo trong ô thay vì để ô đen.
    private void Video_Failed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is not MediaElement video)
            return;
        video.Visibility = Visibility.Collapsed;
        if (video.Tag is UIElement error)
            error.Visibility = Visibility.Visible;
    }
}
