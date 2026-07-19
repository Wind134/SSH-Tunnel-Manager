using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SSHTunnelManager;

public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;

    // Store next to the executable so crash logs survive UAC elevation.
    private static readonly string s_appDir =
        Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string s_crashLog = Path.Combine(s_appDir, "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Catch any unhandled error so the app shows a readable message instead of
        // silently flashing and exiting (the previous "闪退" behaviour).
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        const string mutexName = "SSHTunnelManager_SingleInstance";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show("SSH Tunnel Manager 已经在运行。", "SSH Tunnel Manager",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            // Covers exceptions thrown while creating/showing the main window.
            HandleFatal("Startup exception", ex);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        HandleFatal("UI thread exception", e.Exception);
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        HandleFatal("Non-UI thread exception", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleFatal("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private static void HandleFatal(string kind, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(s_appDir);
            var entry = new StringBuilder()
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}")
                .AppendLine($"Exception: {ex?.GetType().FullName}")
                .AppendLine($"Message: {ex?.Message}")
                .AppendLine(ex?.ToString())
                .AppendLine(new string('-', 60));
            File.AppendAllText(s_crashLog, entry.ToString());
        }
        catch
        {
            // Nothing we can do if we can't even write the log.
        }

        var box = new StringBuilder();
        box.AppendLine("SSH Tunnel Manager 遇到了未处理的错误。");
        box.AppendLine();
        box.AppendLine($"错误类型：{kind}");
        box.AppendLine($"异常：{ex?.GetType().FullName}");
        box.AppendLine($"消息：{ex?.Message}");
        box.AppendLine();
        box.AppendLine("完整堆栈已保存到：");
        box.AppendLine(s_crashLog);

        MessageBox.Show(box.ToString(), "SSH Tunnel Manager - 崩溃", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
