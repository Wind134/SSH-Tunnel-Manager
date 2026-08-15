using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SSHTunnelManager.Models;
using TinyTools.Core.Windowing;
using Windows.Graphics;

namespace TinyTools.WinUI.Services;

/// <summary>
/// Applies DPI-aware startup dimensions, keeps the restored window usable, and
/// persists the last stable restored size without writing on every resize tick.
/// </summary>
internal sealed class WindowPlacementManager : IDisposable
{
    private const double DefaultDpi = 96d;

    private readonly Window _window;
    private readonly Func<AppSettings> _getSettings;
    private readonly Action<AppSettings> _saveSettings;
    private readonly DispatcherTimer _saveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(600),
    };
    private bool _initialized;
    private bool _disposed;

    public WindowPlacementManager(
        Window window,
        Func<AppSettings> getSettings,
        Action<AppSettings> saveSettings)
    {
        _window = window;
        _getSettings = getSettings;
        _saveSettings = saveSettings;
        _saveTimer.Tick += SaveTimer_Tick;
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        ApplyInitialPlacement();
        _window.AppWindow.Changed += AppWindow_Changed;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window.AppWindow.Changed -= AppWindow_Changed;
        _saveTimer.Stop();
        PersistCurrentSize();
        _saveTimer.Tick -= SaveTimer_Tick;
    }

    private void ApplyInitialPlacement()
    {
        double scale = GetDpiScale();
        DisplayArea? displayArea = DisplayArea.GetFromWindowId(
            _window.AppWindow.Id, DisplayAreaFallback.Nearest);
        if (displayArea is null)
        {
            var settings = _getSettings();
            LogicalWindowSize fallbackSize = WindowSizePolicy.Normalize(
                settings.WindowWidth,
                settings.WindowHeight,
                WindowSizePolicy.DefaultWidth + WindowSizePolicy.WorkAreaMargin,
                WindowSizePolicy.DefaultHeight + WindowSizePolicy.WorkAreaMargin);
            _window.AppWindow.Resize(new SizeInt32(
                ToPhysicalPixels(fallbackSize.Width, scale),
                ToPhysicalPixels(fallbackSize.Height, scale)));
            return;
        }

        RectInt32 workArea = displayArea.WorkArea;
        var remembered = _getSettings();
        LogicalWindowSize logicalSize = WindowSizePolicy.Normalize(
            remembered.WindowWidth,
            remembered.WindowHeight,
            workArea.Width / scale,
            workArea.Height / scale);
        int width = Math.Min(workArea.Width, ToPhysicalPixels(logicalSize.Width, scale));
        int height = Math.Min(workArea.Height, ToPhysicalPixels(logicalSize.Height, scale));
        int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);

        _window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || !IsRestored(sender))
            return;

        double scale = GetDpiScale();
        DisplayArea? displayArea = DisplayArea.GetFromWindowId(
            sender.Id, DisplayAreaFallback.Nearest);
        int minimumWidth = ToPhysicalPixels(WindowSizePolicy.MinimumWidth, scale);
        int minimumHeight = ToPhysicalPixels(WindowSizePolicy.MinimumHeight, scale);
        if (displayArea is not null)
        {
            int margin = ToPhysicalPixels(WindowSizePolicy.WorkAreaMargin, scale);
            minimumWidth = Math.Min(
                minimumWidth, Math.Max(1, displayArea.WorkArea.Width - margin));
            minimumHeight = Math.Min(
                minimumHeight, Math.Max(1, displayArea.WorkArea.Height - margin));
        }

        if (sender.Size.Width < minimumWidth || sender.Size.Height < minimumHeight)
        {
            sender.Resize(new SizeInt32(
                Math.Max(sender.Size.Width, minimumWidth),
                Math.Max(sender.Size.Height, minimumHeight)));
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTimer_Tick(object? sender, object e)
    {
        _saveTimer.Stop();
        PersistCurrentSize();
    }

    private void PersistCurrentSize()
    {
        if (!_initialized || !IsRestored(_window.AppWindow))
            return;

        double scale = GetDpiScale();
        double width = Math.Round(_window.AppWindow.Size.Width / scale);
        double height = Math.Round(_window.AppWindow.Size.Height / scale);
        if (width <= 0 || height <= 0)
            return;

        AppSettings settings = _getSettings();
        if (Math.Abs(settings.WindowWidth - width) < 1
            && Math.Abs(settings.WindowHeight - height) < 1)
        {
            return;
        }

        settings.WindowWidth = width;
        settings.WindowHeight = height;
        try
        {
            _saveSettings(settings);
        }
        catch
        {
            // Window geometry persistence is best-effort and must never block
            // startup, tray restore, or application shutdown.
        }
    }

    private static bool IsRestored(AppWindow appWindow)
        => appWindow.Presenter is not OverlappedPresenter presenter
            || presenter.State == OverlappedPresenterState.Restored;

    private double GetDpiScale()
    {
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        uint dpi = GetDpiForWindow(windowHandle);
        return dpi == 0 ? 1d : dpi / DefaultDpi;
    }

    private static int ToPhysicalPixels(double logicalPixels, double scale)
        => Math.Max(1, checked((int)Math.Round(logicalPixels * scale)));

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);
}
