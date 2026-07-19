using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SSHTunnelManager.Models;
using SSHTunnelManager.Services;

namespace SSHTunnelManager.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly TunnelManager _tunnelManager;
    private readonly ConfigStorage _configStorage;
    private AppSettings _settings = new();
    private string _logText = string.Empty;
    private TunnelState? _selectedTunnel;

    public ObservableCollection<TunnelState> Tunnels => _tunnelManager.TunnelStates;

    public ObservableCollection<string> LogEntries { get; } = new();

    public TunnelState? SelectedTunnel
    {
        get => _selectedTunnel;
        set { _selectedTunnel = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get
        {
            var total = Tunnels.Count;
            var running = Tunnels.Count(t => t.Status == TunnelStatus.Connected);
            return $"{total} tunnels | {running} running";
        }
    }

    public ICommand AddTunnelCommand { get; }
    public ICommand EditTunnelCommand { get; }
    public ICommand DeleteTunnelCommand { get; }
    public ICommand StartTunnelCommand { get; }
    public ICommand StopTunnelCommand { get; }
    public ICommand StartAllCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand CopyLogCommand { get; }

    public AppSettings Settings => _settings;

    public MainViewModel(TunnelManager tunnelManager, ConfigStorage configStorage)
    {
        _tunnelManager = tunnelManager;
        _configStorage = configStorage;

        _tunnelManager.LogMessage += OnLogMessage;
        _tunnelManager.TunnelStates.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(StatusText));
            if (e.NewItems != null)
                foreach (TunnelState item in e.NewItems)
                    item.PropertyChanged += (s2, e2) => OnPropertyChanged(nameof(StatusText));
        };

        AddTunnelCommand = new RelayCommand(_ => true, _ => AddTunnel());
        EditTunnelCommand = new RelayCommand(p => p is TunnelState, p => EditTunnel((TunnelState)p!));
        DeleteTunnelCommand = new RelayCommand(p => p is TunnelState, p => DeleteTunnel((TunnelState)p!));
        StartTunnelCommand = new RelayCommand(
            p => p is TunnelState ts && ts.CanStart,
            async p => await _tunnelManager.StartTunnel((TunnelState)p!));
        StopTunnelCommand = new RelayCommand(
            p => p is TunnelState ts && ts.CanStop,
            async p => await _tunnelManager.StopTunnel((TunnelState)p!));
        StartAllCommand = new RelayCommand(_ => Tunnels.Any(t => t.Status is TunnelStatus.Disconnected or TunnelStatus.Failed),
            async _ => await _tunnelManager.StartAllAsync());
        StopAllCommand = new RelayCommand(_ => Tunnels.Any(t => t.Status is TunnelStatus.Connected or TunnelStatus.Connecting),
            async _ => await _tunnelManager.StopAllAsync());
        SettingsCommand = new RelayCommand(_ => true, _ => OpenSettings());
        CopyLogCommand = new RelayCommand(_ => LogEntries.Count > 0, _ => CopyLog());
    }

    private void CopyLog()
    {
        if (LogEntries.Count == 0)
            return;
        // LogEntries stores newest-first; reverse for chronological order.
        var text = string.Join(Environment.NewLine, LogEntries.Reverse());
        Clipboard.SetText(text);
    }

    private void OnLogMessage(string tunnelName, string message)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            LogEntries.Insert(0, message);
            if (LogEntries.Count > 200)
                LogEntries.RemoveAt(LogEntries.Count - 1);
        });
    }

    public void Initialize()
    {
        try
        {
            var config = ConfigStorage.Load();
            _settings = config.Settings ?? new AppSettings();
            _tunnelManager.Initialize(config.Tunnels);
        }
        catch (Exception ex)
        {
            // A corrupted config or any load failure must not crash startup.
            // Start with an empty tunnel list and let the user re-add configs.
            System.Windows.MessageBox.Show(
                $"Failed to load saved configuration:\n{ex.Message}\n\nThe app will start with an empty configuration.",
                "SSH Tunnel Manager",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }

        OnPropertyChanged(nameof(StatusText));
    }

    private void AddTunnel()
    {
        var dialog = new Views.AddEditDialog(null);
        if (dialog.ShowDialog() == true)
        {
            var newConfig = dialog.GetConfig();
            _tunnelManager.AddTunnel(newConfig);
            SaveConfig();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private void EditTunnel(TunnelState state)
    {
        var dialog = new Views.AddEditDialog(state.Config);
        if (dialog.ShowDialog() == true)
        {
            var updated = dialog.GetConfig();
            _tunnelManager.UpdateTunnel(updated);
            SaveConfig();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private void DeleteTunnel(TunnelState state)
    {
        var result = System.Windows.MessageBox.Show(
            $"Delete tunnel \"{state.Config.Name}\"?",
            "Confirm Delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            _tunnelManager.RemoveTunnel(state.Config.Id);
            SaveConfig();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public void SaveConfig()
    {
        var config = new ConfigFile
        {
            Version = 2,
            Tunnels = _tunnelManager.TunnelStates.Select(s => s.Config).ToList(),
            Settings = _settings
        };
        ConfigStorage.Save(config);
    }

    private void OpenSettings()
    {
        var dialog = new Views.SettingsDialog(_settings);
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        if (dialog.ShowDialog() == true)
        {
            _settings = dialog.GetSettings();
            SaveConfig();
            ThemeHelper.Apply(_settings.Theme);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Predicate<object?> _canExecute;
    private readonly Action<object?> _execute;

    public RelayCommand(Predicate<object?> canExecute, Action<object?> execute)
    {
        _canExecute = canExecute;
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => _canExecute(parameter);
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }
}
