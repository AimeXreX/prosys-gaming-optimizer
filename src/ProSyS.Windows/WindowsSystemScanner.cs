using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ProSyS.Core;

namespace ProSyS.Windows;

public sealed class WindowsSystemScanner : ISystemScanner
{
    public async Task<MachineSnapshot> ScanAsync(CancellationToken cancellationToken = default)
    {
        var build = Environment.OSVersion.Version.Build;
        var edition = ReadString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? "Unknown";
        var displayVersion = ReadString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion") ?? Environment.OSVersion.Version.ToString();
        var cpu = ReadString(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString")?.Trim() ?? "Unknown";
        var gpus = ScanGpus();
        var drives = DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed)
            .Select(x => new DriveInfoSnapshot(x.Name, x.DriveFormat, x.TotalSize, x.AvailableFreeSpace,
                string.Equals(x.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))).ToArray();
        var memory = new MemoryStatusEx();
        memory.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        _ = GlobalMemoryStatusEx(ref memory);
        var laptop = GetSystemPowerStatus(out var power) && power.BatteryFlag is not (128 or 255);
        var secureBoot = (ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled") ?? 0) == 1;
        var antiCheat = DetectAntiCheat();
        var idMaterial = $"{Environment.MachineName}|{cpu}|{build}";
        var machineHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idMaterial))).ToLowerInvariant()[..16];
        var snapshotId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{machineHash}";
        var network = ScanNetwork();
        var performanceTask = SamplePerformanceAsync(cancellationToken);
        var networkQualityTask = ProbeGatewayAsync(network, cancellationToken);
        var insights = new SystemInsights(memory.ullAvailPhys, network, ScanProcesses(), ScanStartup(), ScanServices(),
            ScanGamingConfiguration(), ScanSteamGames(), await networkQualityTask, await performanceTask,
            NetworkConnectionScanner.Scan(), GpuVendorApiDetector.Detect(gpus));
        return new MachineSnapshot(snapshotId, DateTimeOffset.UtcNow, machineHash, edition, displayVersion,
            build, RuntimeInformation.OSArchitecture.ToString(), cpu, Environment.ProcessorCount, memory.ullTotalPhys,
            gpus, drives, laptop, secureBoot, GetPowerPlan(), antiCheat, insights);
    }

    private static async Task<NetworkQualitySnapshot?> ProbeGatewayAsync(IReadOnlyList<NetworkAdapterSnapshot> adapters, CancellationToken ct)
    {
        var target = adapters.FirstOrDefault(x => x.Status == OperationalStatus.Up.ToString() && x.Gateway != "—")?.Gateway;
        if (string.IsNullOrWhiteSpace(target)) return null;
        var latencies = new List<long>();
        using var ping = new Ping();
        for (var i = 0; i < 3; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(target, TimeSpan.FromMilliseconds(800), cancellationToken: ct);
                if (reply.Status == IPStatus.Success) latencies.Add(reply.RoundtripTime);
            }
            catch (Exception ex) when (ex is PingException or ArgumentException) { }
        }
        if (latencies.Count == 0) return new(target, 3, 0, 0, 0);
        var average = latencies.Average();
        var jitter = latencies.Count < 2 ? 0 : latencies.Zip(latencies.Skip(1), (a, b) => Math.Abs(a - b)).Average();
        return new(target, 3, latencies.Count, average, jitter);
    }

    private static async Task<PerformanceSnapshot?> SamplePerformanceAsync(CancellationToken ct)
    {
        if (!GetSystemTimes(out var idle1, out var kernel1, out var user1)) return null;
        await Task.Delay(250, ct);
        if (!GetSystemTimes(out var idle2, out var kernel2, out var user2)) return null;
        var idle = ToUInt64(idle2) - ToUInt64(idle1);
        var kernel = ToUInt64(kernel2) - ToUInt64(kernel1);
        var user = ToUInt64(user2) - ToUInt64(user1);
        var total = kernel + user;
        var cpu = total == 0 ? 0 : Math.Clamp((total - idle) * 100d / total, 0, 100);
        return new(cpu, Process.GetProcesses().Length);
    }

    private static IReadOnlyList<NetworkAdapterSnapshot> ScanNetwork()
    {
        var result = new List<NetworkAdapterSnapshot>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            try
            {
                var properties = nic.GetIPProperties();
                var addresses = properties.UnicastAddresses.Select(x => x.Address.ToString()).Where(x => !x.Contains('%')).Take(4).ToArray();
                var dns = properties.DnsAddresses.Select(x => x.ToString()).Take(4).ToArray();
                var gateway = properties.GatewayAddresses.Select(x => x.Address)
                    .FirstOrDefault(x => !x.Equals(IPAddress.Any) && !x.Equals(IPAddress.IPv6Any) && !IPAddress.IsLoopback(x))?.ToString() ?? "—";
                result.Add(new(nic.Name, nic.NetworkInterfaceType.ToString(), nic.Speed, nic.OperationalStatus.ToString(), addresses, dns, gateway));
            }
            catch (NetworkInformationException) { result.Add(new(nic.Name, nic.NetworkInterfaceType.ToString(), 0, "Unavailable", Array.Empty<string>(), Array.Empty<string>(), "—")); }
        }
        return result.OrderByDescending(x => x.Status == OperationalStatus.Up.ToString()).ThenByDescending(x => x.SpeedBitsPerSecond).ToArray();
    }

    private static IReadOnlyList<ProcessSnapshot> ScanProcesses()
    {
        var protectedNames = new HashSet<string>(new[] { "system", "registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon" }, StringComparer.OrdinalIgnoreCase);
        var securityNames = new[] { "MsMpEng", "vgc", "vgtray", "BEService", "EasyAntiCheat", "FACEIT" };
        var rows = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var classification = protectedNames.Contains(process.ProcessName) ? "System critical"
                    : securityNames.Any(x => process.ProcessName.Contains(x, StringComparison.OrdinalIgnoreCase)) ? "Security / anti-cheat"
                    : process.SessionId == 0 ? "Windows / service" : "User app";
                rows.Add(new(process.Id, process.ProcessName, process.WorkingSet64, process.TotalProcessorTime, classification));
            }
            catch (Exception) when (process.HasExited || process.Id == 0) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        return rows.OrderByDescending(x => x.WorkingSetBytes).Take(20).ToArray();
    }

    private static IReadOnlyList<StartupSnapshot> ScanStartup()
    {
        var result = new List<StartupSnapshot>();
        ReadStartupHive(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Current user", result);
        ReadStartupHive(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "All users", result);
        return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static void ReadStartupHive(RegistryKey hive, string path, string source, ICollection<StartupSnapshot> result)
    {
        try
        {
            using var key = hive.OpenSubKey(path, false);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                var command = key.GetValue(name)?.ToString() ?? string.Empty;
                result.Add(new(name, source, SanitizeCommand(command), source == "All users" ? "Machine registration" : "User registration"));
            }
        }
        catch (UnauthorizedAccessException) { }
    }

    private static string SanitizeCommand(string command)
    {
        var match = Regex.Match(command, "^(?:\\\")?([^\\\"]+\\.exe)", RegexOptions.IgnoreCase);
        var executable = match.Success ? match.Groups[1].Value : command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Unknown";
        return Path.GetFileName(executable.Trim('"'));
    }

    private static ServiceSummary ScanServices()
    {
        try
        {
            var services = ServiceController.GetServices();
            var running = services.Count(x => x.Status == ServiceControllerStatus.Running);
            foreach (var service in services) service.Dispose();
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            var automatic = 0;
            var disabled = 0;
            foreach (var name in root?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var key = root!.OpenSubKey(name);
                if (key?.GetValue("Start") is int start) { if (start == 2) automatic++; else if (start == 4) disabled++; }
            }
            return new(services.Length, running, automatic, disabled);
        }
        catch { return new(-1, -1, -1, -1); }
    }

    private static GamingConfiguration ScanGamingConfiguration()
    {
        bool? gameMode = ReadNullableDword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") is int gm ? gm == 1 : null;
        bool? capture = ReadNullableDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled") is int cap ? cap == 1 : null;
        bool? memoryIntegrity = ReadNullableDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled") is int mi ? mi == 1 : null;
        return new(gameMode, capture, memoryIntegrity, null);
    }

    private static IReadOnlyList<string> ScanSteamGames()
    {
        var games = new List<string>();
        var steamPath = ReadString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        if (string.IsNullOrWhiteSpace(steamPath)) return games;
        var steamApps = Path.Combine(steamPath.Replace('/', Path.DirectorySeparatorChar), "steamapps");
        if (!Directory.Exists(steamApps)) return games;
        foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf").Take(300))
        {
            try
            {
                var nameLine = File.ReadLines(manifest).FirstOrDefault(x => x.Contains("\"name\"", StringComparison.OrdinalIgnoreCase));
                var match = nameLine is null ? Match.Empty : Regex.Match(nameLine, "\"name\"\\s+\"(?<name>[^\"]+)\"");
                if (match.Success) games.Add(match.Groups["name"].Value);
            }
            catch (IOException) { }
        }
        return games.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static IReadOnlyList<GpuInfo> ScanGpus()
    {
        var result = new List<GpuInfo>();
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
        if (root is null) return result;
        foreach (var adapter in root.GetSubKeyNames())
        foreach (var index in new[] { "0000", "0001" })
        {
            using var key = root.OpenSubKey($"{adapter}\\{index}");
            var name = key?.GetValue("DriverDesc")?.ToString();
            if (!string.IsNullOrWhiteSpace(name) && result.All(x => !string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                result.Add(new(name, key?.GetValue("DriverVersion")?.ToString() ?? "Unknown", "Windows display registry"));
        }
        return result;
    }

    private static IReadOnlyList<string> DetectAntiCheat()
    {
        var patterns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["vgc"] = "Riot Vanguard", ["EasyAntiCheat"] = "Easy Anti-Cheat", ["BEService"] = "BattlEye", ["FACEIT"] = "FACEIT Anti-Cheat" };
        var found = new List<string>();
        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null) return found;
        var names = services.GetSubKeyNames();
        foreach (var pair in patterns)
            if (names.Any(x => x.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))) found.Add(pair.Value);
        return found;
    }

    private static string GetPowerPlan()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powercfg.exe", "/getactivescheme")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            var output = process?.StandardOutput.ReadToEnd() ?? "Unknown";
            process?.WaitForExit(3000);
            return output.Trim();
        }
        catch { return "Unavailable"; }
    }

    private static string? ReadString(RegistryKey hive, string path, string name) => hive.OpenSubKey(path)?.GetValue(name)?.ToString();
    private static int? ReadDword(RegistryKey hive, string path, string name) => hive.OpenSubKey(path)?.GetValue(name) as int?;
    private static int? ReadNullableDword(RegistryKey hive, string path, string name) => hive.OpenSubKey(path)?.GetValue(name) is int value ? value : null;

    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatusEx
    { public uint dwLength; public uint dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential)] private struct SystemPowerStatus
    { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low; public uint High; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;
}
