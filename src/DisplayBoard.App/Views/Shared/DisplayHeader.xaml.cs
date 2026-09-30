using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DisplayBoard.App.Views.Shared;

public partial class DisplayHeader : UserControl
{
    public static readonly DependencyProperty HeaderBackgroundProperty = DependencyProperty.Register(
        nameof(HeaderBackground), typeof(Brush), typeof(DisplayHeader), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x0F, 0x24, 0x47))));
    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(
        nameof(IconBrush), typeof(Brush), typeof(DisplayHeader), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))));

    public DisplayHeader() => InitializeComponent();

    public Brush HeaderBackground
    {
        get => (Brush)GetValue(HeaderBackgroundProperty);
        set => SetValue(HeaderBackgroundProperty, value);
    }

    public Brush IconBrush
    {
        get => (Brush)GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }
}
