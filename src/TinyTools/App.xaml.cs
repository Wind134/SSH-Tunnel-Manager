
using System.Windows;

namespace TinyTools;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, ev) =>
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "crash.log"),
                $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI: {ev.Exception}\n");
            ev.Handled = true;
        };

        base.OnStartup(e);
    }
}
