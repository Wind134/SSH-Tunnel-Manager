using System.Collections.ObjectModel;
using HandleViewer.Models;
using HandleViewer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TinyTools.WinUI.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace TinyTools.WinUI.Pages;

public sealed partial class FileLocksPage : Page
{
    private readonly ObservableCollection<FileLockEntry> _items = new();
    private CancellationTokenSource? _queryCancellation;

    public FileLocksPage()
    {
        InitializeComponent();
        LockList.ItemsSource = _items;
        Unloaded += (_, _) => CancelQuery();
    }

    private async void QueryButton_Click(object sender, RoutedEventArgs e)
        => await QueryAsync(PathBox.Text);

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelQuery();
        QueryStatus.Text = "查询已取消。";
    }

    private async void PathBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        e.Handled = true;
        await QueryAsync(PathBox.Text);
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "查询该文件或文件夹的占用进程";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
        string? path = items.FirstOrDefault()?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            QueryStatus.Text = "无法读取拖入项目的本地路径。";
            return;
        }

        PathBox.Text = path;
        await QueryAsync(path);
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        PathBox.Text = file.Path;
        await QueryAsync(file.Path);
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return;

        PathBox.Text = folder.Path;
        await QueryAsync(folder.Path);
    }

    private async Task QueryAsync(string? path)
    {
        path = path?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            QueryStatus.Text = "请输入、选择或拖入文件/文件夹路径。";
            return;
        }

        CancelQuery();
        var cancellation = new CancellationTokenSource();
        _queryCancellation = cancellation;
        QueryButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        QueryProgress.IsActive = true;
        QueryStatus.Text = Directory.Exists(path) ? "正在扫描文件夹并查询占用…" : "正在查询文件占用…";

        try
        {
            PathLockQueryResult result = await Task.Run(
                () => FileLockInspector.GetPathLockers(path, cancellation.Token),
                cancellation.Token);
            if (cancellation.IsCancellationRequested)
                return;

            _items.Clear();
            foreach (FileLockEntry entry in result.Entries)
                _items.Add(entry);

            QueryStatus.Text = string.IsNullOrEmpty(result.ErrorMessage)
                ? BuildStatus(result)
                : $"查询失败：{result.ErrorMessage}";
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_queryCancellation, cancellation))
                QueryStatus.Text = "查询已取消。";
        }
        catch (Exception ex)
        {
            QueryStatus.Text = $"查询失败：{ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_queryCancellation, cancellation))
            {
                _queryCancellation = null;
                QueryButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                QueryProgress.IsActive = false;
            }
            cancellation.Dispose();
        }
    }

    private static string BuildStatus(PathLockQueryResult result)
    {
        string occupation = result.Entries.Count == 0
            ? "未发现占用进程"
            : $"发现 {result.Entries.Count} 个占用进程";
        if (!result.IsDirectory)
            return $"“{result.QueriedPath}” — {occupation}";

        var details = new List<string> { $"已扫描 {result.ScannedFileCount} 个文件" };
        if (result.SkippedDirectoryCount > 0)
            details.Add($"跳过 {result.SkippedDirectoryCount} 个无法访问的目录");
        if (result.WasTruncated)
            details.Add($"达到 {FileLockInspector.MaxDirectoryFileCount} 个文件上限，结果可能不完整");
        if (result.ScannedFileCount == 0)
            details.Add("没有可扫描文件；Windows 接口无法检查文件夹自身句柄");
        return $"“{result.QueriedPath}” — {occupation}；{string.Join("；", details)}";
    }

    private async void OpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FileLockEntry entry)
            await ProcessActionCoordinator.OpenExecutableDirectoryAsync(XamlRoot, entry.ProcessPath);
    }

    private async void TerminateProcess_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FileLockEntry entry)
            return;

        if (await ProcessActionCoordinator.ConfirmAndTerminateAsync(
            XamlRoot, entry.Pid, entry.ProcessName))
        {
            await QueryAsync(PathBox.Text);
        }
    }

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FileLockEntry entry)
            return;

        var package = new DataPackage();
        package.SetText(
            $"PID\t{entry.Pid}\n进程名\t{entry.ProcessName}\n应用名称\t{entry.AppName}\n" +
            $"启动时间\t{entry.StartTime}\n可执行路径\t{entry.ProcessPath}");
        Clipboard.SetContent(package);
    }

    private static void InitializePicker(object picker)
    {
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
    }

    private void CancelQuery()
    {
        _queryCancellation?.Cancel();
    }
}
