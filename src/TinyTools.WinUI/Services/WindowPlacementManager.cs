using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SSHTunnelManager.Models;
using TinyTools.Core.Windowing;
using Windows.Graphics;

namespace TinyTools.WinUI.Services;

/// <summary>
/// 管理窗口初始位置、DPI 感知最小尺寸约束与防抖尺寸持久化。
/// </summary>
internal sealed class WindowPlacementManager : IDisposable
{
    private const double DefaultDpi = 96d;

    private readonly Window _window;
    private readonly Func<AppSettings> _getSettings;
    private readonly Action<AppSettings> _saveSettings;
    private readonly DispatcherTimer _saveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };
    private bool _initialized;
    private bool _disposed;
    private int _lastMinPhysicalWidth;
    private int _lastMinPhysicalHeight;

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
        ApplyMinimumSizeConstraints();
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

    // 通过原生 OverlappedPresenter 设置最小尺寸，拖动缩放时无回弹闪烁
    private void ApplyMinimumSizeConstraints()
    {
        if (_window.AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        presenter.IsResizable = true;
        presenter.IsMaximizable = true;
        presenter.IsMinimizable = true;

        double scale = GetDpiScale();
        DisplayArea? displayArea = DisplayArea.GetFromWindowId(
            _window.AppWindow.Id, DisplayAreaFallback.Nearest);

        LogicalWindowSize minLogical = displayArea is not null
            ? WindowSizePolicy.GetEffectiveMinimumSize(
                displayArea.WorkArea.Width / scale,
                displayArea.WorkArea.Height / scale)
            : new LogicalWindowSize(WindowSizePolicy.MinimumWidth, WindowSizePolicy.MinimumHeight);

        int minPhysicalWidth = ToPhysicalPixels(minLogical.Width, scale);
        int minPhysicalHeight = ToPhysicalPixels(minLogical.Height, scale);
        if (minPhysicalWidth == _lastMinPhysicalWidth && minPhysicalHeight == _lastMinPhysicalHeight)
            return;

        _lastMinPhysicalWidth = minPhysicalWidth;
        _lastMinPhysicalHeight = minPhysicalHeight;
        presenter.PreferredMinimumWidth = minPhysicalWidth;
        presenter.PreferredMinimumHeight = minPhysicalHeight;
    }

    private void ApplyInitialPlacement()
    {
        double scale = GetDpiScale();
        DisplayArea? displayArea = DisplayArea.GetFromWindowId(
            _window.AppWindow.Id, DisplayAreaFallback.Nearest);

        AppSettings settings = _getSettings();
        if (displayArea is null)
        {
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
        LogicalWindowSize logicalSize = WindowSizePolicy.Normalize(
            settings.WindowWidth,
            settings.WindowHeight,
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
        // 跨显示器移动或 DPI 变化时刷新最小尺寸限制
        if (args.DidPositionChange)
            ApplyMinimumSizeConstraints();

        if (!args.DidSizeChange || !IsRestored(sender))
            return;

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
            // 窗口几何持久化失败不影响主流程
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
