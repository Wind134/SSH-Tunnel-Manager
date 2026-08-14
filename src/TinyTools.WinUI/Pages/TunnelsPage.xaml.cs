using Microsoft.UI.Xaml.Controls;
using SSHTunnelManager.Services;

namespace TinyTools.WinUI.Pages;

public sealed partial class TunnelsPage : Page
{
    public TunnelsPage()
    {
        InitializeComponent();
        var manager = new TunnelManager();
        manager.Initialize(ConfigStorage.Load().Tunnels);
        TunnelList.ItemsSource = manager.TunnelStates;
        Unloaded += (_, _) => manager.Dispose();
    }
}
