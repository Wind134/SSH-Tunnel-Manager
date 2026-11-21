using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SSHTunnelManager.Models;
using SSHTunnelManager.Services;
using TinyTools.WinUI.Services;

namespace TinyTools.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private UpdateCoordinator Updater => App.Services.UpdateCoordinator;

    public SettingsPage()
    {
        InitializeComponent();
        Populate(App.Services.Settings);
        CurrentVersionText.Text = $"当前版本：v{UpdateCoordinator.GetCurrentVersion()}";
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout(ActualWidth);
        Updater.StateChanged += OnUpdateStateChanged;
        RenderUpdateState(Updater.CurrentState);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Updater.StateChanged -= OnUpdateStateChanged;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyResponsiveLayout(e.NewSize.Width);

    // 窄屏时缩小边距并将双列选项切换为单列堆叠
    private void ApplyResponsiveLayout(double width)
    {
        bool narrow = width < 600;
        LayoutRoot.Padding = narrow ? new Thickness(16, 14, 16, 14) : new Thickness(24, 20, 24, 20);
        Grid.SetColumnSpan(ThemeBox, narrow ? 2 : 1);
        Grid.SetRow(StartPageBox, narrow ? 1 : 0);
        Grid.SetColumn(StartPageBox, narrow ? 0 : 1);
        Grid.SetColumnSpan(StartPageBox, narrow ? 2 : 1);
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
            CheckUpdatesOnStartup = CheckUpdatesOnStartupBox.IsChecked == true,
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
        => await Updater.CheckForUpdatesAsync();

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
        => await Updater.DownloadUpdateAsync();

    private void CancelUpdate_Click(object sender, RoutedEventArgs e)
        => Updater.CancelCurrentOperation();

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateStateSnapshot state = Updater.CurrentState;
        string? filePath = state.DownloadResult?.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        if (!filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                OpenDirectory(directory);
            await ProcessActionCoordinator.ShowMessageAsync(
                XamlRoot,
                "便携版更新包已就绪",
                "请退出 TinyTools，解压下载的 ZIP 并覆盖原程序文件，data 目录不受影响。");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"安装 TinyTools v{state.CheckResult?.LatestVersion}？",
            Content = "安装程序已通过 SHA-256 校验。点击“立即安装”后，TinyTools 将自动关闭并启动安装向导完成覆盖升级，用户配置会被保留。",
            PrimaryButtonText = "立即安装",
            CloseButtonText = "稍后",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        if (Updater.TryLaunchDownloadedInstaller(out string error))
        {
            App.MainWindow.ForceExitForUpdate();
        }
        else
        {
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Message = error;
            UpdateBar.IsOpen = true;
        }
    }

    private void OpenReleasePage_Click(object sender, RoutedEventArgs e)
    {
        Uri? page = Updater.CurrentState.CheckResult?.ReleasePage;
        if (page is not null)
            Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
    }

    private void OnUpdateStateChanged(UpdateStateSnapshot snapshot)
        => RenderUpdateState(snapshot);

    private void RenderUpdateState(UpdateStateSnapshot state)
    {
        bool busy = state.Phase is UpdatePhase.Checking or UpdatePhase.Downloading;
        CheckUpdateButton.IsEnabled = !busy;
        CancelUpdateButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        bool canDownload = state.Phase == UpdatePhase.UpdateAvailable
            && state.CheckResult?.Package is not null;
        DownloadUpdateButton.Visibility = canDownload ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.Visibility = state.Phase == UpdatePhase.ReadyToInstall
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpenReleasePageButton.Visibility = state.CheckResult is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.IsIndeterminate = state.Phase == UpdatePhase.Checking
            || (state.Phase == UpdatePhase.Downloading && state.TotalBytes <= 0);
        if (state.Phase == UpdatePhase.Downloading && state.TotalBytes > 0)
            UpdateProgress.Value = state.ProgressPercentage;

        if (string.IsNullOrWhiteSpace(state.StatusMessage))
        {
            UpdateBar.IsOpen = false;
        }
        else
        {
            UpdateBar.Severity = state.Phase switch
            {
                UpdatePhase.UpToDate or UpdatePhase.ReadyToInstall => InfoBarSeverity.Success,
                UpdatePhase.Failed => InfoBarSeverity.Error,
                UpdatePhase.UpdateAvailable when state.CheckResult?.Package is null => InfoBarSeverity.Warning,
                _ => InfoBarSeverity.Informational,
            };
            UpdateBar.Message = state.StatusMessage;
            UpdateBar.IsOpen = true;
        }

        string notes = state.CheckResult?.ReleaseNotes?.Trim() ?? string.Empty;
        if (state.CheckResult?.IsUpdateAvailable == true && !string.IsNullOrWhiteSpace(notes))
        {
            ReleaseNotesText.Text = notes;
            ReleaseNotesContainer.Visibility = Visibility.Visible;
        }
        else
        {
            ReleaseNotesContainer.Visibility = Visibility.Collapsed;
        }
    }

    private void Populate(AppSettings settings)
    {
        AutoStartMinimizedBox.IsChecked = settings.AutoStartMinimized;
        MinimizeToTrayBox.IsChecked = settings.MinimizeToTrayOnClose;
        ConfirmBeforeExitBox.IsChecked = settings.ConfirmBeforeExit;
        ShowTrayNotificationsBox.IsChecked = settings.ShowTrayNotifications;
        ShowSystemProcessesBox.IsChecked = settings.ShowSystemProcesses;
        CheckUpdatesOnStartupBox.IsChecked = settings.CheckUpdatesOnStartup;
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
