using System.Net.NetworkInformation;

namespace SSHTunnelManager.Services;

public static class PortChecker
{
    public static bool IsPortInUse(int port)
    {
        var props = IPGlobalProperties.GetIPGlobalProperties();
        var listeners = props.GetActiveTcpListeners();
        return listeners.Any(l => l.Port == port);
    }
}
