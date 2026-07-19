namespace SSHTunnelManager.Models;

public enum TunnelStatus
{
    Disconnected,
    Connecting,
    HostKeyPending,
    Connected,
    Failed
}
