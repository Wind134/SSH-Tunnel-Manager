using Microsoft.UI.Xaml;
using SSHTunnelManager.Services;
using TinyTools.WinUI.Services;

namespace TinyTools.WinUI;

public partial class App : Application
{
    public static MainWindow MainWindow { get; private set; } = null!;
    public static AppServices Services { get; private set; } = null!;

    private SingleInstanceCoordinator? _singleInstance;

    public App()
    {
        ConfigStorage.ConfigureExecutablePath(Environment.ProcessPath);
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            try
            {
                string dataDirectory = Path.GetDirectoryName(ConfigStorage.GetConfigPath())!;
                Directory.CreateDirectory(dataDirectory);
                File.AppendAllText(
                    Path.Combine(dataDirectory, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI: {args.Exception}{Environment.NewLine}");
            }
            catch
            {
                // Crash logging must never replace the original exception.
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.IsPrimary)
        {
            _singleInstance.SignalPrimary();
            _singleInstance.Dispose();
            Environment.Exit(0);
            return;
        }

        Services = new AppServices(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        MainWindow = new MainWindow();
        _singleInstance.Listen(() =>
            MainWindow.DispatcherQueue.TryEnqueue(MainWindow.RestoreAndActivate));
        MainWindow.Activate();
    }

    public static void ShutdownServices()
    {
        Services?.Dispose();
        ((App)Current)._singleInstance?.Dispose();
    }
}
