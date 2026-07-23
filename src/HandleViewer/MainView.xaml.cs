using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using HandleViewer.Models;
using HandleViewer.Services;

namespace HandleViewer;

public partial class MainView : UserControl
{
    private List<PortOccupant> _all = new();
    private ListCollectionView _view;
    private bool _loading;
    private bool _initialized;
    private CancellationTokenSource? _portReloadCts;
    private CancellationTokenSource? _fileLockCts;
    private readonly DispatcherTimer _autoRefreshTimer = new();
    private int _autoRefreshSeconds;

    private static readonly HashSet<int> SystemPids = new() { 0, 4 };

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
        Unloaded += (_, _) => _autoRefreshTimer.Stop();
        _autoRefreshTimer.Tick += (_, _) =>
        {
            if (IsVisible)
                Reload();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Reload();
        UpdateAutoRefreshTimer();
    }

    public void ApplySettings(int autoRefreshSeconds, bool showSystemProcesses)
    {
        _autoRefreshSeconds = Math.Max(0, autoRefreshSeconds);
        ShowSystem.IsChecked = showSystemProcesses;
        _view?.Refresh();
        UpdateStatus();
        UpdateAutoRefreshTimer();
    }

    private void UpdateAutoRefreshTimer()
    {
        _autoRefreshTimer.Stop();
        if (_autoRefreshSeconds <= 0 || !IsLoaded)
            return;

        _autoRefreshTimer.Interval = TimeSpan.FromSeconds(_autoRefreshSeconds);
        _autoRefreshTimer.Start();
    }

    // --- Port tab ---

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
        _portReloadCts?.Cancel();
        _portReloadCts = new CancellationTokenSource();
        var ct = _portReloadCts.Token;
        try
        {
            Cursor = Cursors.Wait;
            var entries = await System.Threading.Tasks.Task.Run(
                () => PortInspector.GetAllTcpEntries(), ct);

            if (ct.IsCancellationRequested) return;

            _all = entries;
            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_all);
            _view.Filter = RowFilter;
            Grid.ItemsSource = _view;
        }
        catch (OperationCanceledException)
        {
            // Expected when switching tabs or refreshing while a load is in progress
        }
        finally
        {
            Cursor = Cursors.Arrow;
            _loading = false;
        }
        UpdateStatus();
    }

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

    // --- Port tab context menu ---

    private PortOccupant? GetSelectedRow(DataGrid grid)
        => grid.SelectedItem as PortOccupant;

    private void OpenDirectory_Click(object sender, RoutedEventArgs e)
        => OpenDirectoryFor(GetSelectedRow(Grid)?.ProcessPath);

    private void KillProcess_Click(object sender, RoutedEventArgs e)
        => KillProcessFor(GetSelectedRow(Grid)?.Pid, GetSelectedRow(Grid)?.ProcessName);

    private void CopyRow_Click(object sender, RoutedEventArgs e)
        => CopyPortRow(GetSelectedRow(Grid));

    // --- File lock tab ---

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要查询的文件",
            CheckFileExists = true,
        };
        if (ofd.ShowDialog() == true)
        {
            FilePathBox.Text = ofd.FileName;
            QueryFileLocks(ofd.FileName);
        }
    }

    private void FileLock_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void FileLock_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                FilePathBox.Text = files[0];
                QueryFileLocks(files[0]);
            }
        }
        e.Handled = true;
    }

    private async void QueryFileLocks(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            LockStatusText.Text = "文件不存在";
            DropHint.Visibility = Visibility.Visible;
            LockGrid.Visibility = Visibility.Collapsed;
            return;
        }

        // Cancel any previous query still in flight
        _fileLockCts?.Cancel();
        _fileLockCts = new CancellationTokenSource();
        var ct = _fileLockCts.Token;

        Cursor = Cursors.Wait;
        LockStatusText.Text = "正在查询...";
        DropHint.Visibility = Visibility.Collapsed;
        LockGrid.Visibility = Visibility.Visible;

        try
        {
            var lockers = await System.Threading.Tasks.Task.Run(
                () => FileLockInspector.GetFileLockers(filePath), ct);

            if (ct.IsCancellationRequested) return;

            LockGrid.ItemsSource = lockers;

            if (lockers.Count == 0)
            {
                LockStatusText.Text = $"\u201C{filePath}\u201D - 未被任何进程锁定";
                DropHint.Visibility = Visibility.Visible;
                LockGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                LockStatusText.Text = $"\u201C{filePath}\u201D - {lockers.Count} 个进程锁定";
            }
        }
        catch (OperationCanceledException)
        {
            // A newer query superseded this one
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    // --- File lock tab context menu ---

    private FileLockEntry? GetSelectedLockRow()
        => LockGrid.SelectedItem as FileLockEntry;

    private void LockOpenDirectory_Click(object sender, RoutedEventArgs e)
        => OpenDirectoryFor(GetSelectedLockRow()?.ProcessPath);

    private void LockKillProcess_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedLockRow();
        if (KillProcessFor(row?.Pid, row?.ProcessName))
            QueryFileLocks(FilePathBox.Text); // refresh after kill
    }

    private void LockCopyRow_Click(object sender, RoutedEventArgs e)
        => CopyLockRow(GetSelectedLockRow());

    // --- Shared helpers ---

    private void OpenDirectoryFor(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath)) return;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(processPath);
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
            MessageBox.Show($"无法打开目录:\n{ex.Message}", "句柄查看器",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool KillProcessFor(int? pid, string? processName)
    {
        if (pid == null) return false;

        if (SystemPids.Contains(pid.Value))
        {
            MessageBox.Show("无法终止系统内核进程 (PID 0/4)。", "句柄查看器",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (CriticalProcesses.Contains(processName ?? "") || pid.Value == 4)
        {
            var result = MessageBox.Show(
                $"进程 \"{processName}\" (PID {pid}) 是关键系统进程。\n" +
                "终止它可能导致系统不稳定甚至蓝屏。\n\n确定要继续吗？",
                "警告 - 关键进程",
                MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
            if (result != MessageBoxResult.Yes) return false;
        }
        else
        {
            var result = MessageBox.Show(
                $"确定终止进程 \"{processName}\" (PID {pid})？",
                "确认终止进程",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return false;
        }

        try
        {
            var proc = Process.GetProcessById(pid.Value);
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(3000);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"终止进程失败:\n{ex.Message}\n\n可能需要以管理员身份运行。",
                "句柄查看器", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void CopyPortRow(PortOccupant? row)
    {
        if (row == null) return;
        var text = $"PID\t{row.Pid}\n" +
                   $"进程名\t{row.ProcessName}\n" +
                   $"状态\t{row.State}\n" +
                   $"本地\t{row.LocalAddress}:{row.LocalPort}\n" +
                   $"远程\t{row.RemoteAddress}:{row.RemotePort}\n" +
                   $"可执行路径\t{row.ProcessPath}";
        try { Clipboard.SetText(text); }
        catch { }
    }

    private void CopyLockRow(FileLockEntry? row)
    {
        if (row == null) return;
        var text = $"PID\t{row.Pid}\n" +
                   $"进程名\t{row.ProcessName}\n" +
                   $"应用名称\t{row.AppName}\n" +
                   $"启动时间\t{row.StartTime}\n" +
                   $"可执行路径\t{row.ProcessPath}";
        try { Clipboard.SetText(text); }
        catch { }
    }
}
