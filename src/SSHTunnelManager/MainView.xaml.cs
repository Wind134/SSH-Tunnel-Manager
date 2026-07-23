using System.Windows;
using SSHTunnelManager.Services;
using SSHTunnelManager.ViewModels;

namespace SSHTunnelManager;

public partial class MainView : System.Windows.Controls.UserControl
{
    private readonly MainViewModel _viewModel;
    private readonly TunnelManager _tunnelManager;
    private readonly ConfigStorage _configStorage;
    private bool _initialized;

    public MainView()
    {
        InitializeComponent();

        _tunnelManager = new TunnelManager();
        _configStorage = new ConfigStorage();
        _viewModel = new MainViewModel(_tunnelManager, _configStorage);
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            return;
        _initialized = true;

        _viewModel.Initialize();

        _tunnelManager.ConfigChanged += () => Dispatcher.Invoke(_viewModel.SaveConfig);

        _tunnelManager.OnHostKeyReceived += (state, fingerprint, algoName) =>
        {
            bool result = false;
            Dispatcher.Invoke(() =>
            {
                var dialog = new Views.HostKeyDialog(state, fingerprint, algoName);
                dialog.Owner = Window.GetWindow(this);
                result = dialog.ShowDialog() == true;
            });
            return result;
        };
    }

    public void OnShellClosing()
    {
        _viewModel.SaveConfig();
        _ = _tunnelManager.StopAllAsync();
        _tunnelManager.Dispose();
    }

    public MainViewModel ViewModel => _viewModel;
    public TunnelManager TunnelManager => _tunnelManager;
}
