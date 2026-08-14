using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SSHTunnelManager.Models;
using SSHTunnelManager.Services;

namespace TinyTools.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Populate(App.Services.Settings);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            AutoStartMinimized = AutoStartMinimizedBox.IsChecked == true,
            MinimizeToTrayOnClose = MinimizeToTrayBox.IsChecked == true,
            ConfirmBeforeExit = ConfirmBeforeExitBox.IsChecked == true,
            ShowTrayNotifications = ShowTrayNotificationsBox.IsChecked == true,
            Theme = SelectedTag(ThemeBox, "System"),
            StartPage = SelectedTag(StartPageBox, "LastUsed"),
            LastPage = App.Services.Settings.LastPage,
            PortAutoRefreshSeconds = int.TryParse(SelectedTag(PortRefreshBox, "0"), out int seconds)
                ? seconds
                : 0,
            ShowSystemProcesses = ShowSystemProcessesBox.IsChecked == true,
        };

        App.Services.SaveSettings(settings);
        ApplyTheme(settings.Theme);
        SavedBar.IsOpen = true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
        => Populate(new AppSettings { LastPage = App.Services.Settings.LastPage });

    private void OpenDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? directory = Path.GetDirectoryName(ConfigStorage.GetConfigPath());
            if (string.IsNullOrWhiteSpace(directory))
                return;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SavedBar.Severity = InfoBarSeverity.Error;
            SavedBar.Message = $"无法打开数据目录：{ex.Message}";
            SavedBar.IsOpen = true;
        }
    }

    private void Populate(AppSettings settings)
    {
        AutoStartMinimizedBox.IsChecked = settings.AutoStartMinimized;
        MinimizeToTrayBox.IsChecked = settings.MinimizeToTrayOnClose;
        ConfirmBeforeExitBox.IsChecked = settings.ConfirmBeforeExit;
        ShowTrayNotificationsBox.IsChecked = settings.ShowTrayNotifications;
        ShowSystemProcessesBox.IsChecked = settings.ShowSystemProcesses;
        SelectTag(ThemeBox, settings.Theme, "System");
        SelectTag(StartPageBox, settings.StartPage, "LastUsed");
        SelectTag(PortRefreshBox, settings.PortAutoRefreshSeconds.ToString(), "0");
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

    private static string SelectedTag(ComboBox comboBox, string fallback)
        => (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static void SelectTag(ComboBox comboBox, string? value, string fallback)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            ?? comboBox.Items
                .OfType<ComboBoxItem>()
                .First(item => string.Equals(item.Tag?.ToString(), fallback, StringComparison.OrdinalIgnoreCase));
    }
}
