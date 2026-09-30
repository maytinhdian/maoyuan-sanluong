using System.Windows;
using System.Windows.Media;

namespace DisplayBoard.App.Controls;

/// <summary>Bộ icon vector đơn giản (lưới 24×24), không phụ thuộc font hệ thống.</summary>
public static class IconGeometries
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["factory"] = "M2,22 L2,10 L8,14 L8,10 L14,14 L14,3 L18,3 L18,14 L22,14 L22,22 Z M5,17 H8 V19 H5 Z M11,17 H14 V19 H11 Z M17,17 H20 V19 H17 Z",
        ["paint"] = "M3,3 H18 V9 H3 Z M18,5 H21 V12 H12 V15 H10 V10 H19 V7 H18 Z M9.5,15 H12.5 V22 H9.5 Z",
        ["box"] = "M12,2 L21,6.5 L21,17.5 L12,22 L3,17.5 L3,6.5 Z M12,4.2 L6,7.2 L12,10.2 L18,7.2 Z M11,12 L5,9 V16.3 L11,19.3 Z M13,12 V19.3 L19,16.3 V9 Z",
        ["search"] = "M10,2 A8,8 0 1 1 9.99,2 Z M10,5 A5,5 0 1 0 10.01,5 Z M15.2,13.8 L22,20.6 L20.6,22 L13.8,15.2 Z",
        ["trophy"] = "M6,2 H18 V4 H22 V8 A4,4 0 0 1 18,12 A6,6 0 0 1 13,15 V18 H17 V22 H7 V18 H11 V15 A6,6 0 0 1 6,12 A4,4 0 0 1 2,8 V4 H6 Z M4,6 V8 A2,2 0 0 0 6,10 V6 Z M18,6 V10 A2,2 0 0 0 20,8 V6 Z",
        ["people"] = "M8,3 A3.5,3.5 0 1 1 7.99,3 Z M1,20 A7,7 0 0 1 15,20 V21 H1 Z M17,5 A3,3 0 1 1 16.99,5 Z M16.5,13 A6,6 0 0 1 23,19 V21 H17 V20 A8.5,8.5 0 0 0 14.8,13.3 A6,6 0 0 1 16.5,13 Z",
        ["chart"] = "M2,20 H22 V22 H2 Z M3.5,12 H7.5 V19 H3.5 Z M10,7 H14 V19 H10 Z M16.5,3 H20.5 V19 H16.5 Z",
        ["trend"] = "M2,20 H22 V22 H2 Z M2,17 L8,10 L12,14 L20,5 L21.5,6.4 L12,17 L8,13 L3.5,18.3 Z",
        ["star"] = "M12,1.5 L15.1,8 L22.2,8.9 L17,13.8 L18.3,20.9 L12,17.5 L5.7,20.9 L7,13.8 L1.8,8.9 L8.9,8 Z",
        ["crown"] = "M2,7 L7,11.5 L12,3.5 L17,11.5 L22,7 L20,19 H4 Z M4,20.5 H20 V22.5 H4 Z",
        ["warning"] = "M12,1.5 L23.5,21.5 H0.5 Z M10.8,8.5 V15 H13.2 V8.5 Z M10.8,16.8 V19.2 H13.2 V16.8 Z",
        ["list"] = "M2,4 H5 V7 H2 Z M7,4 H22 V7 H7 Z M2,10.5 H5 V13.5 H2 Z M7,10.5 H22 V13.5 H7 Z M2,17 H5 V20 H2 Z M7,17 H22 V20 H7 Z",
        ["megaphone"] = "M3,9 H8 L18,3 V21 L8,15 H7 L8.5,21 H5.5 L4,15 H3 Z M20,9 H23 V15 H20 Z",
        ["clock"] = "M12,1 A11,11 0 1 1 11.99,1 Z M12,4 A8,8 0 1 0 12.01,4 Z M11,6 H13 V11.4 L17,13.8 L16,15.5 L11,12.6 Z",
        ["check"] = "M12,1 A11,11 0 1 1 11.99,1 Z M10.2,15.6 L6.6,12 L5.2,13.4 L10.2,18.4 L18.8,9.8 L17.4,8.4 Z",
        ["tool"] = "M21,6.5 A5.5,5.5 0 0 1 13.4,11.6 L5,20 A2.1,2.1 0 0 1 2,17 L10.4,8.6 A5.5,5.5 0 0 1 17.5,1 L14,4.5 L15,9 L19.5,10 Z",
        ["truck"] = "M1,5 H15 V16 H1 Z M15,9 H19.5 L23,12.5 V16 H15 Z M5,15 A2.5,2.5 0 1 1 4.99,15 Z M18,15 A2.5,2.5 0 1 1 17.99,15 Z",
    };

    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Geometry Get(string? name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "star" : name.Trim();
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached))
                return cached;
            Geometry geometry;
            if (name.Equals("gear", StringComparison.OrdinalIgnoreCase))
                geometry = CreateGear();
            else if (Paths.TryGetValue(name, out var data))
                geometry = Geometry.Parse("F0 " + data);
            else
                geometry = Geometry.Parse("F0 " + Paths["star"]);
            geometry.Freeze();
            Cache[name] = geometry;
            return geometry;
        }
    }

    private static Geometry CreateGear()
    {
        const int teeth = 8;
        var figure = new PathFigure { IsClosed = true, IsFilled = true };
        var center = new Point(12, 12);
        for (var i = 0; i < teeth * 4; i++)
        {
            var angle = Math.PI * 2 * i / (teeth * 4);
            var radius = (i % 4) is 0 or 1 ? 11 : 8.5;
            var point = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
            if (i == 0)
                figure.StartPoint = point;
            else
                figure.Segments.Add(new LineSegment(point, true));
        }
        var outer = new PathGeometry([figure]);
        var hole = new EllipseGeometry(center, 3.5, 3.5);
        return new CombinedGeometry(GeometryCombineMode.Exclude, outer, hole);
    }
}
