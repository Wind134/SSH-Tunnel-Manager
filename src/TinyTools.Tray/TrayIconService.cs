using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TinyTools.Tray;

/// <summary>
/// A dependency-free Win32 notification-area icon hosted by a dedicated
/// message thread. This keeps the WinUI application independent from
/// Windows Forms and avoids pulling its desktop runtime into self-contained
/// releases.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmCommand = 0x0111;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmApp = 0x8000;
    private const uint TrayCallbackMessage = WmApp + 1;
    private const uint ShowBalloonMessage = WmApp + 2;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmLeftAlign = 0x0000;

    private const int OpenCommandId = 1001;
    private const int SettingsCommandId = 1002;
    private const int ExitCommandId = 1003;
    private const uint IconId = 1;

    private static readonly IntPtr MessageOnlyWindow = new(-3);

    private readonly string _toolTip;
    private readonly string? _executablePath;
    private readonly string _windowClassName = $"TinyTools.Tray.{Guid.NewGuid():N}";
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _messageLock = new();
    private readonly Thread _messageThread;
    private readonly WindowProcedure _windowProcedure;

    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private Exception? _startupException;
    private string _balloonTitle = string.Empty;
    private string _balloonMessage = string.Empty;
    private int _balloonDuration = 2500;
    private bool _disposed;

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconService(string toolTip, string? executablePath)
    {
        _toolTip = Limit(toolTip, 127);
        _executablePath = executablePath;
        _windowProcedure = WindowProc;
        _messageThread = new Thread(MessageThreadMain)
        {
            IsBackground = true,
            Name = "TinyTools notification area"
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            Dispose();
            throw new TimeoutException("Timed out while creating the notification-area icon.");
        }

        if (_startupException is not null)
        {
            Dispose();
            throw new InvalidOperationException(
                "Could not create the notification-area icon.",
                _startupException);
        }
    }

    public void ShowInformation(
        string title,
        string message,
        int durationMilliseconds = 2500)
    {
        ThrowIfDisposed();

        lock (_messageLock)
        {
            _balloonTitle = Limit(title, 63);
            _balloonMessage = Limit(message, 255);
            _balloonDuration = Math.Clamp(durationMilliseconds, 1000, 30000);
        }

        if (!PostMessage(_windowHandle, ShowBalloonMessage, IntPtr.Zero, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var windowHandle = _windowHandle;
        if (windowHandle != IntPtr.Zero)
        {
            PostMessage(windowHandle, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        bool messageThreadStopped = !_messageThread.IsAlive;
        if (Thread.CurrentThread != _messageThread && !messageThreadStopped)
        {
            messageThreadStopped = _messageThread.Join(TimeSpan.FromSeconds(3));
        }

        // If native startup is unexpectedly stuck, leave this small wait
        // handle alive rather than racing a late _ready.Set() on that thread.
        if (messageThreadStopped || Thread.CurrentThread == _messageThread)
            _ready.Dispose();
        GC.SuppressFinalize(this);
    }

    private void MessageThreadMain()
    {
        var moduleHandle = GetModuleHandle(null);
        ushort classAtom = 0;

        try
        {
            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                Instance = moduleHandle,
                ClassName = _windowClassName,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure)
            };

            classAtom = RegisterClassEx(ref windowClass);
            if (classAtom == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _windowHandle = CreateWindowEx(
                0,
                _windowClassName,
                "TinyTools Tray",
                0,
                0,
                0,
                0,
                0,
                MessageOnlyWindow,
                IntPtr.Zero,
                moduleHandle,
                IntPtr.Zero);
            if (_windowHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _iconHandle = LoadApplicationIcon();
            var iconData = CreateIconData(NifMessage | NifTip | NifIcon);
            if (!ShellNotifyIcon(NimAdd, ref iconData))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _ready.Set();

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _startupException = exception;
            _ready.Set();
        }
        finally
        {
            try
            {
                RemoveIcon();
            }
            catch
            {
                // Native cleanup failures must not terminate the process.
            }

            if (_windowHandle != IntPtr.Zero)
            {
                DestroyWindow(_windowHandle);
                _windowHandle = IntPtr.Zero;
            }

            if (classAtom != 0)
            {
                UnregisterClass(_windowClassName, moduleHandle);
            }
        }
    }

    private IntPtr WindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case TrayCallbackMessage:
                    switch ((uint)lParam.ToInt64())
                    {
                        case WmLButtonDoubleClick:
                            RaiseSafely(OpenRequested);
                            return IntPtr.Zero;
                        case WmRButtonUp:
                            ShowContextMenu(windowHandle);
                            return IntPtr.Zero;
                    }

                    break;

                case ShowBalloonMessage:
                    ShowBalloon();
                    return IntPtr.Zero;

                case WmCommand:
                    HandleCommand((int)(wParam.ToInt64() & 0xffff));
                    return IntPtr.Zero;

                case WmClose:
                    DestroyWindow(windowHandle);
                    return IntPtr.Zero;

                case WmDestroy:
                    RemoveIcon();
                    _windowHandle = IntPtr.Zero;
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch
        {
            // Exceptions must never escape a native window procedure.
        }

        return DefWindowProc(windowHandle, message, wParam, lParam);
    }

    private void ShowContextMenu(IntPtr windowHandle)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, OpenCommandId, "打开 TinyTools");
            AppendMenu(menu, MfString, SettingsCommandId, "设置");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, ExitCommandId, "退出");

            GetCursorPos(out var cursorPosition);
            SetForegroundWindow(windowHandle);
            TrackPopupMenu(
                menu,
                TpmRightButton | TpmBottomAlign | TpmLeftAlign,
                cursorPosition.X,
                cursorPosition.Y,
                0,
                windowHandle,
                IntPtr.Zero);
            PostMessage(windowHandle, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void HandleCommand(int commandId)
    {
        switch (commandId)
        {
            case OpenCommandId:
                RaiseSafely(OpenRequested);
                break;
            case SettingsCommandId:
                RaiseSafely(SettingsRequested);
                break;
            case ExitCommandId:
                RaiseSafely(ExitRequested);
                break;
        }
    }

    private void ShowBalloon()
    {
        string title;
        string message;
        int duration;
        lock (_messageLock)
        {
            title = _balloonTitle;
            message = _balloonMessage;
            duration = _balloonDuration;
        }

        var iconData = CreateIconData(NifInfo);
        iconData.InfoTitle = title;
        iconData.Info = message;
        iconData.TimeoutOrVersion = (uint)duration;
        iconData.InfoFlags = NiifInfo;
        ShellNotifyIcon(NimModify, ref iconData);
    }

    private NotifyIconData CreateIconData(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = _windowHandle,
        Id = IconId,
        Flags = flags,
        CallbackMessage = TrayCallbackMessage,
        IconHandle = _iconHandle,
        Tip = _toolTip,
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private IntPtr LoadApplicationIcon()
    {
        if (!string.IsNullOrWhiteSpace(_executablePath))
        {
            var largeIcons = new IntPtr[1];
            var smallIcons = new IntPtr[1];
            if (ExtractIconEx(_executablePath, 0, largeIcons, smallIcons, 1) > 0)
            {
                if (largeIcons[0] != IntPtr.Zero)
                {
                    if (smallIcons[0] != IntPtr.Zero)
                    {
                        DestroyIcon(smallIcons[0]);
                    }

                    return largeIcons[0];
                }

                if (smallIcons[0] != IntPtr.Zero)
                {
                    return smallIcons[0];
                }
            }
        }

        var fallback = LoadIcon(IntPtr.Zero, new IntPtr(32512));
        if (fallback == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        // Shared system icons must not be destroyed.
        return fallback;
    }

    private void RemoveIcon()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            var iconData = CreateIconData(0);
            ShellNotifyIcon(NimDelete, ref iconData);
        }

        if (_iconHandle != IntPtr.Zero && !IsSharedSystemIcon(_iconHandle))
        {
            DestroyIcon(_iconHandle);
        }

        _iconHandle = IntPtr.Zero;
    }

    private static bool IsSharedSystemIcon(IntPtr iconHandle) =>
        iconHandle == LoadIcon(IntPtr.Zero, new IntPtr(32512));

    private static void RaiseSafely(Action? handler)
    {
        if (handler is null)
        {
            return;
        }

        foreach (Action subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber();
            }
            catch
            {
                // A tray callback must not terminate the native message loop.
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string Limit(string? value, int maximumLength)
    {
        var text = value ?? string.Empty;
        return text.Length <= maximumLength ? text : text[..maximumLength];
    }

    private delegate IntPtr WindowProcedure(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr WindowHandle;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint minimumFilter,
        uint maximumFilter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport(
        "shell32.dll",
        EntryPoint = "Shell_NotifyIconW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string file,
        int iconIndex,
        [Out] IntPtr[] largeIcons,
        [Out] IntPtr[] smallIcons,
        uint iconCount);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", ExactSpelling = true)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(
        IntPtr menu,
        uint flags,
        nuint item,
        string? newItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr windowHandle,
        IntPtr rectangle);
}
