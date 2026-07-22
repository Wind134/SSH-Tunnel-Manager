using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using HandleViewer.Models;

namespace HandleViewer.Services;

/// <summary>
/// Enumerates TCP listeners and established connections, resolving PID -> process
/// name / path via the extended IP Helper API (GetExtendedTcpTable) because the
/// managed System.Net.NetworkInformation layer doesn't expose owning PID.
/// </summary>
public static class PortInspector
{
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int TableClass, int Reserved);

    // MIB_TCPROW_OWNER_PID — 24 bytes per row.
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    // MIB_TCP6ROW_OWNER_PID — 56 bytes per row.
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
        public uint dwRemoteScopeId;
        public uint dwRemotePort;
        public uint dwState;
        public uint dwOwningPid;
    }

    public static List<PortOccupant> GetAllTcpEntries()
    {
        var list = new List<PortOccupant>();
        list.AddRange(GetIPv4());
        list.AddRange(GetIPv6());

        // Listeners first, then established — easier to read in the UI.
        return list
            .OrderBy(o => o.Kind != TcpEntryKind.Listener)
            .ThenBy(o => o.LocalPort)
            .ThenBy(o => o.Pid)
            .ToList();
    }

    private static List<PortOccupant> GetIPv4()
    {
        var list = new List<PortOccupant>();
        int size = 0;
        var first = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (first != ERROR_INSUFFICIENT_BUFFER || size == 0)
            return list;

        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return list;

            int count = Marshal.ReadInt32(buf);
            IntPtr rowPtr = IntPtr.Add(buf, sizeof(int));
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr = IntPtr.Add(rowPtr, rowSize);

                bool isListener = row.dwState == 2; // MIB_TCP_STATE_LISTEN
                ushort localPort = Swap((ushort)(row.dwLocalPort & 0xFFFF));
                ushort remotePort = Swap((ushort)(row.dwRemotePort & 0xFFFF));

                var (name, path) = GetProcessInfo((int)row.dwOwningPid);

                list.Add(new PortOccupant
                {
                    Pid = (int)row.dwOwningPid,
                    ProcessName = name,
                    ProcessPath = path,
                    Family = IpFamily.IPv4,
                    Kind = isListener ? TcpEntryKind.Listener : TcpEntryKind.Established,
                    LocalAddress = new IPAddress(row.dwLocalAddr).ToString(),
                    LocalPort = localPort,
                    RemoteAddress = isListener ? "—" : new IPAddress(row.dwRemoteAddr).ToString(),
                    RemotePort = isListener ? 0 : remotePort,
                    State = StateName(row.dwState)
                });
            }
        }
        finally { Marshal.FreeHGlobal(buf); }

        return list;
    }

    private static List<PortOccupant> GetIPv6()
    {
        var list = new List<PortOccupant>();
        int size = 0;
        var first = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        if (first != ERROR_INSUFFICIENT_BUFFER || size == 0)
            return list;

        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return list;

            int count = Marshal.ReadInt32(buf);
            IntPtr rowPtr = IntPtr.Add(buf, sizeof(int));
            int rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();

            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(rowPtr);
                rowPtr = IntPtr.Add(rowPtr, rowSize);

                bool isListener = row.dwState == 2;
                ushort localPort = Swap((ushort)(row.dwLocalPort & 0xFFFF));
                ushort remotePort = Swap((ushort)(row.dwRemotePort & 0xFFFF));

                var (name, path) = GetProcessInfo((int)row.dwOwningPid);

                list.Add(new PortOccupant
                {
                    Pid = (int)row.dwOwningPid,
                    ProcessName = name,
                    ProcessPath = path,
                    Family = IpFamily.IPv6,
                    Kind = isListener ? TcpEntryKind.Listener : TcpEntryKind.Established,
                    LocalAddress = new IPAddress(row.LocalAddr, (uint)row.dwLocalScopeId).ToString(),
                    LocalPort = localPort,
                    RemoteAddress = isListener ? "—" : new IPAddress(row.RemoteAddr, (uint)row.dwRemoteScopeId).ToString(),
                    RemotePort = isListener ? 0 : remotePort,
                    State = StateName(row.dwState)
                });
            }
        }
        finally { Marshal.FreeHGlobal(buf); }

        return list;
    }

    private static (string name, string path) GetProcessInfo(int pid)
    {
        if (pid == 0) return ("System Idle", string.Empty);
        try
        {
            using var p = Process.GetProcessById(pid);
            string path = string.Empty;
            try { path = p.MainModule?.FileName ?? string.Empty; }
            catch { /* system / elevated processes throw on MainModule access */ }

            return (p.ProcessName, path);
        }
        catch
        {
            return ("(access denied)", string.Empty);
        }
    }

    // ntohs equivalent — port numbers come back in network byte order.
    private static ushort Swap(ushort v) => (ushort)((v >> 8) | (v << 8));

    private static string StateName(uint state) => state switch
    {
        1 => "CLOSED",
        2 => "LISTEN",
        3 => "SYN_SENT",
        4 => "SYN_RCVD",
        5 => "ESTABLISHED",
        6 => "FIN_WAIT1",
        7 => "FIN_WAIT2",
        8 => "CLOSE_WAIT",
        9 => "CLOSING",
        10 => "TIME_WAIT",
        11 => "DELETE_TCB",
        _ => $"state={state}",
    };
}
