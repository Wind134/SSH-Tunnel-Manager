using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SSHTunnelManager.Models;

namespace SSHTunnelManager.Converters;

public class StatusToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush s_gray = new(Color.FromRgb(156, 156, 156));
    private static readonly SolidColorBrush s_yellow = new(Color.FromRgb(255, 185, 0));
    private static readonly Brush s_blue = new SolidColorBrush(Color.FromRgb(0, 120, 212));
    private static readonly Brush s_green = new SolidColorBrush(Color.FromRgb(16, 185, 129));
    private static readonly Brush s_red = new SolidColorBrush(Color.FromRgb(239, 68, 68));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TunnelStatus status)
        {
            return status switch
            {
                TunnelStatus.Disconnected => s_gray,
                TunnelStatus.Connecting => s_yellow,
                TunnelStatus.HostKeyPending => s_blue,
                TunnelStatus.Connected => s_green,
                TunnelStatus.Failed => s_red,
                _ => s_gray
            };
        }
        return s_gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public class StatusToDotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TunnelStatus status)
        {
            return status switch
            {
                TunnelStatus.Disconnected => "\u25CB",
                TunnelStatus.Connecting => "\u25D0",
                TunnelStatus.HostKeyPending => "\u25CF",
                TunnelStatus.Connected => "\u25CF",
                TunnelStatus.Failed => "\u25CF",
                _ => "\u25CB"
            };
        }
        return "\u25CB";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is bool b && !b) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
