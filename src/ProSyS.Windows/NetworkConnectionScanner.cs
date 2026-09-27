using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using ProSyS.Core;

namespace ProSyS.Windows;

public static class NetworkConnectionScanner
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    public const int MaxRows = 2000;

    public static IReadOnlyList<NetworkConnectionSnapshot> Scan()
    {
        var names = new Dictionary<uint, string>();
        var rows = new List<NetworkConnectionSnapshot>();
        foreach (var (pid, local, remote, state) in ReadTable(AfInet).Concat(ReadTable(AfInet6)))
        {
            if (rows.Count >= MaxRows) break;
            rows.Add(new((int)pid, ProcessName(pid, names), local, remote, ((TcpState)state).ToString()));
        }
        return rows.OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.RemoteEndpoint).ToArray();
    }

    private static IEnumerable<(uint Pid, string Local, string Remote, uint State)> ReadTable(int family)
    {
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, true, family, TcpTableClass.OwnerPidAll, 0);
        if (size <= 0) return Array.Empty<(uint, string, string, uint)>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, true, family, TcpTableClass.OwnerPidAll, 0) != 0) return Array.Empty<(uint, string, string, uint)>();
            var count = Math.Min(Marshal.ReadInt32(buffer), MaxRows);
            var result = new List<(uint, string, string, uint)>(count);
            if (family == AfInet)
            {
                var rowSize = Marshal.SizeOf<TcpRow>();
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<TcpRow>(IntPtr.Add(buffer, sizeof(uint) + i * rowSize));
                    result.Add((row.ProcessId, Endpoint(new IPAddress(BitConverter.GetBytes(row.LocalAddress)), row.LocalPort),
                        Endpoint(new IPAddress(BitConverter.GetBytes(row.RemoteAddress)), row.RemotePort), row.State));
                }
            }
            else
            {
                var rowSize = Marshal.SizeOf<Tcp6Row>();
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<Tcp6Row>(IntPtr.Add(buffer, sizeof(uint) + i * rowSize));
                    result.Add((row.ProcessId, Endpoint(new IPAddress(row.LocalAddress, row.LocalScopeId), row.LocalPort),
                        Endpoint(new IPAddress(row.RemoteAddress, row.RemoteScopeId), row.RemotePort), row.State));
                }
            }
            return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string ProcessName(uint pid, Dictionary<uint, string> cache)
    {
        if (cache.TryGetValue(pid, out var cached)) return cached;
        var name = "PID " + pid;
        try { using var process = Process.GetProcessById((int)pid); name = process.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        cache[pid] = name;
        return name;
    }

    /// <summary>Formats an endpoint; the port is stored in network byte order in the low 16 bits.</summary>
    public static string Endpoint(IPAddress address, uint port)
    {
        var bytes = BitConverter.GetBytes(port);
        return new IPEndPoint(address, bytes[0] * 256 + bytes[1]).ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow { public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, ProcessId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
        public uint LocalScopeId, LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddress;
        public uint RemoteScopeId, RemotePort, State, ProcessId;
    }

    private enum TcpTableClass { BasicListener, BasicConnections, BasicAll, OwnerPidListener, OwnerPidConnections, OwnerPidAll }
    private enum TcpState { Closed = 1, Listen, SynSent, SynReceived, Established, FinWait1, FinWait2, CloseWait, Closing, LastAck, DeleteTcb }
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int ipVersion, TcpTableClass tableClass, uint reserved);
}
