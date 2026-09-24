using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace ProSyS.Windows;

public sealed class LiveMetricsSampler
{
    private ulong? _lastIdle, _lastKernel, _lastUser, _lastNetwork;
    private DateTimeOffset? _lastAt;

    public LiveMetrics Sample()
    {
        var now = DateTimeOffset.UtcNow;
        var cpu = 0d;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var i = ToUInt64(idle); var k = ToUInt64(kernel); var u = ToUInt64(user);
            if (_lastIdle is { } li && _lastKernel is { } lk && _lastUser is { } lu)
            {
                var idleDelta = i - li; var total = k - lk + u - lu;
                if (total > 0) cpu = Math.Clamp((total - idleDelta) * 100d / total, 0, 100);
            }
            _lastIdle = i; _lastKernel = k; _lastUser = u;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        _ = GlobalMemoryStatusEx(ref memory);
        ulong network = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            try { var stats = nic.GetIPv4Statistics(); network += (ulong)Math.Max(0, stats.BytesReceived) + (ulong)Math.Max(0, stats.BytesSent); } catch (NetworkInformationException) { }
        var bytesPerSecond = 0d;
        if (_lastNetwork is { } previous && _lastAt is { } previousAt)
        {
            var seconds = (now - previousAt).TotalSeconds;
            if (seconds > 0 && network >= previous) bytesPerSecond = (network - previous) / seconds;
        }
        _lastNetwork = network; _lastAt = now;
        return new(cpu, memory.MemoryLoad, bytesPerSecond, now);
    }

    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;
}

public sealed record LiveMetrics(double CpuPercent, uint MemoryLoadPercent, double NetworkBytesPerSecond, DateTimeOffset CapturedAt);
