using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using SSHTunnelManager.Models;
using SSHTunnelManager.Services;

namespace TinyTools.WinUI.Services;

/// <summary>
/// 应用服务容器：集中管理隧道引擎、日志节流缓冲、配置与全局更新协调器。
/// </summary>
public sealed class AppServices : IDisposable
{
    private const int MaxLogEntries = 500;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _logFlushTimer;
    private readonly ConcurrentQueue<string> _pendingLogs = new();
    private int _flushScheduled;
    private bool _disposed;

    public TunnelManager TunnelManager { get; }
    public UpdateCoordinator UpdateCoordinator { get; }
    public ObservableCollection<string> LogEntries { get; } = new();
    public AppSettings Settings { get; private set; }

    public AppServices(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        // 150ms 批量刷新日志，避免高频隧道请求阻塞 UI 线程或触发 ListView 布局异常
        _logFlushTimer = dispatcherQueue.CreateTimer();
        _logFlushTimer.Interval = TimeSpan.FromMilliseconds(150);
        _logFlushTimer.IsRepeating = false;
        _logFlushTimer.Tick += (_, _) => FlushPendingLogs();

        TunnelManager = new TunnelManager(action =>
        {
            if (_dispatcherQueue.HasThreadAccess)
                action();
            else
                _dispatcherQueue.TryEnqueue(() => action());
        });
        UpdateCoordinator = new UpdateCoordinator(dispatcherQueue);

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
        if (_disposed)
            return;

        _pendingLogs.Enqueue(message);
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
            _dispatcherQueue.TryEnqueue(() => _logFlushTimer.Start());
    }

    private void FlushPendingLogs()
    {
        Interlocked.Exchange(ref _flushScheduled, 0);
        if (_disposed)
            return;

        var batch = new List<string>();
        while (_pendingLogs.TryDequeue(out string? entry))
            batch.Add(entry);

        if (batch.Count == 0)
            return;

        // 仅保留最新 500 条，防止单次突发流量撑爆列表
        int start = Math.Max(0, batch.Count - MaxLogEntries);
        for (int i = start; i < batch.Count; i++)
            LogEntries.Insert(0, batch[i]);

        while (LogEntries.Count > MaxLogEntries)
            LogEntries.RemoveAt(LogEntries.Count - 1);

        if (!_pendingLogs.IsEmpty && Interlocked.Exchange(ref _flushScheduled, 1) == 0)
            _logFlushTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        SaveTunnels();
        _disposed = true;
        _logFlushTimer.Stop();
        TunnelManager.LogMessage -= OnLogMessage;
        TunnelManager.ConfigChanged -= OnConfigChanged;
        UpdateCoordinator.Dispose();
        TunnelManager.Dispose();
    }
}
