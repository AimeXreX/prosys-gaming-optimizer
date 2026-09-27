using ProSyS.Core;
using Xunit;

namespace ProSyS.Tests;

/// <summary>A fact that needs real Windows APIs (registry, iphlpapi, process priority, WMI).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires Windows."; }
}

/// <summary>A fact that needs the PresentMon binary fetched by tools/PresentMon/Get-PresentMon.ps1.</summary>
public sealed class PresentMonFactAttribute : FactAttribute
{
    public PresentMonFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows.";
        else if (!File.Exists(TestData.PresentMonPath)) Skip = "PresentMon is not downloaded; run tools/PresentMon/Get-PresentMon.ps1.";
    }
}

public static class TestData
{
    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProSyS.sln"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    public static string PresentMonPath => Path.Combine(RepositoryRoot, "tools", "PresentMon", "2.6.0", "PresentMon.exe");

    public static MachineSnapshot Snapshot() => new("snapshot", DateTimeOffset.UtcNow, "hash", "Windows 11", "24H2", 26100, "X64", "CPU", 8, 16UL * 1024 * 1024 * 1024,
        Array.Empty<GpuInfo>(), Array.Empty<DriveInfoSnapshot>(), false, true, "Balanced", Array.Empty<string>());

    public static TweakMetadata Metadata(string id, string[]? after = null, EvidenceType evidence = EvidenceType.GenerallySupportedBehavior, bool recommended = true) =>
        new(id, 1, id, id, "Test", "Test", RiskProfile.Safe(), BenefitLevel.Low, evidence, false, false, false, false, false,
            new[] { "Windows 11" }, Array.Empty<string>(), Array.Empty<string>(), after ?? Array.Empty<string>(), "Restore", "2026-09-23", recommended);
}

public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProSyS.Tests", Guid.NewGuid().ToString("N"));
    public TempFolder() => Directory.CreateDirectory(Path);
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
}

public sealed class NullAudit : IAuditLog
{
    public Task WriteAsync(object entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>A stateless tweak with no side effects.</summary>
public sealed class FakeTweak(TweakMetadata metadata) : ITweak
{
    public TweakMetadata Metadata { get; } = metadata;
    public Task ApplyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<TweakBackup> BackupAsync(CancellationToken cancellationToken = default) => Task.FromResult(new TweakBackup(Metadata.Id, false, null, "None", DateTimeOffset.UtcNow));
    public Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DetectionResult(DetectionStatus.Enabled, 1, "Fake boundary", Confidence.Verified, DateTimeOffset.UtcNow));
    public Task<CompatibilityResult> EvaluateCompatibilityAsync(MachineSnapshot machine, CancellationToken cancellationToken = default) => Task.FromResult(new CompatibilityResult(CompatibilityStatus.Compatible, "Test"));
    public Task RollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<DetectionResult> VerifyAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DetectionResult(DetectionStatus.Compliant, 0, "Fake boundary", Confidence.Verified, DateTimeOffset.UtcNow));
    public Task<DetectionResult> VerifyRollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default) => Task.FromResult(new DetectionResult(DetectionStatus.Unknown, null, "Fake boundary", Confidence.Verified, DateTimeOffset.UtcNow));
}

/// <summary>An in-memory value with injectable faults; rollback writes the backup back unless <see cref="CorruptRollback"/> is set.</summary>
public sealed class StatefulTweak(string id) : ITweak
{
    public object Value { get; set; } = 1;
    public object Recommended { get; init; } = 0;
    public string Kind { get; init; } = "DWord";
    public bool FailApply { get; set; }
    public bool CorruptRollback { get; set; }
    public Action<CancellationToken>? OnApply { get; set; }
    public TweakMetadata Metadata { get; } = TestData.Metadata(id);

    public Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DetectionResult(Same(Value, Recommended) ? DetectionStatus.Compliant : DetectionStatus.Enabled, Value, "test", Confidence.Verified, DateTimeOffset.UtcNow));
    public Task<CompatibilityResult> EvaluateCompatibilityAsync(MachineSnapshot machine, CancellationToken cancellationToken = default) => Task.FromResult(new CompatibilityResult(CompatibilityStatus.Compatible, "test"));
    public Task<TweakBackup> BackupAsync(CancellationToken cancellationToken = default) => Task.FromResult(new TweakBackup(Metadata.Id, true, Value, Kind, DateTimeOffset.UtcNow));
    public Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        OnApply?.Invoke(cancellationToken);
        if (FailApply) throw new InvalidOperationException("Injected apply failure");
        Value = Recommended;
        return Task.CompletedTask;
    }
    public Task<DetectionResult> VerifyAsync(CancellationToken cancellationToken = default) => DetectAsync(cancellationToken);
    public Task RollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default)
    {
        Value = CorruptRollback ? 99 : Restore(backup);
        return Task.CompletedTask;
    }
    public Task<DetectionResult> VerifyRollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DetectionResult(DetectionStatus.Enabled, Value, "test", Confidence.Verified, DateTimeOffset.UtcNow));

    private static object Restore(TweakBackup backup) => backup.OriginalValue is System.Text.Json.JsonElement
        ? ProSyS.Windows.RegistryTweak.ConvertBackupValue(backup)
        : backup.OriginalValue!;
    private static bool Same(object a, object b) => ValueText.Canonical(a) == ValueText.Canonical(b);
}
