using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SSHTunnelManager;

namespace TinyTools;

public partial class MainWindow : Window
{
    private MainView? _tunnelView;
    private HandleViewer.MainView? _handleView;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        SwitchTo("NavTunnel");
        Closing += OnClosing;
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            SwitchTo(btn.Name);
    }

    private void SwitchTo(string navName)
    {
        ResetNavStyles();

        // Highlight active nav
        var active = (Button)FindName(navName);
        if (active != null)
            active.Style = (Style)FindResource("NavButtonActive");

        // Swap content
        switch (navName)
        {
            case "NavTunnel":
            _tunnelView ??= new MainView();
            ContentArea.Content = _tunnelView;
            break;
        case "NavHandle":
            _handleView ??= new HandleViewer.MainView();
            ContentArea.Content = _handleView;
            break;
        }
    }

    private void ResetNavStyles()
    {
        NavTunnel.Style = (Style)FindResource("NavButton");
        NavHandle.Style = (Style)FindResource("NavButton");
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _forceClose = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_forceClose)
        {
            // Minimize to tray instead of exiting.
            e.Cancel = true;
            Hide();
            SetupTray();
            _notifyIcon?.ShowBalloonTip(2500, "TinyTools",
                "已最小化到托盘，后台继续运行。右键托盘图标可退出。",
                System.Windows.Forms.ToolTipIcon.Info);
            return;
        }

        // Clean up child views
        _tunnelView?.OnShellClosing();
        _notifyIcon?.Dispose();
    }

    private void SetupTray()
    {
        if (_notifyIcon != null) return;

        System.Drawing.Icon? appIcon = null;
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                appIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        }
        catch { }

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = appIcon ?? System.Drawing.SystemIcons.Application,
            Text = "TinyTools",
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => RestoreFromTray());
        menu.Items.Add("退出", null, (_, _) =>
        {
            _forceClose = true;
            Dispatcher.Invoke(Close);
        });
        _notifyIcon.ContextMenuStrip = menu;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
