using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ProSyS.Core;

namespace ProSyS.Windows;

/// <summary>
/// Read-only checks for common FPS limiters that ProSyS cannot or should not change itself (firmware, hardware, HKLM + reboot).
/// Nothing here mutates the system.
/// </summary>
public static class PerformanceAdvisor
{
    public sealed record MemoryModule(int SmbiosType, int ConfiguredMhz, ulong CapacityBytes);

    public static IReadOnlyList<PerformanceAdvice> Analyze(MachineSnapshot machine)
    {
        var onBattery = OperatingSystem.IsWindows() && GetSystemPowerStatus(out var power) && power.ACLineStatus == 0;
        var refresh = OperatingSystem.IsWindows() ? TweakCatalog.CreateTweaks().OfType<DisplayRefreshTweak>().First().DetectAsync().GetAwaiter().GetResult() : null;
        return Evaluate(machine, ReadMemoryModules(), onBattery, ReadHardwareGpuScheduling(), refresh);
    }

    /// <summary>The decision logic, separated from Windows data collection so it can be tested anywhere.</summary>
    public static IReadOnlyList<PerformanceAdvice> Evaluate(MachineSnapshot machine, IReadOnlyList<MemoryModule> memory, bool onBattery, int? gpuSchedulingMode, DetectionResult? refresh)
    {
        var advice = new List<PerformanceAdvice>();

        if (machine.IsLaptop && onBattery)
            advice.Add(new("on-battery", AdviceSeverity.Important, "Running on battery",
                "Laptops lower CPU and GPU clocks on battery. Plug in the charger while gaming.",
                "لپ‌تاپ با باتری کار می‌کند", "لپ‌تاپ‌ها روی باتری فرکانس پردازنده و کارت گرافیک را پایین می‌آورند. هنگام بازی شارژر را وصل کنید."));

        if (refresh is { Status: DetectionStatus.NonCompliant })
            advice.Add(new("refresh-rate", AdviceSeverity.Important, "Display runs below its highest refresh rate",
                $"{refresh.Detail} Apply \"Highest refresh rate\" in Recommendations.",
                "نمایشگر با کمتر از بیشترین نرخ تازه‌سازی کار می‌کند",
                $"{refresh.Detail} گزینه «بیشترین نرخ تازه‌سازی» را از بخش پیشنهادها اعمال کنید."));

        if (machine.Gpus.Count >= 2)
            advice.Add(new("hybrid-gpu", AdviceSeverity.Suggestion, "Integrated and dedicated GPU detected",
                "Make sure games run on the dedicated GPU: enable \"Use the high-performance GPU\" in the game's profile.",
                "کارت گرافیک مجتمع و مجزا شناسایی شد",
                "مطمئن شوید بازی‌ها روی کارت گرافیک مجزا اجرا می‌شوند: گزینه «استفاده از GPU پرقدرت» را در پروفایل بازی فعال کنید."));

        if (memory.Count == 1)
            advice.Add(new("single-channel", AdviceSeverity.Suggestion, "One memory module (single channel)",
                "Two matching modules run in dual channel, which typically raises FPS in CPU-bound games noticeably. On laptops check whether a second slot is free.",
                "فقط یک ماژول رم (تک‌کاناله)",
                "دو ماژول یکسان به‌صورت دوکاناله کار می‌کنند و معمولاً FPS بازی‌های وابسته به CPU را محسوس بالا می‌برند. در لپ‌تاپ بررسی کنید اسلات دوم خالی است یا نه."));

        var ddr4Base = memory.Any(x => x.SmbiosType == 26 && x.ConfiguredMhz is > 0 and <= 2666);
        var ddr5Base = memory.Any(x => x.SmbiosType == 34 && x.ConfiguredMhz is > 0 and <= 4800);
        if (!machine.IsLaptop && (ddr4Base || ddr5Base))
        {
            var speed = memory.Max(x => x.ConfiguredMhz);
            advice.Add(new("memory-base-speed", AdviceSeverity.Suggestion, $"Memory runs at base speed ({speed} MT/s)",
                "If your memory kit is rated faster (see its label or product page), enable XMP (Intel) or EXPO (AMD) in the BIOS/UEFI. ProSyS never changes firmware settings.",
                $"رم با سرعت پایه کار می‌کند ({speed} MT/s)",
                "اگر رم شما برای سرعت بالاتری ساخته شده (برچسب یا مشخصات آن را ببینید)، XMP (اینتل) یا EXPO (AMD) را در BIOS/UEFI فعال کنید. ProSyS هرگز تنظیمات فرم‌ور را تغییر نمی‌دهد."));
        }

        if (gpuSchedulingMode == 1)
            advice.Add(new("gpu-scheduling", AdviceSeverity.Info, "Hardware-accelerated GPU scheduling is off",
                "It is required for DLSS/FSR frame generation and can lower CPU overhead. Turn it on in Settings › System › Display › Graphics › Change default graphics settings, then restart.",
                "زمان‌بندی سخت‌افزاری GPU خاموش است",
                "برای Frame Generation در DLSS/FSR لازم است و می‌تواند سربار CPU را کم کند. آن را از Settings › System › Display › Graphics › Change default graphics settings روشن کنید و سیستم را ری‌استارت کنید."));

        if (machine.Drives.FirstOrDefault(x => x.IsSystem) is { TotalBytes: > 0 } system && (system.FreeBytes < system.TotalBytes / 10 || system.FreeBytes < 20L * 1024 * 1024 * 1024))
            advice.Add(new("disk-space", AdviceSeverity.Suggestion, $"Low free space on {system.Name} ({system.FreeBytes / 1073741824d:F0} GB free)",
                "Shader caches, updates and the page file need room. Keep at least 20 GB or 10% free on the system drive.",
                $"فضای خالی کم روی {system.Name} ({system.FreeBytes / 1073741824d:F0} گیگابایت آزاد)",
                "کش شیدرها، به‌روزرسانی‌ها و فایل Page به فضا نیاز دارند. دست‌کم ۲۰ گیگابایت یا ۱۰٪ درایو سیستم را آزاد نگه دارید."));

        if (machine.Insights?.Gaming.GameModeEnabled == false)
            advice.Add(new("game-mode", AdviceSeverity.Suggestion, "Game Mode is off",
                "Apply \"Game Mode\" in Recommendations.", "حالت بازی خاموش است", "«حالت بازی» را از بخش پیشنهادها اعمال کنید."));

        return advice.OrderByDescending(x => x.Severity).ToList();
    }

    private static IReadOnlyList<MemoryModule> ReadMemoryModules()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<MemoryModule>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SMBIOSMemoryType, ConfiguredClockSpeed, Capacity FROM Win32_PhysicalMemory");
            var result = new List<MemoryModule>();
            foreach (var item in searcher.Get())
                using (item)
                    result.Add(new(Convert.ToInt32(item["SMBIOSMemoryType"] ?? 0), Convert.ToInt32(item["ConfiguredClockSpeed"] ?? 0), Convert.ToUInt64(item["Capacity"] ?? 0UL)));
            return result;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { return Array.Empty<MemoryModule>(); }
    }

    /// <summary>HwSchMode: 2 = on, 1 = off, null = not reported (unsupported GPU/driver or default).</summary>
    private static int? ReadHardwareGpuScheduling()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            return key?.GetValue("HwSchMode") as int?;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
