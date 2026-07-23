using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SSHTunnelManager;
using SSHTunnelManager.Models;
using SSHTunnelManager.Services;

namespace TinyTools;

public partial class MainWindow : Window
{
    private MainView? _tunnelView;
    private HandleViewer.MainView? _handleView;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private AppSettings _settings;
    private bool _forceClose;
    private bool _initialStartupApplied;

    public MainWindow()
    {
        _settings = LoadSettings();

        InitializeComponent();
        ThemeHelper.Apply(_settings.Theme);
        SwitchTo(GetInitialPage(), persistSelection: false);
        Closing += OnClosing;
        ContentRendered += OnContentRendered;
    }

    private static AppSettings LoadSettings()
    {
        try
        {
            return ConfigStorage.Load().Settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private string GetInitialPage() => _settings.StartPage switch
    {
        "Tunnel" => "NavTunnel",
        "HandleViewer" => "NavHandle",
        _ => _settings.LastPage == "HandleViewer" ? "NavHandle" : "NavTunnel"
    };

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (_initialStartupApplied)
            return;
        _initialStartupApplied = true;

        if (_settings.AutoStartMinimized)
            MinimizeToTray(showNotification: false);
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            SwitchTo(btn.Name, persistSelection: true);
    }

    private void SwitchTo(string navName, bool persistSelection)
    {
        ResetNavStyles();

        if (FindName(navName) is Button active)
            active.Style = (Style)FindResource("NavButtonActive");

        switch (navName)
        {
            case "NavTunnel":
                _tunnelView ??= new MainView();
                ContentArea.Content = _tunnelView;
                _settings.LastPage = "Tunnel";
                break;
            case "NavHandle":
                _handleView ??= new HandleViewer.MainView();
                _handleView.ApplySettings(
                    _settings.PortAutoRefreshSeconds,
                    _settings.ShowSystemProcesses);
                ContentArea.Content = _handleView;
                _settings.LastPage = "HandleViewer";
                break;
        }

        if (persistSelection)
            SaveSettings();
    }

    private void ResetNavStyles()
    {
        NavTunnel.Style = (Style)FindResource("NavButton");
        NavHandle.Style = (Style)FindResource("NavButton");
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Views.SettingsDialog(_settings) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _settings = dialog.GetSettings();
        ThemeHelper.Apply(_settings.Theme);
        _handleView?.ApplySettings(
            _settings.PortAutoRefreshSeconds,
            _settings.ShowSystemProcesses);
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            ConfigStorage.SaveSettings(_settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"保存设置失败：\n{ex.Message}",
                "TinyTools",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => RequestExit();

    private void RequestExit()
    {
        if (!ConfirmExit())
            return;

        _forceClose = true;
        Close();
    }

    private bool ConfirmExit()
    {
        if (!_settings.ConfirmBeforeExit)
            return true;

        return MessageBox.Show(
            "退出 TinyTools？\n\n正在运行的 SSH 隧道将被停止。",
            "确认退出",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_forceClose && _settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            MinimizeToTray(showNotification: _settings.ShowTrayNotifications);
            return;
        }

        if (!_forceClose && !ConfirmExit())
        {
            e.Cancel = true;
            return;
        }

        _forceClose = true;
        SaveSettings();
        _tunnelView?.OnShellClosing();
        _notifyIcon?.Dispose();
    }

    private void MinimizeToTray(bool showNotification)
    {
        Hide();
        SetupTray();
        if (showNotification)
        {
            _notifyIcon?.ShowBalloonTip(
                2500,
                "TinyTools",
                "已最小化到托盘，后台继续运行。右键托盘图标可退出。",
                System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void SetupTray()
    {
        if (_notifyIcon != null)
            return;

        System.Drawing.Icon? appIcon = null;
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                appIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        }
        catch
        {
            // Fall back to the system application icon.
        }

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = appIcon ?? System.Drawing.SystemIcons.Application,
            Text = "TinyTools",
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => RestoreFromTray());
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(() =>
        {
            RestoreFromTray();
            Settings_Click(this, new RoutedEventArgs());
        }));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(RequestExit));
        _notifyIcon.ContextMenuStrip = menu;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
