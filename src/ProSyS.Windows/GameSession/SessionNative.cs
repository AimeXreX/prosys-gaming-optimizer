using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ProSyS.Windows.GameSession;

/// <summary>Documented Win32 entry points used by game-session levers. Handles are opened with the narrowest access right each call needs.</summary>
internal static class SessionNative
{
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint ProcessSetLimitedInformation = 0x2000;
    public const uint ProcessSetInformation = 0x0200;
    public const uint NormalPriorityClass = 0x20;
    public const uint BelowNormalPriorityClass = 0x4000;
    public const uint PowerThrottlingExecutionSpeed = 0x1;
    private const int ProcessPowerThrottling = 4;

    public static SafeProcessHandle Open(int processId, uint access)
    {
        var handle = OpenProcess(access, false, (uint)processId);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        return handle;
    }

    /// <summary>Process creation time disambiguates recycled PIDs when restoring after a crash.</summary>
    public static long? GetStartTicks(int processId)
    {
        try
        {
            using var handle = Open(processId, ProcessQueryLimitedInformation);
            return GetProcessTimes(handle, out var created, out _, out _, out _) ? created : null;
        }
        catch (Win32Exception) { return null; }
    }

    public static string? GetImagePath(int processId)
    {
        try
        {
            using var handle = Open(processId, ProcessQueryLimitedInformation);
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        catch (Win32Exception) { return null; }
    }

    public static uint GetPriority(SafeProcessHandle handle) => GetPriorityClass(handle) is var value and not 0 ? value : throw new Win32Exception(Marshal.GetLastWin32Error());
    public static void SetPriority(SafeProcessHandle handle, uint priority) { if (!SetPriorityClass(handle, priority)) throw new Win32Exception(Marshal.GetLastWin32Error()); }

    public static PowerThrottlingState GetThrottling(SafeProcessHandle handle)
    {
        var state = new PowerThrottlingState { Version = 1 };
        return GetProcessInformation(handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>()) ? state : new PowerThrottlingState { Version = 1 };
    }

    public static void SetThrottling(SafeProcessHandle handle, uint controlMask, uint stateMask)
    {
        var state = new PowerThrottlingState { Version = 1, ControlMask = controlMask, StateMask = stateMask };
        if (!SetProcessInformation(handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>())) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static IReadOnlyList<CpuSetInfo> GetCpuSets()
    {
        _ = GetSystemCpuSetInformation(IntPtr.Zero, 0, out var required, IntPtr.Zero, 0);
        if (required == 0) return Array.Empty<CpuSetInfo>();
        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            if (!GetSystemCpuSetInformation(buffer, required, out var returned, IntPtr.Zero, 0)) return Array.Empty<CpuSetInfo>();
            var result = new List<CpuSetInfo>();
            var offset = 0;
            while (offset < returned)
            {
                var size = Marshal.ReadInt32(buffer, offset);
                if (size <= 0) break;
                if (Marshal.ReadInt32(buffer, offset + 4) == 0)
                {
                    var flags = Marshal.ReadByte(buffer, offset + 19);
                    result.Add(new CpuSetInfo((uint)Marshal.ReadInt32(buffer, offset + 8), Marshal.ReadByte(buffer, offset + 15),
                        Marshal.ReadByte(buffer, offset + 18), (flags & 0x1) != 0));
                }
                offset += size;
            }
            return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static uint[] GetProcessCpuSets(SafeProcessHandle handle)
    {
        if (GetProcessDefaultCpuSets(handle, null, 0, out var required) || required == 0) return Array.Empty<uint>();
        var ids = new uint[required];
        return GetProcessDefaultCpuSets(handle, ids, required, out _) ? ids : throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static void SetProcessCpuSets(SafeProcessHandle handle, uint[] ids)
    {
        if (!SetProcessDefaultCpuSets(handle, ids.Length == 0 ? null : ids, (uint)ids.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)] public struct PowerThrottlingState { public uint Version; public uint ControlMask; public uint StateMask; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] buffer, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetPriorityClass(SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetPriorityClass(SafeProcessHandle process, uint priority);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessInformation(SafeProcessHandle process, int informationClass, ref PowerThrottlingState information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessInformation(SafeProcessHandle process, int informationClass, ref PowerThrottlingState information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemCpuSetInformation(IntPtr information, uint length, out uint returned, IntPtr process, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessDefaultCpuSets(SafeProcessHandle process, uint[]? ids, uint count, out uint required);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessDefaultCpuSets(SafeProcessHandle process, uint[]? ids, uint count);
}

public sealed record CpuSetInfo(uint Id, byte CoreIndex, byte EfficiencyClass, bool Parked);
