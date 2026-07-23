using System.Threading;
using System.Windows;

namespace TinyTools;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\TinyTools_SingleInstance",
            createdNew: out bool createdNew);
        _ownsSingleInstanceMutex = createdNew;

        if (!createdNew)
        {
            MessageBox.Show(
                "TinyTools 已在运行中，请从任务栏或系统托盘打开。",
                "TinyTools",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (s, ev) =>
        {
            try
            {
                var dataDir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "data");
                System.IO.Directory.CreateDirectory(dataDir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dataDir, "crash.log"),
                    $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI: {ev.Exception}\n");
            }
            catch
            {
                // Logging must never hide the original failure.
            }

            MessageBox.Show(
                "TinyTools 遇到未处理错误，即将退出。详细信息已写入 data\\crash.log。",
                "TinyTools",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ev.Handled = false;
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
