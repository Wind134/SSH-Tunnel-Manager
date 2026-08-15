using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SSHTunnelManager.Models;
using SSHTunnelManager.Services;
using TinyTools.Core.Updates;
using TinyTools.WinUI.Services;

namespace TinyTools.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private static readonly HttpClient UpdateHttpClient = new();
    private readonly GitHubUpdateService _updateService = new(UpdateHttpClient);
    private CancellationTokenSource? _updateCancellation;
    private UpdateCheckResult? _availableUpdate;

    public SettingsPage()
    {
        InitializeComponent();
        Populate(App.Services.Settings);
        CurrentVersionText.Text = $"当前版本：{CurrentVersion}";
        Unloaded += (_, _) => _updateCancellation?.Cancel();
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
            WindowWidth = App.Services.Settings.WindowWidth,
            WindowHeight = App.Services.Settings.WindowHeight,
        };

        App.Services.SaveSettings(settings);
        ApplyTheme(settings.Theme);
        SavedBar.Severity = InfoBarSeverity.Success;
        SavedBar.Message = "设置已保存。";
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

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        _updateCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        _availableUpdate = null;
        CheckUpdateButton.IsEnabled = false;
        DownloadUpdateButton.Visibility = Visibility.Collapsed;
        OpenReleasePageButton.Visibility = Visibility.Collapsed;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.IsIndeterminate = true;
        UpdateBar.IsOpen = false;

        try
        {
            UpdateCheckResult result = await _updateService.CheckAsync(
                CurrentVersion, cancellation.Token);
            _availableUpdate = result;
            OpenReleasePageButton.Visibility = Visibility.Visible;

            if (!result.IsUpdateAvailable)
            {
                string message = result.CurrentVersion > result.LatestVersion
                    ? $"当前版本 {result.CurrentVersion} 高于公开最新版本 {result.LatestVersion}，可能是预览构建。"
                    : $"当前已是最新版本（{result.LatestVersion}）。";
                ShowUpdateStatus(InfoBarSeverity.Success, message);
            }
            else if (result.Package is null)
            {
                ShowUpdateStatus(
                    InfoBarSeverity.Warning,
                    $"发现新版本 {result.LatestVersion}，但该 Release 尚未提供兼容的 WinUI 更新包。可前往发布页查看详情。");
            }
            else
            {
                DownloadUpdateButton.Visibility = Visibility.Visible;
                ShowUpdateStatus(
                    InfoBarSeverity.Informational,
                    $"发现新版本 {result.LatestVersion}，可下载 {FormatSize(result.Package.Size)} 更新包。");
            }
        }
        catch (OperationCanceledException)
        {
            // Navigating away or starting another check cancels this request.
        }
        catch (Exception ex)
        {
            ShowUpdateStatus(InfoBarSeverity.Error, $"检查更新失败：{ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_updateCancellation, cancellation))
            {
                _updateCancellation = null;
                CheckUpdateButton.IsEnabled = true;
                UpdateProgress.IsIndeterminate = false;
                UpdateProgress.Visibility = Visibility.Collapsed;
            }
            cancellation.Dispose();
        }
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate?.Package is not ReleasePackage package)
            return;

        _updateCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        DownloadUpdateButton.IsEnabled = false;
        CheckUpdateButton.IsEnabled = false;
        UpdateProgress.IsIndeterminate = false;
        UpdateProgress.Value = 0;
        UpdateProgress.Visibility = Visibility.Visible;

        try
        {
            string destinationDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TinyTools",
                "Updates",
                _availableUpdate.LatestVersion.ToString());
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                UpdateProgress.Value = value.Percentage;
                UpdateBar.Message = value.TotalBytes > 0
                    ? $"正在下载… {value.Percentage:0}%"
                    : $"正在下载… {FormatSize(value.BytesReceived)}";
                UpdateBar.Severity = InfoBarSeverity.Informational;
                UpdateBar.IsOpen = true;
            });

            UpdateDownloadResult result = await _updateService.DownloadAsync(
                package, destinationDirectory, progress, cancellation.Token);
            ShowUpdateStatus(InfoBarSeverity.Success, "更新包下载完成，SHA-256 校验通过。");

            if (result.FilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "运行更新安装程序？",
                    Content = "安装程序已经验证。运行后请按提示关闭 TinyTools 并完成覆盖安装。",
                    PrimaryButtonText = "运行安装程序",
                    CloseButtonText = "稍后",
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    Process.Start(new ProcessStartInfo(result.FilePath) { UseShellExecute = true });
            }
            else
            {
                OpenDirectory(destinationDirectory);
                await ProcessActionCoordinator.ShowMessageAsync(
                    XamlRoot,
                    "便携版更新包已就绪",
                    "请退出 TinyTools，解压下载的 ZIP，并覆盖原有程序文件。data 配置目录不会包含在更新包中。未来发布 WinUI 安装包后可直接启动安装程序完成升级。");
            }
        }
        catch (OperationCanceledException)
        {
            ShowUpdateStatus(InfoBarSeverity.Warning, "更新下载已取消。");
        }
        catch (Exception ex)
        {
            ShowUpdateStatus(InfoBarSeverity.Error, $"下载更新失败：{ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_updateCancellation, cancellation))
            {
                _updateCancellation = null;
                DownloadUpdateButton.IsEnabled = true;
                CheckUpdateButton.IsEnabled = true;
                UpdateProgress.Visibility = Visibility.Collapsed;
            }
            cancellation.Dispose();
        }
    }

    private void OpenReleasePage_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null)
            Process.Start(new ProcessStartInfo(_availableUpdate.ReleasePage.AbsoluteUri) { UseShellExecute = true });
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

    private static Version CurrentVersion
    {
        get
        {
            Version version = typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0);
            return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
        }
    }

    private void ShowUpdateStatus(InfoBarSeverity severity, string message)
    {
        UpdateBar.Severity = severity;
        UpdateBar.Message = message;
        UpdateBar.IsOpen = true;
    }

    private static string FormatSize(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.0} MB"
            : $"{Math.Max(0, bytes) / 1024d:0.0} KB";

    private static void OpenDirectory(string directory)
    {
        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add(directory);
        Process.Start(startInfo);
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
