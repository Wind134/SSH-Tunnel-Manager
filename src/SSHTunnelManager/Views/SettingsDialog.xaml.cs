using System.Windows;
using System.Windows.Controls;
using SSHTunnelManager.Models;

namespace SSHTunnelManager.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(AppSettings initial)
    {
        InitializeComponent();

        AutoStartMinimizedBox.IsChecked = initial.AutoStartMinimized;
        MinimizeToTrayBox.IsChecked = initial.MinimizeToTrayOnClose;

        foreach (ComboBoxItem item in ThemeBox.Items)
        {
            if (item.Tag?.ToString() == initial.Theme)
            {
                ThemeBox.SelectedItem = item;
                break;
            }
        }

        if (ThemeBox.SelectedItem == null)
            ThemeBox.SelectedIndex = 0;
    }

    public AppSettings GetSettings() => new()
    {
        AutoStartMinimized = AutoStartMinimizedBox.IsChecked == true,
        MinimizeToTrayOnClose = MinimizeToTrayBox.IsChecked == true,
        Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "System"
    };

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
