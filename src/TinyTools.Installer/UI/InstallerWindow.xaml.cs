using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TinyTools.Installer.Models;
using TinyTools.Installer.Services;

namespace TinyTools.Installer.UI;

public partial class InstallerWindow : Window
{
    private readonly InstallerLaunchOptions _options;
    private readonly InstallEngine _engine = new();
    private CancellationTokenSource? _executionCts;
    private InstallExecutionResult? _lastResult;
    private bool _isExecuting;

    public InstallerWindow(InstallerLaunchOptions options)
    {
        InitializeComponent();
        _options = options;

        VersionBadgeText.Text = $"v{InstallEngine.GetInstallerVersion()}";
        InstallPathBox.Text = options.InstallDirectory;
        ConfigureInitialMode(options.Mode);

        Loaded += OnLoaded;
    }

    private void ConfigureInitialMode(InstallerMode mode)
    {
        switch (mode)
        {
            case InstallerMode.Upgrade:
                StepIndicatorText.Text = "版本升级";
                HeadlineText.Text = "升级 TinyTools";
                SubheadlineText.Text = "将覆盖更新程序文件，您的 SSH 隧道与设置配置会完整保留。";
                PrimaryButton.Content = "立即升级";
                break;

            case InstallerMode.Uninstall:
                StepIndicatorText.Text = "卸载程序";
                HeadlineText.Text = "卸载 TinyTools";
                SubheadlineText.Text = "将移除程序文件与快捷方式，个人配置（data 目录）默认保留。";
                InstallPathBox.IsReadOnly = true;
                BrowseButton.Visibility = Visibility.Collapsed;
                ShortcutOptionsPanel.Visibility = Visibility.Collapsed;
                PrimaryButton.Content = "确认卸载";
                break;

            default:
                StepIndicatorText.Text = "安装配置";
                HeadlineText.Text = "准备安装 TinyTools";
                SubheadlineText.Text = "请选择安装位置和快捷方式选项，整个过程只需数秒。";
                PrimaryButton.Content = "开始安装";
                break;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 从主程序触发的自动升级直接进入安装进度
        if (_options.AutoStart && _options.Mode == InstallerMode.Upgrade)
            await StartExecutionAsync();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_isExecuting)
            return;
        Close();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 TinyTools 安装目录",
            InitialDirectory = InstallPathBox.Text,
        };
        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            InstallPathBox.Text = dialog.FolderName;
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is not null)
        {
            if (_lastResult.Succeeded
                && _options.Mode != InstallerMode.Uninstall
                && !string.IsNullOrWhiteSpace(_lastResult.InstalledExecutablePath))
            {
                InstallEngine.LaunchApplication(_lastResult.InstalledExecutablePath);
            }
            Close();
            return;
        }

        await StartExecutionAsync();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isExecuting)
        {
            _executionCts?.Cancel();
            SecondaryButton.IsEnabled = false;
            return;
        }

        Close();
    }

    private async Task StartExecutionAsync()
    {
        if (_isExecuting)
            return;

        _isExecuting = true;
        _executionCts = new CancellationTokenSource();

        ConfigStepPanel.Visibility = Visibility.Collapsed;
        ResultStepPanel.Visibility = Visibility.Collapsed;
        ProgressStepPanel.Visibility = Visibility.Visible;
        CloseTitleButton.IsEnabled = false;
        PrimaryButton.Visibility = Visibility.Collapsed;
        SecondaryButton.Content = "取消";
        SecondaryButton.IsEnabled = true;

        StepIndicatorText.Text = _options.Mode == InstallerMode.Uninstall ? "正在卸载" : "正在安装";
        ProgressTitleText.Text = _options.Mode switch
        {
            InstallerMode.Upgrade => "正在升级 TinyTools…",
            InstallerMode.Uninstall => "正在卸载 TinyTools…",
            _ => "正在安装 TinyTools…",
        };

        var request = new InstallRequest(
            _options.Mode,
            InstallPathBox.Text.Trim(),
            _options.WaitProcessId,
            DesktopShortcutCheck.IsChecked == true,
            StartMenuShortcutCheck.IsChecked == true,
            LaunchAfterInstallCheck.IsChecked == true);

        var progress = new Progress<InstallProgressInfo>(info =>
        {
            InstallProgressBar.Value = info.Percentage;
            ProgressPercentText.Text = $"{info.Percentage:0}%";
            ProgressDetailText.Text = info.Message;
        });

        InstallExecutionResult result = await _engine.ExecuteAsync(request, progress, _executionCts.Token);
        _lastResult = result;
        _isExecuting = false;
        _executionCts.Dispose();
        _executionCts = null;

        ShowResultStep(result, request);
    }

    private void ShowResultStep(InstallExecutionResult result, InstallRequest request)
    {
        ProgressStepPanel.Visibility = Visibility.Collapsed;
        ResultStepPanel.Visibility = Visibility.Visible;
        CloseTitleButton.IsEnabled = true;
        StepIndicatorText.Text = result.Succeeded ? "已完成" : "操作未完成";

        if (result.Succeeded)
        {
            ResultBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7"));
            ResultBadgeIcon.Text = "✓";
            ResultBadgeIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15803D"));
            ResultTitleText.Text = request.Mode switch
            {
                InstallerMode.Upgrade => "升级完成",
                InstallerMode.Uninstall => "卸载完成",
                _ => "安装完成",
            };
            ResultMessageText.Text = result.Message;

            if (request.Mode != InstallerMode.Uninstall && !string.IsNullOrWhiteSpace(result.InstalledExecutablePath))
            {
                PrimaryButton.Content = "立即启动 TinyTools";
                PrimaryButton.Visibility = Visibility.Visible;
                SecondaryButton.Content = "关闭";
                SecondaryButton.IsEnabled = true;

                // 升级模式且勾选自动启动时直接拉起新版
                if (request.Mode == InstallerMode.Upgrade && request.LaunchAfterInstall)
                {
                    InstallEngine.LaunchApplication(result.InstalledExecutablePath);
                    Close();
                }
            }
            else
            {
                PrimaryButton.Content = "完成";
                PrimaryButton.Visibility = Visibility.Visible;
                SecondaryButton.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            ResultBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2"));
            ResultBadgeIcon.Text = "!";
            ResultBadgeIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
            ResultTitleText.Text = "操作失败";
            ResultMessageText.Text = result.Message;
            PrimaryButton.Content = "关闭";
            PrimaryButton.Visibility = Visibility.Visible;
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
    }
}
