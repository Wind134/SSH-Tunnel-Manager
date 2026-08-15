using System.Collections.ObjectModel;
using HandleViewer.Models;
using HandleViewer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TinyTools.WinUI.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TinyTools.WinUI.Pages;

public sealed partial class PortsPage : Page
{
    private readonly ObservableCollection<PortOccupant> _visibleItems = new();
    private readonly DispatcherTimer _autoRefreshTimer = new();
    private List<PortOccupant> _allItems = [];
    private CancellationTokenSource? _refreshCancellation;
    private bool _initialized;
    private bool _refreshing;

    public PortsPage()
    {
        InitializeComponent();
        PortList.ItemsSource = _visibleItems;
        ShowSystem.IsChecked = App.Services.Settings.ShowSystemProcesses;
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        Loaded += PortsPage_Loaded;
        Unloaded += PortsPage_Unloaded;
        _initialized = true;
    }

    private async void PortsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ConfigureAutoRefresh();
        await RefreshAsync();
    }

    private void PortsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _autoRefreshTimer.Stop();
        _refreshCancellation?.Cancel();
    }

    private async void AutoRefreshTimer_Tick(object? sender, object e)
        => await RefreshAsync();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        => await RefreshAsync();

    private void Filter_Changed(object sender, object e)
    {
        if (_initialized)
            ApplyFilter();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;

        _refreshing = true;
        _refreshCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        RefreshButton.IsEnabled = false;
        RefreshProgress.IsActive = true;
        StatusText.Text = "正在读取 TCP 端口…";

        try
        {
            List<PortOccupant> entries = await Task.Run(
                PortInspector.GetAllTcpEntries, cancellation.Token);
            if (cancellation.IsCancellationRequested)
                return;

            _allItems = entries;
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            // Navigating away cancels a pending refresh.
        }
        catch (Exception ex)
        {
            StatusText.Text = $"读取失败：{ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, cancellation))
            {
                _refreshCancellation = null;
                _refreshing = false;
                RefreshButton.IsEnabled = true;
                RefreshProgress.IsActive = false;
            }
            cancellation.Dispose();
        }
    }

    private void ApplyFilter()
    {
        string kind = SelectedTag(KindFilter);
        string family = SelectedTag(FamilyFilter);
        string query = SearchBox.Text.Trim();
        bool showSystem = ShowSystem.IsChecked == true;

        IEnumerable<PortOccupant> filtered = _allItems.Where(entry =>
            (kind == "All"
                || kind == "Listener" && entry.Kind == TcpEntryKind.Listener
                || kind == "Established" && entry.Kind == TcpEntryKind.Established)
            && (family == "All"
                || family == "IPv4" && entry.Family == IpFamily.IPv4
                || family == "IPv6" && entry.Family == IpFamily.IPv6)
            && (showSystem || entry.Pid is not (0 or 4))
            && MatchesSearch(entry, query));

        _visibleItems.Clear();
        foreach (PortOccupant entry in filtered)
            _visibleItems.Add(entry);

        int listeners = _allItems.Count(item => item.Kind == TcpEntryKind.Listener);
        int connections = _allItems.Count - listeners;
        string visible = _visibleItems.Count == _allItems.Count
            ? string.Empty
            : $"；当前显示 {_visibleItems.Count} 条";
        StatusText.Text = $"共 {_allItems.Count} 条：监听 {listeners}，连接 {connections}{visible}";
    }

    private static bool MatchesSearch(PortOccupant entry, string query)
    {
        if (query.Length == 0)
            return true;

        return entry.LocalPort.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.RemotePort.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.LocalAddress.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.RemoteAddress.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string SelectedTag(ComboBox comboBox)
        => (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";

    private void ConfigureAutoRefresh()
    {
        _autoRefreshTimer.Stop();
        int seconds = App.Services.Settings.PortAutoRefreshSeconds;
        if (seconds <= 0)
            return;

        _autoRefreshTimer.Interval = TimeSpan.FromSeconds(seconds);
        _autoRefreshTimer.Start();
    }

    private async void OpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PortOccupant entry)
            await ProcessActionCoordinator.OpenExecutableDirectoryAsync(XamlRoot, entry.ProcessPath);
    }

    private async void TerminateProcess_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PortOccupant entry)
            return;

        if (await ProcessActionCoordinator.ConfirmAndTerminateAsync(
            XamlRoot, entry.Pid, entry.ProcessName))
        {
            await RefreshAsync();
        }
    }

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PortOccupant entry)
            return;

        var package = new DataPackage();
        package.SetText(
            $"PID\t{entry.Pid}\n进程名\t{entry.ProcessName}\n状态\t{entry.State}\n协议\t{entry.Family}\n" +
            $"本地\t{entry.LocalAddress}:{entry.LocalPort}\n远端\t{entry.RemoteAddress}:{entry.RemotePort}\n" +
            $"可执行路径\t{entry.ProcessPath}");
        Clipboard.SetContent(package);
    }
}
