using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.Controls;

/// <summary>Biểu đồ đường lũy kế: thực hiện (liền) và mục tiêu (nét đứt). Không tương tác, tối ưu cho TV.</summary>
public sealed class TrendChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<HourlyPoint>), typeof(TrendChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<HourlyPoint>? Points
    {
        get => (IReadOnlyList<HourlyPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private static readonly Brush Grid = Freeze(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)));
    private static readonly Brush Label = Freeze(new SolidColorBrush(Color.FromRgb(0xB7, 0xC6, 0xE0)));
    private static readonly Brush Actual = Freeze(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)));
    private static readonly Brush Target = Freeze(new SolidColorBrush(Color.FromRgb(0xF4, 0x72, 0xB6)));
    private static readonly Brush AreaFill = Freeze(new LinearGradientBrush(Color.FromArgb(120, 0x3B, 0x82, 0xF6), Color.FromArgb(0, 0x3B, 0x82, 0xF6), 90));
    private static readonly Typeface Font = new("Segoe UI");

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var points = Points;
        if (points is null || points.Count < 2 || ActualWidth < 100 || ActualHeight < 100)
            return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double left = 130, bottom = 60, top = 20, right = 40;
        var plot = new Rect(left, top, ActualWidth - left - right, ActualHeight - top - bottom);

        var max = points.Max(p => Math.Max(p.CumulativeTarget, p.CumulativeQuantity ?? 0));
        var yMax = NiceCeiling((double)max);
        double X(int i) => plot.Left + plot.Width * i / (points.Count - 1);
        double Y(decimal v) => plot.Bottom - plot.Height * (double)v / yMax;

        // Lưới ngang + nhãn trục Y
        const int gridLines = 4;
        for (var i = 0; i <= gridLines; i++)
        {
            var value = yMax * i / gridLines;
            var y = plot.Bottom - plot.Height * i / gridLines;
            dc.DrawLine(new Pen(Grid, 1.5), new Point(plot.Left, y), new Point(plot.Right, y));
            var text = Text(value.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")), 26, Label, dpi);
            dc.DrawText(text, new Point(plot.Left - text.Width - 16, y - text.Height / 2));
        }

        // Nhãn trục X
        var step = points.Count > 12 ? 2 : 1;
        for (var i = 0; i < points.Count; i += step)
        {
            var text = Text(points[i].Hour.ToString("HH:mm"), 26, Label, dpi);
            dc.DrawText(text, new Point(X(i) - text.Width / 2, plot.Bottom + 12));
        }

        // Đường mục tiêu (nét đứt)
        var targetGeometry = Polyline(points.Select((p, i) => new Point(X(i), Y(p.CumulativeTarget))));
        dc.DrawGeometry(null, new Pen(Target, 5) { DashStyle = new DashStyle([3, 2], 0) }, targetGeometry);

        // Đường thực hiện + vùng tô bên dưới
        var actual = points.Select((p, i) => (p, i)).Where(x => x.p.CumulativeQuantity is not null)
            .Select(x => new Point(X(x.i), Y(x.p.CumulativeQuantity!.Value))).ToList();
        if (actual.Count == 0)
            return;
        if (actual.Count > 1)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(actual[0].X, plot.Bottom), true, true);
                ctx.PolyLineTo(actual, false, false);
                ctx.LineTo(new Point(actual[^1].X, plot.Bottom), false, false);
            }
            dc.DrawGeometry(AreaFill, null, area);
            dc.DrawGeometry(null, new Pen(Actual, 7) { LineJoin = PenLineJoin.Round }, Polyline(actual));
        }
        foreach (var point in actual)
            dc.DrawEllipse(Actual, new Pen(Brushes.White, 3), point, 8, 8);

        // Nhãn giá trị tại điểm mới nhất
        var last = actual[^1];
        var lastValue = points.Last(p => p.CumulativeQuantity is not null).CumulativeQuantity!.Value;
        var label = Text(lastValue.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")), 34, Brushes.Black, dpi, FontWeights.Bold);
        var box = new Rect(last.X - label.Width / 2 - 14, last.Y - label.Height - 30, label.Width + 28, label.Height + 8);
        if (box.Right > ActualWidth) box.X = ActualWidth - box.Width;
        if (box.Top < 0) box.Y = last.Y + 20;
        dc.DrawRoundedRectangle(Brushes.White, null, box, 8, 8);
        dc.DrawText(label, new Point(box.X + 14, box.Y + 4));
    }

    private static StreamGeometry Polyline(IEnumerable<Point> points)
    {
        var list = points.ToList();
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(list[0], false, false);
        ctx.PolyLineTo(list.Skip(1).ToList(), true, true);
        return geometry;
    }

    private static FormattedText Text(string text, double size, Brush brush, double dpi, FontWeight? weight = null) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            weight is null ? Font : new Typeface(Font.FontFamily, FontStyles.Normal, weight.Value, FontStretches.Normal), size, brush, dpi);

    private static double NiceCeiling(double value)
    {
        if (value <= 0)
            return 100;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var factor in new[] { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 })
        {
            if (factor * magnitude >= value)
                return factor * magnitude;
        }
        return 10 * magnitude;
    }
}
