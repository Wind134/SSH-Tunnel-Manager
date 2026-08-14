using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TinyTools.WinUI.Pages;

namespace TinyTools.WinUI;

public sealed partial class MainWindow : Window
{
    public FrameworkElement RootElement => RootGrid;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("app.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 720));

        // Mica is Windows 11-only. Keeping the root transparent lets WinUI use
        // its normal solid theme background when the system backdrop is absent.
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };

        Navigation.SelectedItem = Navigation.MenuItems[0];
        Navigate(typeof(OverviewPage));
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            Navigate(typeof(SettingsPage));
            return;
        }

        string? tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag?.ToString();
        Navigate(tag switch
        {
            "tunnels" => typeof(TunnelsPage),
            "ports" => typeof(PortsPage),
            "locks" => typeof(FileLocksPage),
            _ => typeof(OverviewPage),
        });
    }

    private void Navigate(Type pageType)
    {
        if (ContentFrame.CurrentSourcePageType == pageType)
            return;

        ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
    }
}
