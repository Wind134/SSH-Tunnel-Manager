using System.Collections.ObjectModel;
using HandleViewer.Models;
using HandleViewer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TinyTools.WinUI.Pages;

public sealed partial class PortsPage : Page
{
    private readonly ObservableCollection<PortOccupant> _items = new();

    public PortsPage()
    {
        InitializeComponent();
        PortList.ItemsSource = _items;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "正在读取 TCP 端口…";
        try
        {
            var entries = await Task.Run(PortInspector.GetAllTcpEntries);
            _items.Clear();
            foreach (var entry in entries)
                _items.Add(entry);
            StatusText.Text = $"共 {entries.Count} 条 TCP 记录";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"读取失败：{ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }
}
