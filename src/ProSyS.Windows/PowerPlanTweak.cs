using System.Runtime.InteropServices;
using ProSyS.Core;

namespace ProSyS.Windows;

/// <summary>
/// Switches the active power plan to Ultimate Performance or High performance (whichever exists) and restores the original plan.
/// Uses the documented powrprof API; switching between existing plans does not need administrator rights.
/// Many Modern Standby laptops only expose Balanced; there the tweak reports Unsupported and points to Power mode instead.
/// </summary>
public sealed class PowerPlanTweak : ITweak
{
    public const string BackupKind = "PowerScheme";
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public TweakMetadata Metadata { get; }
    public PowerPlanTweak(TweakMetadata metadata) => Metadata = metadata;

    public Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        var active = GetActiveScheme();
        var target = FindTarget();
        if (active is null) return Task.FromResult(Result(DetectionStatus.DetectionFailed, null, "The active power plan could not be read."));
        if (target is null) return Task.FromResult(Result(DetectionStatus.Unsupported, active.Value.ToString(), "No High performance or Ultimate Performance plan exists on this PC."));
        var compliant = active == target || active == UltimatePerformance;
        return Task.FromResult(Result(compliant ? DetectionStatus.Compliant : DetectionStatus.NonCompliant, active.Value.ToString(),
            compliant ? "A high-performance power plan is active." : "A higher-performance power plan is available."));
    }

    public Task<CompatibilityResult> EvaluateCompatibilityAsync(MachineSnapshot machine, CancellationToken cancellationToken = default) =>
        Task.FromResult(FindTarget() is null
            ? new CompatibilityResult(CompatibilityStatus.Unsupported, "This PC only exposes the Balanced plan (typical for Modern Standby laptops). Use Settings › System › Power › Power mode › Best performance instead.")
            : new CompatibilityResult(CompatibilityStatus.Compatible, "A high-performance power plan is available."));

    public Task<TweakBackup> BackupAsync(CancellationToken cancellationToken = default)
    {
        var active = GetActiveScheme() ?? throw new InvalidOperationException("The active power plan could not be read.");
        return Task.FromResult(new TweakBackup(Metadata.Id, true, active.ToString(), BackupKind, DateTimeOffset.UtcNow));
    }

    public Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var target = FindTarget() ?? throw new InvalidOperationException("No high-performance power plan exists on this PC.");
        SetActiveScheme(target);
        return Task.CompletedTask;
    }

    public Task<DetectionResult> VerifyAsync(CancellationToken cancellationToken = default) => DetectAsync(cancellationToken);

    public Task RollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default)
    {
        SetActiveScheme(Guid.Parse(ValueText.Canonical(backup.OriginalValue) ?? throw new InvalidDataException("The backup has no power plan.")));
        return Task.CompletedTask;
    }

    public Task<DetectionResult> VerifyRollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default) =>
        Task.FromResult(GetActiveScheme() is { } active ? Result(DetectionStatus.Present, active.ToString(), null) : Result(DetectionStatus.DetectionFailed, null, "The active power plan could not be read."));

    private DetectionResult Result(DetectionStatus status, object? value, string? detail) =>
        new(status, value, "Power plan (powrprof)", Confidence.Verified, DateTimeOffset.UtcNow, detail);

    private static Guid? FindTarget()
    {
        var schemes = EnumerateSchemes();
        return schemes.Contains(UltimatePerformance) ? UltimatePerformance : schemes.Contains(HighPerformance) ? HighPerformance : null;
    }

    private static Guid? GetActiveScheme()
    {
        if (!OperatingSystem.IsWindows() || PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    private static void SetActiveScheme(Guid scheme)
    {
        var result = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        if (result != 0) throw new InvalidOperationException($"Windows refused to switch the power plan (error {result}).");
    }

    private static HashSet<Guid> EnumerateSchemes()
    {
        var result = new HashSet<Guid>();
        if (!OperatingSystem.IsWindows()) return result;
        var buffer = new byte[16];
        for (uint index = 0; ; index++)
        {
            var size = (uint)buffer.Length;
            if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref size) != 0) break;
            result.Add(new Guid(buffer));
        }
        return result;
    }

    private const uint AccessScheme = 16;
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid schemeGuid);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
