namespace SSHTunnelManager.Models;

public class AppSettings
{
    public bool AutoStartMinimized { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public string Theme { get; set; } = "System";
}

public class ConfigFile
{
    public int Version { get; set; } = 2;
    public List<TunnelConfig> Tunnels { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
