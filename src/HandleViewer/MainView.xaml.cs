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

    // --- File/folder lock tab ---

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要查询的文件",
            CheckFileExists = true,
        };
        if (ofd.ShowDialog() == true)
        {
            FilePathBox.Text = ofd.FileName;
            QueryPathLocks(ofd.FileName);
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择要查询的文件夹",
            Multiselect = false,
        };
        if (dialog.ShowDialog() == true)
        {
            FilePathBox.Text = dialog.FolderName;
            QueryPathLocks(dialog.FolderName);
        }
    }

    private void FilePathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        QueryPathLocks(FilePathBox.Text);
        e.Handled = true;
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
                QueryPathLocks(files[0]);
            }
        }
        e.Handled = true;
    }

    private async void QueryPathLocks(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path)))
        {
            LockStatusText.Text = "路径不存在";
            DropHint.Visibility = Visibility.Visible;
            LockGrid.Visibility = Visibility.Collapsed;
            return;
        }

        bool isDirectory = System.IO.Directory.Exists(path);

        // Cancel any previous query still in flight
        _fileLockCts?.Cancel();
        _fileLockCts = new CancellationTokenSource();
        var ct = _fileLockCts.Token;

        Cursor = Cursors.Wait;
        LockStatusText.Text = isDirectory ? "正在扫描文件夹并查询占用..." : "正在查询文件占用...";
        DropHint.Visibility = Visibility.Collapsed;
        LockGrid.Visibility = Visibility.Visible;

        try
        {
            var result = await System.Threading.Tasks.Task.Run(
                () => FileLockInspector.GetPathLockers(path, ct), ct);

            if (ct.IsCancellationRequested) return;

            LockGrid.ItemsSource = result.Entries;

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                LockStatusText.Text = $"查询失败：{result.ErrorMessage}";
                DropHint.Visibility = Visibility.Visible;
                LockGrid.Visibility = Visibility.Collapsed;
                return;
            }

            LockStatusText.Text = BuildLockStatus(result);
            if (result.Entries.Count == 0)
            {
                DropHint.Visibility = Visibility.Visible;
                LockGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                DropHint.Visibility = Visibility.Collapsed;
                LockGrid.Visibility = Visibility.Visible;
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

    private static string BuildLockStatus(PathLockQueryResult result)
    {
        string occupation = result.Entries.Count == 0
            ? "未发现占用进程"
            : $"发现 {result.Entries.Count} 个占用进程";

        if (!result.IsDirectory)
            return $"“{result.QueriedPath}” - {occupation}";

        var scanDetails = new List<string>
        {
            $"已扫描 {result.ScannedFileCount} 个文件",
        };
        if (result.SkippedDirectoryCount > 0)
            scanDetails.Add($"跳过 {result.SkippedDirectoryCount} 个无法访问的目录");
        if (result.WasTruncated)
            scanDetails.Add($"达到 {FileLockInspector.MaxDirectoryFileCount} 个文件上限，结果可能不完整");
        if (result.ScannedFileCount == 0)
            scanDetails.Add("没有可扫描文件；系统接口无法检查文件夹自身句柄");

        return $"“{result.QueriedPath}” - {occupation}；{string.Join("；", scanDetails)}";
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
            QueryPathLocks(FilePathBox.Text); // refresh after kill
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
