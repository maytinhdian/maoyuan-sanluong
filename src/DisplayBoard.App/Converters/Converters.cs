using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DisplayBoard.App.Converters;

/// <summary>Đường dẫn file → ảnh. Load vào bộ nhớ (OnLoad) để không giữ khóa file ảnh.</summary>
public sealed class PathToImageConverter : IValueConverter
{
    private static readonly Dictionary<string, (DateTime Stamp, ImageSource Image)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public int DecodePixelWidth { get; set; } = 400;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
            return null;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            var key = $"{path}|{DecodePixelWidth}";
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached) && cached.Stamp == stamp)
                    return cached.Image;
            }
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = DecodePixelWidth;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            lock (Cache)
            {
                Cache[key] = (stamp, image);
            }
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            Serilog.Log.Warning("Không load được ảnh {Path}: {Message}", path, ex.Message);
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex && ColorConverter.ConvertFromString(hex) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException)
        {
        }
        return Brushes.SteelBlue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Null/rỗng → Collapsed. Parameter "invert" để đảo.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string s => s.Length > 0,
            bool b => b,
            int i => i != 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true
        };
        if (parameter is "invert")
            hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Trạng thái Đạt/Gần đạt/Chậm → xanh/vàng/đỏ. bool = chênh lệch âm? Parameter "text": không có trạng thái thì trả màu chữ trắng.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush Met = Frozen("#22C55E");
    private static readonly Brush Near = Frozen("#F5B301");
    private static readonly Brush NotMet = Frozen("#EF4444");
    private static readonly Brush None = Frozen("#5B6B85");
    private static readonly Brush NoneText = Frozen("#FFFFFF");

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DisplayBoard.Core.Models.ProgressStatus.Met => Met,
        DisplayBoard.Core.Models.ProgressStatus.Near => Near,
        DisplayBoard.Core.Models.ProgressStatus.Behind => NotMet,
        // bool: true = âm/thiếu (đỏ), false = xanh
        bool negative => negative ? NotMet : Met,
        _ => parameter is "text" ? NoneText : None
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>[phân số 0..1, độ rộng khung] → độ rộng thanh tiến độ.</summary>
public sealed class FractionOfWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = values.Length > 0 && values[0] is double f ? f : 0;
        var width = values.Length > 1 && values[1] is double w ? w : 0;
        return Math.Max(0, Math.Clamp(fraction, 0, 1) * width);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
