using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using ProSyS.Core;

namespace ProSyS.Windows;

public static class NetworkConnectionScanner
{
    public static IReadOnlyList<NetworkConnectionSnapshot> Scan()
    {
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2, TcpTableClass.OwnerPidAll, 0);
        if (size <= 0) return Array.Empty<NetworkConnectionSnapshot>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, true, 2, TcpTableClass.OwnerPidAll, 0) != 0) return Array.Empty<NetworkConnectionSnapshot>();
            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<TcpRow>();
            var rows = new List<NetworkConnectionSnapshot>(Math.Min(count, 500));
            for (var i = 0; i < count && i < 500; i++)
            {
                var row = Marshal.PtrToStructure<TcpRow>(IntPtr.Add(buffer, sizeof(uint) + i * rowSize));
                var name = "PID " + row.ProcessId;
                try { using var process = Process.GetProcessById((int)row.ProcessId); name = process.ProcessName; } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                rows.Add(new((int)row.ProcessId, name, Endpoint(row.LocalAddress, row.LocalPort), Endpoint(row.RemoteAddress, row.RemotePort), ((TcpState)row.State).ToString()));
            }
            return rows.OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.RemoteEndpoint).ToArray();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string Endpoint(uint address, uint port)
    {
        var ip = new IPAddress(BitConverter.GetBytes(address));
        var bytes = BitConverter.GetBytes(port);
        return $"{ip}:{bytes[0] * 256 + bytes[1]}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow { public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, ProcessId; }
    private enum TcpTableClass { BasicListener, BasicConnections, BasicAll, OwnerPidListener, OwnerPidConnections, OwnerPidAll }
    private enum TcpState { Closed = 1, Listen, SynSent, SynReceived, Established, FinWait1, FinWait2, CloseWait, Closing, LastAck, DeleteTcb }
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int ipVersion, TcpTableClass tableClass, uint reserved);
}
