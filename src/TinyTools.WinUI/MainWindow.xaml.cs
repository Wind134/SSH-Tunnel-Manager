using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SSHTunnelManager.Services;
using TinyTools.Tray;
using TinyTools.WinUI.Pages;
using TinyTools.WinUI.Services;

namespace TinyTools.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private TrayIconService? _trayIcon;
    private bool _forceClose;
    private bool _cleanupComplete;
    private bool _initialActivationApplied;
    private bool _exitConfirmationPending;
    private bool _updateBannerDismissed;
    private readonly WindowPlacementManager _windowPlacement;

    public FrameworkElement RootElement => RootGrid;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("app.ico");

        _windowPlacement = new WindowPlacementManager(
            this,
            () => App.Services.Settings,
            ConfigStorage.SaveSettings);
        _windowPlacement.Initialize();

        AppWindow.Closing += OnAppWindowClosing;
        Activated += OnActivated;
        App.Services.TunnelManager.HostKeyConfirmationRequested += ConfirmHostKeyAsync;
        App.Services.UpdateCoordinator.StateChanged += OnUpdateStateChanged;
        UpdateBanner.CloseButtonClick += (_, _) => _updateBannerDismissed = true;

        // 仅 Windows 11 启用 Mica，Windows 10 自动回退到标准主题背景
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };

        RootGrid.RequestedTheme = App.Services.Settings.Theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        string initialTag = GetInitialNavigationTag();
        Navigation.SelectedItem = Navigation.MenuItems
            .OfType<NavigationViewItem>()
            .First(item => string.Equals(item.Tag?.ToString(), initialTag, StringComparison.Ordinal));
        Navigate(initialTag switch
        {
            "ports" => typeof(PortsPage),
            "locks" => typeof(FileLocksPage),
            _ => typeof(TunnelsPage),
        });

        _ = App.Services.UpdateCoordinator.CleanupObsoleteInstallersAsync();

        if (App.Services.Settings.CheckUpdatesOnStartup)
            _ = App.Services.UpdateCoordinator.CheckSilentlyOnStartupAsync();
    }

    /// <summary>
    /// 安装升级时强制退出主窗口，跳过托盘隐藏与二次确认。
    /// </summary>
    public void ForceExitForUpdate()
    {
        _forceClose = true;
        Close();
    }

    private static string GetInitialNavigationTag()
    {
        string page = App.Services.Settings.StartPage == "LastUsed"
            ? App.Services.Settings.LastPage
            : App.Services.Settings.StartPage;
        return page switch
        {
            "HandleViewer" => "ports",
            "FileLocks" => "locks",
            _ => "tunnels",
        };
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_initialActivationApplied)
            return;

        _initialActivationApplied = true;
        if (App.Services.Settings.AutoStartMinimized)
            MinimizeToTray(showNotification: false);
    }

    private void OnUpdateStateChanged(UpdateStateSnapshot snapshot)
    {
        bool hasNewVersion = snapshot.Phase is UpdatePhase.UpdateAvailable or UpdatePhase.ReadyToInstall
            && snapshot.CheckResult?.IsUpdateAvailable == true;

        if (!hasNewVersion)
        {
            UpdateBanner.IsOpen = false;
            return;
        }

        // 已在设置页或用户手动关闭横幅时不再重复弹出
        if (_updateBannerDismissed || ContentFrame.CurrentSourcePageType == typeof(SettingsPage))
            return;

        UpdateBanner.Title = $"发现新版本 v{snapshot.CheckResult!.LatestVersion}";
        UpdateBanner.Message = snapshot.Phase == UpdatePhase.ReadyToInstall
            ? "安装包已下载完成，点击前往设置页立即安装。"
            : "新版本已发布，支持直接下载安装包完成升级。";
        UpdateBanner.IsOpen = true;
    }

    private void GoToUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateBanner.IsOpen = false;
        NavigateToSettings();
    }

    private void NavigateToSettings()
    {
        Navigation.SelectedItem = Navigation.SettingsItem;
        Navigate(typeof(SettingsPage));
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            UpdateBanner.IsOpen = false;
            Navigate(typeof(SettingsPage));
            return;
        }

        string? tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag?.ToString();
        if (tag is "tunnels" or "ports" or "locks")
        {
            App.Services.Settings.LastPage = tag switch
            {
                "ports" => "HandleViewer",
                "locks" => "FileLocks",
                _ => "Tunnel",
            };
            ConfigStorage.SaveSettings(App.Services.Settings);
        }

        Navigate(tag switch
        {
            "tunnels" => typeof(TunnelsPage),
            "ports" => typeof(PortsPage),
            "locks" => typeof(FileLocksPage),
            _ => typeof(TunnelsPage),
        });
    }

    private void Navigate(Type pageType)
    {
        if (ContentFrame.CurrentSourcePageType == pageType)
            return;

        ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_forceClose)
        {
            Cleanup();
            return;
        }

        args.Cancel = true;
        if (App.Services.Settings.MinimizeToTrayOnClose)
        {
            MinimizeToTray(App.Services.Settings.ShowTrayNotifications);
            return;
        }

        if (_exitConfirmationPending)
            return;

        _exitConfirmationPending = true;
        try
        {
            if (await ConfirmExitAsync())
            {
                _forceClose = true;
                Close();
            }
        }
        finally
        {
            _exitConfirmationPending = false;
        }
    }

    private async Task<bool> ConfirmExitAsync()
    {
        if (!App.Services.Settings.ConfirmBeforeExit)
            return true;

        await _dialogGate.WaitAsync();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "退出 TinyTools？",
                Content = "正在运行的 SSH 隧道将被停止。",
                PrimaryButtonText = "退出",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    private async Task<bool> ConfirmHostKeyAsync(
        HostKeyConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dialogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ContentDialog? activeDialog = null;
        using var registration = cancellationToken.Register(() =>
        {
            completion.TrySetResult(false);
            DispatcherQueue.TryEnqueue(() => activeDialog?.Hide());
        });

        try
        {
            bool queued = DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetResult(false);
                        return;
                    }

                    var content = new StackPanel { Spacing = 10, MaxWidth = 480 };
                    content.Children.Add(new TextBlock
                    {
                        Text = request.IsChangedKey
                            ? "服务器提供的主机密钥与已保存的密钥不同。除非你确认服务器密钥已合法变更，否则不要继续。"
                            : "这是首次连接到该服务器。请核对主机密钥指纹。",
                        TextWrapping = TextWrapping.Wrap,
                    });
                    content.Children.Add(new TextBlock
                    {
                        Text = $"算法：{request.Algorithm}",
                    });
                    content.Children.Add(new TextBox
                    {
                        Text = request.Fingerprint,
                        IsReadOnly = true,
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    });

                    activeDialog = new ContentDialog
                    {
                        XamlRoot = RootGrid.XamlRoot,
                        Title = request.IsChangedKey ? "主机密钥已变化" : "确认主机密钥",
                        Content = content,
                        PrimaryButtonText = "信任并连接",
                        CloseButtonText = "拒绝",
                        DefaultButton = ContentDialogButton.Close,
                    };
                    completion.TrySetResult(
                        await activeDialog.ShowAsync() == ContentDialogResult.Primary);
                }
                catch (Exception)
                {
                    completion.TrySetResult(false);
                }
                finally
                {
                    activeDialog = null;
                }
            });

            return queued && await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    private async Task RequestExitAsync()
    {
        RestoreAndActivate();
        if (_exitConfirmationPending)
            return;

        _exitConfirmationPending = true;
        try
        {
            if (!await ConfirmExitAsync())
                return;

            _forceClose = true;
            Close();
        }
        finally
        {
            _exitConfirmationPending = false;
        }
    }

    private void MinimizeToTray(bool showNotification)
    {
        SetupTray();
        AppWindow.Hide();

        if (showNotification)
        {
            _trayIcon?.ShowInformation(
                "TinyTools",
                "已最小化到托盘，SSH 隧道会继续在后台运行。");
        }
    }

    private void SetupTray()
    {
        if (_trayIcon is not null)
            return;

        _trayIcon = new TrayIconService("TinyTools", Environment.ProcessPath);
        _trayIcon.OpenRequested += () => DispatcherQueue.TryEnqueue(RestoreAndActivate);
        _trayIcon.SettingsRequested += () => DispatcherQueue.TryEnqueue(() =>
        {
            RestoreAndActivate();
            NavigateToSettings();
        });
        _trayIcon.ExitRequested += () => DispatcherQueue.TryEnqueue(async () =>
            await RequestExitAsync());
    }

    public void RestoreAndActivate()
    {
        AppWindow.Show();
        Activate();
        AppWindow.MoveInZOrderAtTop();
    }

    private void Cleanup()
    {
        if (_cleanupComplete)
            return;

        _cleanupComplete = true;
        _windowPlacement.Dispose();
        App.Services.TunnelManager.HostKeyConfirmationRequested -= ConfirmHostKeyAsync;
        App.Services.UpdateCoordinator.StateChanged -= OnUpdateStateChanged;
        _trayIcon?.Dispose();
        App.ShutdownServices();
    }
}
