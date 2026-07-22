using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using HandleViewer.Models;
using HandleViewer.Services;

namespace HandleViewer;

public partial class MainView : UserControl
{
    private List<PortOccupant> _all = new();
    private ListCollectionView _view;
    private bool _loading;
    private bool _initialized;

    // System Idle (0) and System (4) cannot be killed and live in the kernel.
    private static readonly HashSet<int> SystemPids = new() { 0, 4 };

    // Process names that are critical to Windows stability — prompt before killing.
    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "wininit", "services", "lsass", "smss", "winlogon",
        "explorer", "dwm", "svchost", "System",
    };

    public MainView()
    {
        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_all);
        _view.Filter = RowFilter;

        InitializeComponent();

        _initialized = true;
        Grid.ItemsSource = _view;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Reload();

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && _initialized)
            _view?.Refresh();
        UpdateStatus();
    }

    private async void Reload()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            Cursor = Cursors.Wait;
            var entries = await System.Threading.Tasks.Task.Run(
                () => PortInspector.GetAllTcpEntries());

            _all = entries;
            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_all);
            _view.Filter = RowFilter;
            Grid.ItemsSource = _view;
        }
        finally
        {
            Cursor = Cursors.Arrow;
            _loading = false;
        }
        UpdateStatus();
    }

    // --- Context menu handlers ---

    private PortOccupant? GetSelectedRow()
    {
        return Grid.SelectedItem as PortOccupant;
    }

    private void OpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row == null || string.IsNullOrEmpty(row.ProcessPath)) return;

        try
        {
            var dir = System.IO.Path.GetDirectoryName(row.ProcessPath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{dir}\"",
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开目录：\n{ex.Message}", "句柄查看器",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void KillProcess_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row == null) return;

        if (SystemPids.Contains(row.Pid))
        {
            MessageBox.Show("无法终止系统内核进程（PID 0/4）。", "句柄查看器",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Critical process — double-confirm.
        if (CriticalProcesses.Contains(row.ProcessName) || row.Pid == 4)
        {
            var result = MessageBox.Show(
                $"进程 \"{row.ProcessName}\" (PID {row.Pid}) 是关键系统进程。\n" +
                "终止它可能导致系统不稳定甚至蓝屏。\n\n确定要继续吗？",
                "警告 - 关键进程",
                MessageBoxButton.YesNo, MessageBoxImage.Exclamation);

            if (result != MessageBoxResult.Yes)
                return;
        }
        else
        {
            // Normal process — simple confirm.
            var result = MessageBox.Show(
                $"确定终止进程 \"{row.ProcessName}\" (PID {row.Pid})？",
                "确认终止进程",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        try
        {
            var proc = Process.GetProcessById(row.Pid);
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(3000);

            // Refresh the list after killing.
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"终止进程失败：\n{ex.Message}\n\n" +
                "可能需要以管理员身份运行。",
                "句柄查看器", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row == null) return;

        var text = $"状态\t{row.State}\n" +
                   $"协议\t{row.Family}\n" +
                   $"本地地址\t{row.LocalAddress}\n" +
                   $"本地端口\t{row.LocalPort}\n" +
                   $"远程地址\t{row.RemoteAddress}\n" +
                   $"远程端口\t{row.RemotePort}\n" +
                   $"PID\t{row.Pid}\n" +
                   $"进程名\t{row.ProcessName}\n" +
                   $"可执行路径\t{row.ProcessPath}";

        try { Clipboard.SetText(text); }
        catch { /* clipboard locked */ }
    }

    // --- Filtering ---

    private bool RowFilter(object obj)
    {
        if (obj is not PortOccupant row)
            return false;

        var kindSel = (KindFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        if (kindSel == "Listener" && row.Kind != TcpEntryKind.Listener)
            return false;
        if (kindSel == "Established" && row.Kind != TcpEntryKind.Established)
            return false;

        var famSel = (FamilyFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        if (famSel == "IPv4" && row.Family != IpFamily.IPv4)
            return false;
        if (famSel == "IPv6" && row.Family != IpFamily.IPv6)
            return false;

        if (ShowSystem.IsChecked != true && SystemPids.Contains(row.Pid))
            return false;

        var q = SearchBox.Text?.Trim() ?? "";
        if (q.Length > 0)
        {
            bool hit =
                row.LocalPort.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                || row.RemotePort.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                || row.Pid.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                || row.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || row.LocalAddress.Contains(q, StringComparison.OrdinalIgnoreCase)
                || row.RemoteAddress.Contains(q, StringComparison.OrdinalIgnoreCase);
            if (!hit)
                return false;
        }

        return true;
    }

    private void UpdateStatus()
    {
        if (!_initialized || _all == null || _view == null || StatusText == null) return;

        var visible = _view.OfType<PortOccupant>().ToList();
        int total = _all.Count;
        int listening = _all.Count(o => o.Kind == TcpEntryKind.Listener);
        int established = _all.Count(o => o.Kind == TcpEntryKind.Established);
        int visListen = visible.Count(o => o.Kind == TcpEntryKind.Listener);
        int visConn = visible.Count(o => o.Kind == TcpEntryKind.Established);

        StatusText.Text = $"共 {total} 条 - 监听 {listening} - 连接 {established}"
                          + (total != visible.Count ? $"   (显示 {visible.Count} - 监听 {visListen} - 连接 {visConn})" : "");
    }
}