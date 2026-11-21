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
    // 启动后自动检查新版本
    public bool CheckUpdatesOnStartup { get; set; } = true;
    // 窗口逻辑尺寸（DIP），跨 DPI 和多显示器保持可用
    public double WindowWidth { get; set; } = 1080;
    public double WindowHeight { get; set; } = 680;
}

public class ConfigFile
{
    public int Version { get; set; } = 2;
    public List<TunnelConfig> Tunnels { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
