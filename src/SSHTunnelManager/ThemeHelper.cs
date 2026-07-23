using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SSHTunnelManager;

/// <summary>
/// Applies the Light / Dark / System theme by swapping the brush resources
/// declared in App.xaml. MainWindow binds its Background / Foreground to these
/// resources via DynamicResource, so changes propagate automatically.
/// </summary>
public static class ThemeHelper
{
    public static void Apply(string? theme)
    {
        var app = Application.Current;
        if (app == null)
            return;

        bool dark = IsDark(theme);

        app.Resources["AppBg"] = new SolidColorBrush(dark ? System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20) : Colors.WhiteSmoke);
        app.Resources["AppFg"] = new SolidColorBrush(dark ? System.Windows.Media.Color.FromRgb(0xE6, 0xE6, 0xE6) : Colors.Black);
        app.Resources["PanelBg"] = new SolidColorBrush(dark ? System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D) : System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5));
        app.Resources["BorderCol"] = new SolidColorBrush(dark ? System.Windows.Media.Color.FromRgb(0x40, 0x40, 0x40) : Colors.LightGray);
       app.Resources["MutedFg"] = new SolidColorBrush(dark ? System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA) : System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));
       app.Resources["GridBg"] = new SolidColorBrush(dark ? System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x25) : Colors.White);
       app.Resources["GridAltBg"] = new SolidColorBrush(dark ? System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A) : System.Windows.Media.Color.FromRgb(0xFA, 0xFA, 0xFA));
       app.Resources["GridBorder"] = new SolidColorBrush(dark ? System.Windows.Media.Color.FromRgb(0x40, 0x40, 0x40) : Colors.LightGray);
   }

    private static bool IsDark(string? theme)
    {
        if (string.Equals(theme, "Dark", System.StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(theme, "Light", System.StringComparison.OrdinalIgnoreCase))
            return false;

        // "System" (or anything unexpected) -> follow Windows Apps theme.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v == 0; // 0 = dark, 1 = light
        }
        catch
        {
            // registry unavailable -> fall back to light
        }

        return false;
    }
}
