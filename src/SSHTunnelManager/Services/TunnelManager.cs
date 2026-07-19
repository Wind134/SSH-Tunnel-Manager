using Renci.SshNet;
using Renci.SshNet.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using SSHTunnelManager.Models;

namespace SSHTunnelManager.Services;

public class TunnelState : INotifyPropertyChanged
{
    private TunnelStatus _status = TunnelStatus.Disconnected;
    private string _statusMessage = string.Empty;
    private DateTime? _connectedSince;

    public TunnelConfig Config { get; }

    public TunnelStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    public DateTime? ConnectedSince
    {
        get => _connectedSince;
        set { _connectedSince = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    public string StatusText
    {
        get
        {
            return Status switch
            {
                TunnelStatus.Disconnected => "Disconnected",
                TunnelStatus.Connecting => "Connecting...",
                TunnelStatus.HostKeyPending => "Host key pending",
                TunnelStatus.Connected when ConnectedSince.HasValue =>
                    $"Connected ({(int)(DateTime.Now - ConnectedSince.Value).TotalMinutes}m)",
                TunnelStatus.Connected => "Connected",
                TunnelStatus.Failed => $"Failed: {StatusMessage}",
                _ => string.Empty
            };
        }
    }

    public string MaskedHost { get; private set; } = string.Empty;

    public bool CanStart => Status is TunnelStatus.Disconnected or TunnelStatus.Failed;
    public bool CanStop => Status is TunnelStatus.Connected or TunnelStatus.Connecting;

    internal SshClient? Client { get; set; }
    internal ForwardedPortRemote? ForwardedPort { get; set; }
    internal int ReconnectAttempts { get; set; }
    internal CancellationTokenSource? ReconnectCts { get; set; }

    public TunnelState(TunnelConfig config)
    {
        Config = config;
        UpdateMaskedHost();
    }

    public void UpdateMaskedHost()
    {
        try
        {
            var host = CryptoHelper.Decrypt(Config.EncryptedHost);
            MaskedHost = CryptoHelper.MaskIp(host);
        }
        catch
        {
            MaskedHost = "***";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class TunnelManager : IDisposable
{
    private readonly Dictionary<Guid, TunnelState> _tunnels = new();
    private System.Threading.Timer? _heartbeatTimer;
    private bool _disposed;

    public ObservableCollection<TunnelState> TunnelStates { get; } = new();

    public event Action<string, string>? LogMessage;
    public event Action? ConfigChanged;

    public void Initialize(IEnumerable<TunnelConfig> configs)
    {
        TunnelStates.Clear();
        _tunnels.Clear();

        foreach (var config in configs)
        {
            var state = new TunnelState(config);
            _tunnels[config.Id] = state;
            TunnelStates.Add(state);
        }

        _heartbeatTimer ??= new System.Threading.Timer(HeartbeatCallback, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public void AddTunnel(TunnelConfig config)
    {
        var state = new TunnelState(config);
        _tunnels[config.Id] = state;
        TunnelStates.Add(state);
    }

    public void RemoveTunnel(Guid id)
    {
        if (_tunnels.TryGetValue(id, out var state))
        {
            StopTunnel(state).Wait();
            _tunnels.Remove(id);
            TunnelStates.Remove(state);
        }
    }

    public void UpdateTunnel(TunnelConfig config)
    {
        if (_tunnels.TryGetValue(config.Id, out var state))
        {
            var wasConnected = state.Status == TunnelStatus.Connected;

            _ = StopTunnel(state);

            state.Config.Name = config.Name;
            state.Config.LocalPort = config.LocalPort;
            state.Config.RemotePort = config.RemotePort;
            state.Config.SshUser = config.SshUser;
            state.Config.SshPort = config.SshPort;
            state.Config.AuthMethod = config.AuthMethod;
            state.Config.EncryptedHost = config.EncryptedHost;
            state.Config.EncryptedPassword = config.EncryptedPassword;
            state.Config.KeyFilePath = config.KeyFilePath;
            state.Config.AutoReconnect = config.AutoReconnect;
            state.Config.ModifiedAt = DateTime.Now;
            state.UpdateMaskedHost();
            state.Status = TunnelStatus.Disconnected;
            state.StatusMessage = string.Empty;
            state.ConnectedSince = null;

            if (wasConnected)
                _ = StartTunnel(state);
        }
    }

    public async Task StartTunnel(TunnelState state)
    {
        if (state.Status == TunnelStatus.Connected || state.Status == TunnelStatus.Connecting)
            return;

        state.Status = TunnelStatus.Connecting;
        state.StatusMessage = string.Empty;
        state.ReconnectAttempts = 0;

        await ConnectAsync(state);
    }

    public Task StopTunnel(TunnelState state)
    {
        state.ReconnectCts?.Cancel();
        state.ReconnectCts = null;

        try
        {
            state.ForwardedPort?.Stop();
            state.ForwardedPort?.Dispose();
        }
        catch { /* ignore */ }

        try
        {
            state.Client?.Disconnect();
            state.Client?.Dispose();
        }
        catch { /* ignore */ }

        state.ForwardedPort = null;
        state.Client = null;
        state.Status = TunnelStatus.Disconnected;
        state.StatusMessage = string.Empty;
        state.ConnectedSince = null;

        var name = state.Config.Name;
        Log(name, "Tunnel stopped");

        return Task.CompletedTask;
    }

    public async Task StartAllAsync()
    {
        foreach (var state in _tunnels.Values.ToList())
        {
            if (state.Status == TunnelStatus.Disconnected || state.Status == TunnelStatus.Failed)
                await StartTunnel(state);
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var state in _tunnels.Values.ToList())
            await StopTunnel(state);
    }

    private async Task ConnectAsync(TunnelState state)
    {
        var config = state.Config;
        var name = config.Name;

        try
        {
            var host = CryptoHelper.Decrypt(config.EncryptedHost);
            if (string.IsNullOrEmpty(host))
            {
                state.Status = TunnelStatus.Failed;
                state.StatusMessage = "Host is empty";
                Log(name, "Failed: host is empty");
                return;
            }

            Log(name, $"Connecting to {CryptoHelper.MaskIp(host)}:{config.SshPort}...");

            // Precheck: the tunnel forwards to 127.0.0.1:LocalPort on this machine.
            // Warn (non-fatal) if that local target isn't actually listening, so the
            // user knows the tunnel may not reach anything.
            if (!PortChecker.IsPortInUse(config.LocalPort))
                Log(name, $"Warning: local port {config.LocalPort} is not listening — the tunnel target may be unavailable.");

            AuthenticationMethod auth = config.AuthMethod switch
            {
                AuthMethod.Password => new PasswordAuthenticationMethod(
                    config.SshUser,
                    CryptoHelper.Decrypt(config.EncryptedPassword)),
                AuthMethod.PrivateKey => new PrivateKeyAuthenticationMethod(
                    config.SshUser,
                    LoadPrivateKey(config.KeyFilePath, name)),
                _ => throw new InvalidOperationException("Unknown auth method")
            };

            var connInfo = new ConnectionInfo(host, config.SshPort, config.SshUser, auth)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            state.Client = new SshClient(connInfo);

            state.Client.HostKeyReceived += (sender, e) =>
            {
                var fingerprintHex = BitConverter.ToString(e.FingerPrint).Replace("-", ":");

                if (config.HostKeyTrust == HostKeyTrust.Trusted
                    && fingerprintHex == config.HostKeyFingerprint)
                {
                    e.CanTrust = true;
                }
                else if (config.HostKeyTrust == HostKeyTrust.Unknown
                    || string.IsNullOrEmpty(config.HostKeyFingerprint))
                {
                    Log(name, $"Host key fingerprint: {e.HostKeyName} {fingerprintHex}");

                    bool trust = false;
                    state.Status = TunnelStatus.HostKeyPending;
                    trust = OnHostKeyReceived?.Invoke(state, fingerprintHex, e.HostKeyName) ?? false;

                if (trust)
                {
                    config.HostKeyFingerprint = fingerprintHex;
                    config.HostKeyTrust = HostKeyTrust.Trusted;
                    e.CanTrust = true;
                    Log(name, "Host key accepted by user");
                    ConfigChanged?.Invoke();
                }
                    else
                    {
                        e.CanTrust = false;
                        Log(name, "Host key rejected by user");
                    }
                }
                else
                {
                    e.CanTrust = false;
                    Log(name, "Host key mismatch - connection rejected");
                }
            };

            await Task.Run(() => state.Client.Connect());

            Log(name, "SSH authenticated successfully");

            var forward = new ForwardedPortRemote(
                "127.0.0.1", (uint)config.RemotePort,
                "127.0.0.1", (uint)config.LocalPort);

            state.Client.AddForwardedPort(forward);
            forward.Start();
            state.ForwardedPort = forward;

            Log(name, $"Port forwarding started: remote:{config.RemotePort} -> local:{config.LocalPort}");

            // connectivity test via SSH command
            _ = Task.Run(() => TestConnectivityAsync(state));

            state.Status = TunnelStatus.Connected;
            state.ConnectedSince = DateTime.Now;
            Log(name, "Tunnel established");
        }
        catch (SshException ex)
        {
            state.Status = TunnelStatus.Failed;
            state.StatusMessage = ex.Message;
            Log(name, $"Connection failed: {ex.Message}");
            TryReconnect(state);
        }
        catch (Exception ex)
        {
            state.Status = TunnelStatus.Failed;
            state.StatusMessage = ex.Message;
            Log(name, $"Error: {ex.Message}");
            TryReconnect(state);
        }
    }

    private PrivateKeyFile LoadPrivateKey(string keyPath, string name)
    {
        // Pre-flight checks so failures produce a clear, actionable message
        // instead of an opaque "access denied" from deep inside SSH.NET.
        if (string.IsNullOrWhiteSpace(keyPath))
            throw new InvalidOperationException(
                "No private key file selected. Edit this tunnel and choose a key file, or switch to password auth.");

        if (Directory.Exists(keyPath))
            throw new InvalidOperationException(
                $"The key path points to a folder, not a file: {keyPath}");

        if (!File.Exists(keyPath))
            throw new FileNotFoundException(
                $"Private key file not found: {keyPath}", keyPath);

        // Verify we can actually read the file before handing it to SSH.NET.
        try
        {
            using var probe = File.OpenRead(keyPath);
        }
        catch (UnauthorizedAccessException)
        {
            Log(name, $"No read permission on key file: {keyPath}");
            throw new UnauthorizedAccessException(
                $"No permission to read the key file:\n{keyPath}\n\n" +
                "Fix on Windows: right-click the file → Properties → Security, and grant your " +
                "account Read permission. Or run in PowerShell:\n" +
                $"  icacls \"{keyPath}\" /grant:r \"$env:USERNAME:R\"");
        }
        catch (IOException ex)
        {
            throw new IOException($"Cannot open the key file:\n{keyPath}\n{ex.Message}", ex);
        }

        try
        {
            return new PrivateKeyFile(keyPath);
        }
        catch (Renci.SshNet.Common.SshException ex)
        {
            // SSH.NET 2020.0.2 does not support the modern OpenSSH key format
            // ("-----BEGIN OPENSSH PRIVATE KEY-----"), which is the default for
            // ssh-keygen ed25519 / newer RSA keys.
            throw new InvalidOperationException(
                "The private key could not be parsed. It may be in the newer OpenSSH " +
                "format or encrypted with a passphrase (both unsupported by this build).\n" +
                "Convert it to classic PEM without a passphrase:\n" +
                $"  ssh-keygen -p -m PEM -f \"{keyPath}\"\n\n" +
                $"Details: {ex.Message}", ex);
        }
    }

    private async Task TestConnectivityAsync(TunnelState state)
    {
        var config = state.Config;
        var name = config.Name;

        try
        {
            await Task.Delay(2000); // give the tunnel a moment

            if (state.Client?.IsConnected != true)
                return;

            using var cmd = state.Client.RunCommand(
                $"curl -x http://127.0.0.1:{config.RemotePort} " +
                $"-s -o /dev/null -w '%{{http_code}}' " +
                $"--connect-timeout 5 https://www.google.com");

            var result = cmd.Result.Trim();
            if (result == "200" || result == "301" || result == "302")
                Log(name, "Connectivity test passed");
            else
                Log(name, $"Connectivity test returned HTTP {result} (tunnel may be limited)");
        }
        catch (Exception ex)
        {
            Log(name, $"Connectivity test failed: {ex.Message}");
        }
    }

    private void TryReconnect(TunnelState state)
    {
        if (!state.Config.AutoReconnect)
            return;

        if (state.ReconnectAttempts >= 5)
        {
            Log(state.Config.Name, "Max reconnection attempts reached, giving up");
            return;
        }

        state.ReconnectAttempts++;
        state.ReconnectCts?.Cancel();
        state.ReconnectCts = new CancellationTokenSource();
        var ct = state.ReconnectCts.Token;

        var delay = state.ReconnectAttempts <= 3 ? 3 : 10;
        Log(state.Config.Name, $"Reconnecting in {delay}s (attempt {state.ReconnectAttempts}/5)...");

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                if (ct.IsCancellationRequested)
                    return;

                state.Status = TunnelStatus.Connecting;
                await ConnectAsync(state);
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex)
            {
                Log(state.Config.Name, $"Reconnection error: {ex.Message}");
            }
        });
    }

    private void HeartbeatCallback(object? _)
    {
        foreach (var state in _tunnels.Values.ToList())
        {
            if (state.Status == TunnelStatus.Connected)
            {
                if (state.Client?.IsConnected != true)
                {
                    Log(state.Config.Name, "Connection lost");
                    state.Status = TunnelStatus.Failed;
                    state.StatusMessage = "Connection lost";
                    state.ConnectedSince = null;
                    TryReconnect(state);
                }
            }
        }
    }

    private void Log(string tunnelName, string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogMessage?.Invoke(tunnelName, $"[{timestamp}] {tunnelName} -> {message}");
    }

    public event Func<TunnelState, string, string, bool>? OnHostKeyReceived;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _heartbeatTimer?.Dispose();

        foreach (var state in _tunnels.Values.ToList())
        {
            try
            {
                state.ForwardedPort?.Stop();
                state.ForwardedPort?.Dispose();
                state.Client?.Disconnect();
                state.Client?.Dispose();
            }
            catch { /* ignore */ }
        }

        _tunnels.Clear();
    }
}
