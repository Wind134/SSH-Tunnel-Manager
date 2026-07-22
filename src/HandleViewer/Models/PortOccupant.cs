namespace HandleViewer.Models;

public enum TcpEntryKind
{
    Listener,    // LISTEN state, no remote endpoint
    Established, // connected to a remote peer
}

public enum IpFamily
{
    IPv4,
    IPv6,
}

public class PortOccupant
{
    public int Pid { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string ProcessPath { get; init; } = string.Empty;
    public IpFamily Family { get; init; }
    public TcpEntryKind Kind { get; init; }
    public string LocalAddress { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public string RemoteAddress { get; init; } = string.Empty;
    public int RemotePort { get; init; }
    public string State { get; init; } = string.Empty;
}
