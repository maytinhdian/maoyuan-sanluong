using System.Windows;
using System.Windows.Media;

namespace DisplayBoard.App.Controls;

/// <summary>Thanh tiến độ ngang bo tròn. <see cref="Fraction"/> 0..1 (lớn hơn 1 vẫn vẽ đầy).</summary>
public sealed class ProgressLine : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(ProgressLine), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(ProgressLine), new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(ProgressLine), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;
        var radius = h / 2;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), radius, radius);
        var filled = Math.Clamp(Fraction, 0, 1) * w;
        if (filled > 0)
            dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, Math.Max(filled, h), h), radius, radius);
    }
}
