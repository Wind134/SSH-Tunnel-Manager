namespace SSHTunnelManager.Models;

public class AppSettings
{
    public bool AutoStartMinimized { get; set; }
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool ConfirmBeforeExit { get; set; } = true;
    public bool ShowTrayNotifications { get; set; } = true;
    public string Theme { get; set; } = "System";
    public string StartPage { get; set; } = "LastUsed";
    public string LastPage { get; set; } = "Tunnel";
    public int PortAutoRefreshSeconds { get; set; }
    public bool ShowSystemProcesses { get; set; }
    // Stored as device-independent pixels so the window remains usable when
    // Windows display scaling or the active monitor changes.
    public double WindowWidth { get; set; } = 1120;
    public double WindowHeight { get; set; } = 720;
}

public class ConfigFile
{
    public int Version { get; set; } = 2;
    public List<TunnelConfig> Tunnels { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
