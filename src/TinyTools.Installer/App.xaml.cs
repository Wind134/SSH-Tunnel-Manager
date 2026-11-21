using System.Windows;
using TinyTools.Installer.Models;
using TinyTools.Installer.Services;
using TinyTools.Installer.UI;

namespace TinyTools.Installer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InstallerLaunchOptions options = CommandLineParser.Parse(e.Args);
        var window = new InstallerWindow(options);
        MainWindow = window;
        window.Show();
    }
}
