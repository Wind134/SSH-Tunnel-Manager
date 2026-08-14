using System.Threading;

namespace TinyTools.WinUI.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    // Keep the preview independent from the installed WPF fallback. These can
    // be unified when WinUI becomes the sole release entry point.
    private const string MutexName = @"Local\TinyTools_WinUI_SingleInstance";
    private const string ActivationEventName = @"Local\TinyTools_WinUI_Activate";

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listenerTask;

    public bool IsPrimary => _ownsMutex;

    public SingleInstanceCoordinator()
    {
        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        // Creating/opening this for both roles closes the small race where a
        // secondary process observes the mutex before the primary creates its
        // activation event.
        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ActivationEventName);
    }

    public void SignalPrimary()
    {
        if (_ownsMutex)
            return;

        _activationEvent.Set();
    }

    public void Listen(Action activated)
    {
        if (!_ownsMutex)
            return;

        _listenerTask = Task.Run(() =>
        {
            WaitHandle[] handles = [_activationEvent, _shutdown.Token.WaitHandle];
            while (!_shutdown.IsCancellationRequested)
            {
                int signaled = WaitHandle.WaitAny(handles);
                if (signaled == 0)
                    activated();
                else
                    break;
            }
        });
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try { _listenerTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _activationEvent.Dispose();
        _shutdown.Dispose();
        if (_ownsMutex)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
