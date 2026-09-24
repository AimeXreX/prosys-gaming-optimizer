using ProSyS.Core;

namespace ProSyS.Windows;

public static class GpuVendorApiDetector
{
    public static IReadOnlyList<GpuApiCapability> Detect(IReadOnlyList<GpuInfo> adapters)
    {
        var system = Environment.SystemDirectory;
        var nvidia = adapters.Any(x => x.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        var amd = adapters.Any(x => x.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase));
        var intel = adapters.Any(x => x.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase));
        return new[]
        {
            Capability("NVIDIA", "NVAPI", nvidia, Path.Combine(system, "nvapi64.dll")),
            Capability("AMD", "ADLX/ADL", amd, Path.Combine(system, "atiadlxx.dll")),
            Capability("Intel", "IGCL", intel, Path.Combine(system, "ControlLib.dll"))
        };
    }

    private static GpuApiCapability Capability(string vendor, string api, bool adapterPresent, string library)
    {
        var exists = adapterPresent && File.Exists(library);
        var detail = !adapterPresent ? "Compatible adapter not detected." : exists ? $"Official vendor runtime detected: {Path.GetFileName(library)}" : "Adapter detected; vendor telemetry runtime is unavailable.";
        return new(vendor, api, exists, detail);
    }
}
