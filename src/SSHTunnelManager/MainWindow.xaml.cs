using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using SSHTunnelManager.Services;
using SSHTunnelManager.ViewModels;

namespace SSHTunnelManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TunnelManager _tunnelManager;
    private readonly ConfigStorage _configStorage;
    private NotifyIcon? _notifyIcon;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();

        _tunnelManager = new TunnelManager();
        _configStorage = new ConfigStorage();
        _viewModel = new MainViewModel(_tunnelManager, _configStorage);
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();

        // Apply saved theme and auto-minimize preference.
        ThemeHelper.Apply(_viewModel.Settings.Theme);
        if (_viewModel.Settings.AutoStartMinimized)
            WindowState = WindowState.Minimized;

        SetupTray();

        // Persist immediately when a host key is trusted, so the trust survives
        // a "minimize to tray" close (which otherwise skips SaveConfig on exit).
        _tunnelManager.ConfigChanged += () => Dispatcher.Invoke(_viewModel.SaveConfig);

        _tunnelManager.OnHostKeyReceived += (state, fingerprint, algoName) =>
        {
            bool result = false;
            Dispatcher.Invoke(() =>
            {
                var dialog = new Views.HostKeyDialog(state, fingerprint, algoName);
                dialog.Owner = this;
                result = dialog.ShowDialog() == true;
            });
            return result;
        };
    }

    private void SetupTray()
    {
        // Use the icon embedded in the EXE (via <ApplicationIcon> in the .csproj)
        // instead of the generic Windows SystemIcons.Application.
       System.Drawing.Icon? appIcon = null;
       try
       {
            // Assembly.Location is "" under single-file publish (.NET 6+), so
            // Environment.ProcessPath (always the real host exe) + AppContext.BaseDirectory
            // fallback gives a path that ExtractAssociatedIcon can actually open.
            var exePath = System.Environment.ProcessPath
                          ?? System.IO.Path.Combine(AppContext.BaseDirectory, "SSHTunnelManager.exe");
            if (!string.IsNullOrEmpty(exePath))
                appIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        }
        catch { /* fall back to default on any failure */ }

        _notifyIcon = new NotifyIcon
        {
            Icon = appIcon ?? SystemIcons.Application,
            Text = "SSH Tunnel Manager",
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => RestoreFromTray());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        _notifyIcon.ContextMenuStrip = menu;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _forceClose = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Minimize to tray instead of quitting when the user closes the window.
        if (!_forceClose && _viewModel.Settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            _notifyIcon?.ShowBalloonTip(
                2500, "SSH Tunnel Manager",
                "已最小化到托盘，后台继续运行。右键托盘图标可退出。",
                ToolTipIcon.Info);
            return;
        }

        _viewModel.SaveConfig();

        _ = _tunnelManager.StopAllAsync();
        _tunnelManager.Dispose();
        _notifyIcon?.Dispose();
    }
}
