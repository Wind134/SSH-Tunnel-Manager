using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using SSHTunnelManager.Models;
using SSHTunnelManager.Services;

namespace TinyTools.WinUI.Services;

public sealed class AppServices : IDisposable
{
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _disposed;

    public TunnelManager TunnelManager { get; } = new();
    public ObservableCollection<string> LogEntries { get; } = new();
    public AppSettings Settings { get; private set; }

    public AppServices(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;

        ConfigFile config;
        try
        {
            config = ConfigStorage.Load();
        }
        catch
        {
            config = new ConfigFile();
        }

        Settings = config.Settings ?? new AppSettings();
        TunnelManager.Initialize(config.Tunnels);
        TunnelManager.LogMessage += OnLogMessage;
        TunnelManager.ConfigChanged += OnConfigChanged;
    }

    public void SaveTunnels()
    {
        if (_disposed)
            return;

        var config = new ConfigFile
        {
            Version = 2,
            Settings = Settings,
            Tunnels = TunnelManager.TunnelStates.Select(state => state.Config).ToList(),
        };
        ConfigStorage.Save(config);
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        ConfigStorage.SaveSettings(settings);
    }

    private void OnConfigChanged()
    {
        if (_dispatcherQueue.HasThreadAccess)
            SaveTunnels();
        else
            _dispatcherQueue.TryEnqueue(SaveTunnels);
    }

    private void OnLogMessage(string tunnelName, string message)
    {
        void AddLog()
        {
            LogEntries.Insert(0, message);
            if (LogEntries.Count > 500)
                LogEntries.RemoveAt(LogEntries.Count - 1);
        }

        if (_dispatcherQueue.HasThreadAccess)
            AddLog();
        else
            _dispatcherQueue.TryEnqueue(AddLog);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        SaveTunnels();
        _disposed = true;
        TunnelManager.LogMessage -= OnLogMessage;
        TunnelManager.ConfigChanged -= OnConfigChanged;
        TunnelManager.Dispose();
    }
}
