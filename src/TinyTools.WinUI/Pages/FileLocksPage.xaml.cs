using HandleViewer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TinyTools.WinUI.Pages;

public sealed partial class FileLocksPage : Page
{
    public FileLocksPage() => InitializeComponent();

    private async void QueryButton_Click(object sender, RoutedEventArgs e)
    {
        QueryButton.IsEnabled = false;
        QueryStatus.Text = "正在查询…";
        try
        {
            var result = await Task.Run(() => FileLockInspector.GetPathLockers(PathBox.Text));
            LockList.ItemsSource = result.Entries;
            QueryStatus.Text = string.IsNullOrEmpty(result.ErrorMessage)
                ? $"已扫描 {result.ScannedFileCount} 个文件，发现 {result.Entries.Count} 个占用进程。"
                : result.ErrorMessage;
        }
        catch (Exception ex)
        {
            QueryStatus.Text = $"查询失败：{ex.Message}";
        }
        finally
        {
            QueryButton.IsEnabled = true;
        }
    }
}
