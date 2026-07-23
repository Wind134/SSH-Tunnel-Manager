using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SSHTunnelManager.Models;
using SSHTunnelManager.Services;

namespace TinyTools.Views;

public partial class SettingsDialog : Window
{
    private readonly string _lastPage;

    public SettingsDialog(AppSettings initial)
    {
        InitializeComponent();
        _lastPage = initial.LastPage;
        Populate(initial);
    }

    public AppSettings GetSettings() => new()
    {
        AutoStartMinimized = AutoStartMinimizedBox.IsChecked == true,
        MinimizeToTrayOnClose = MinimizeToTrayBox.IsChecked == true,
        ConfirmBeforeExit = ConfirmBeforeExitBox.IsChecked == true,
        ShowTrayNotifications = ShowTrayNotificationsBox.IsChecked == true,
        Theme = SelectedTag(ThemeBox, "System"),
        StartPage = SelectedTag(StartPageBox, "LastUsed"),
        LastPage = _lastPage,
        PortAutoRefreshSeconds = int.TryParse(SelectedTag(PortRefreshBox, "0"), out int seconds)
            ? seconds
            : 0,
        ShowSystemProcesses = ShowSystemProcessesBox.IsChecked == true
    };

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

    private static string SelectedTag(ComboBox comboBox, string fallback)
        => (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static void SelectTag(ComboBox comboBox, string? value, string fallback)
    {
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), fallback, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
        => Populate(new AppSettings { LastPage = _lastPage });

    private void OpenDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(ConfigStorage.GetConfigPath());
            if (string.IsNullOrEmpty(directory))
                return;

            System.IO.Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"无法打开数据目录：\n{ex.Message}",
                "TinyTools",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
