using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DisplayBoard.App.Controls;

/// <summary>Hiển thị icon theo tên (xem <see cref="IconGeometries"/>).</summary>
public sealed class Icon : Shape
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Icon),
        new FrameworkPropertyMetadata("star", FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    static Icon()
    {
        StretchProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(Stretch.Uniform));
        FillProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(Brushes.White));
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override Geometry DefiningGeometry => IconGeometries.Get(Kind);
}
