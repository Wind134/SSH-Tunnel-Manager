using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SSHTunnelManager.Services;

namespace TinyTools.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loaded;

    public SettingsPage()
    {
        InitializeComponent();
        string theme = ConfigStorage.Load().Settings.Theme;
        ThemeBox.SelectedIndex = theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        ApplyTheme(theme);
        _loaded = true;
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || ThemeBox.SelectedItem is not ComboBoxItem item)
            return;

        string theme = item.Tag?.ToString() ?? "System";
        var settings = ConfigStorage.Load().Settings;
        settings.Theme = theme;
        ConfigStorage.SaveSettings(settings);
        ApplyTheme(theme);
    }

    private static void ApplyTheme(string theme)
    {
        App.MainWindow.RootElement.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }
}
