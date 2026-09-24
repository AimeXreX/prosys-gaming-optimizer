using System.Collections.ObjectModel;

namespace ProSyS.Core;

public enum RiskLevel { None = 0, VeryLow = 1, Low = 2, Moderate = 3, High = 4, Experimental = 5 }
public enum BenefitLevel { None, Negligible, Low, Moderate, PotentiallyHigh, Unknown }
public enum EvidenceType { OfficialDocumentation, VendorDocumentation, MeasuredOnThisMachine, ControlledBenchmark, GenerallySupportedBehavior, HardwareDependent, Experimental, Legacy, Unknown }
public enum Confidence { Verified, High, Medium, Experimental, Unknown }
public enum DetectionStatus { Enabled, Disabled, Unsupported, Unavailable, Unknown, DetectionFailed, PermissionRequired }
public enum CompatibilityStatus { Compatible, Unsupported, PermissionRequired, Unknown }
public enum Reversibility { Easy, Moderate, Difficult }
public enum OperationState { Created, Analyzed, Planned, BackedUp, Applying, Verifying, Completed, PartiallyFailed, RollbackPending, RollingBack, RolledBack, RecoveryRequired }

public sealed record RiskProfile(
    RiskLevel Level, int PerformanceImpact, int LatencyImpact, int StabilityRisk,
    int SecurityImpact, int CompatibilityRisk, Reversibility Reversibility, Confidence Confidence)
{
    public static RiskProfile Safe(int performance = 1, int latency = 0) =>
        new(RiskLevel.VeryLow, performance, latency, 0, 0, 1, Reversibility.Easy, Confidence.High);
}

public sealed record DetectionResult(
    DetectionStatus Status, object? Value, string Source, Confidence Confidence,
    DateTimeOffset Timestamp, string? Detail = null);

public sealed record CompatibilityResult(CompatibilityStatus Status, string Reason);

public sealed record TweakMetadata(
    string Id, int Revision, string Name, string Description, string Category,
    string Why, RiskProfile Risk, BenefitLevel Benefit, EvidenceType Evidence,
    bool RequiresRestart, bool RequiresSignOut, bool RequiresAdministrator,
    bool AffectsSecurity, bool AffectsAntiCheat, string[] SupportedBuilds,
    string[] Requires, string[] ConflictsWith, string[] MustRunAfter,
    string RollbackMethod, string LastReviewed, bool RecommendedByDefault = true);

public sealed record MachineSnapshot(
    string Id, DateTimeOffset CapturedAt, string MachineNameHash, string WindowsEdition,
    string WindowsVersion, int WindowsBuild, string Architecture, string Cpu,
    int LogicalProcessors, ulong TotalMemoryBytes, IReadOnlyList<GpuInfo> Gpus,
    IReadOnlyList<DriveInfoSnapshot> Drives, bool IsLaptop, bool SecureBoot,
    string PowerPlan, IReadOnlyList<string> AntiCheatProducts, SystemInsights? Insights = null);

public sealed record GpuInfo(string Name, string DriverVersion, string Source);
public sealed record DriveInfoSnapshot(string Name, string Format, long TotalBytes, long FreeBytes, bool IsSystem);
public sealed record NetworkAdapterSnapshot(string Name, string Type, long SpeedBitsPerSecond, string Status,
    IReadOnlyList<string> Addresses, IReadOnlyList<string> DnsServers, string Gateway);
public sealed record ProcessSnapshot(int Id, string Name, long WorkingSetBytes, TimeSpan TotalProcessorTime, string Classification);
public sealed record StartupSnapshot(string Name, string Source, string Command, string PublisherClass);
public sealed record ServiceSummary(int Total, int Running, int Automatic, int Disabled);
public sealed record GamingConfiguration(bool? GameModeEnabled, bool? CaptureEnabled, bool? MemoryIntegrityEnabled, bool? HypervisorPresent);
public sealed record NetworkQualitySnapshot(string Target, int Sent, int Received, double AverageLatencyMs, double JitterMs);
public sealed record PerformanceSnapshot(double CpuUtilizationPercent, int ProcessCount);
public sealed record NetworkConnectionSnapshot(int ProcessId, string ProcessName, string LocalEndpoint, string RemoteEndpoint, string State);
public sealed record GpuApiCapability(string Vendor, string Api, bool Available, string Detail);
public sealed record SystemInsights(ulong AvailableMemoryBytes, IReadOnlyList<NetworkAdapterSnapshot> NetworkAdapters,
    IReadOnlyList<ProcessSnapshot> TopProcesses, IReadOnlyList<StartupSnapshot> StartupItems,
    ServiceSummary Services, GamingConfiguration Gaming, IReadOnlyList<string> InstalledGames,
    NetworkQualitySnapshot? NetworkQuality = null, PerformanceSnapshot? Performance = null,
    IReadOnlyList<NetworkConnectionSnapshot>? NetworkConnections = null, IReadOnlyList<GpuApiCapability>? GpuApis = null);

public sealed record PlannedTweak(
    string TweakId, string Name, DetectionResult Current, CompatibilityResult Compatibility,
    RiskProfile Risk, BenefitLevel Benefit, bool Selected, string ExpectedChange);

public sealed record OptimizationPlan(
    Guid PlanId, DateTimeOffset CreatedAt, string MachineSnapshotId,
    ReadOnlyCollection<PlannedTweak> Tweaks, string Sha256);

public sealed record TweakBackup(string TweakId, bool ValueExisted, object? OriginalValue, string ValueKind, DateTimeOffset CapturedAt);
public sealed record TweakExecutionResult(string TweakId, bool Success, string Message, DetectionResult? Verification);
public sealed record SessionSummary(Guid SessionId, OperationState State, string BackupDirectory, IReadOnlyList<TweakExecutionResult> Results);

public enum BenchmarkComparability { High, Medium, Low, Invalid }
public sealed record BenchmarkFingerprint(string GameProcess, string WindowsBuild, string GpuDriver, string PowerPlan, string SnapshotId);
public sealed record BenchmarkSummary(Guid RunId, DateTimeOffset CapturedAt, string Source, string GameProcess, int FrameCount,
    double AverageFps, double OnePercentLowFps, double PointOnePercentLowFps, double MedianFrameTimeMs,
    double P99FrameTimeMs, double StandardDeviationMs, BenchmarkFingerprint Fingerprint, string CsvPath);
public sealed record BenchmarkComparison(BenchmarkComparability Comparability, string Reason, double AverageFpsDeltaPercent,
    double OnePercentLowDeltaPercent, double P99FrameTimeDeltaPercent, bool MeaningfulImprovement);

public enum OptimizationProfileKind { Safe, Balanced, Competitive, Experimental }
public sealed record GameProfile(Guid Id, string Name, string GameProcess, string? ExecutablePath, OptimizationProfileKind Profile,
    IReadOnlyList<string> TweakIds, bool RestoreOnExit, bool OverlayEnabled, DateTimeOffset UpdatedAt, bool CpuPriorityEnabled = false);

public interface ITweak
{
    TweakMetadata Metadata { get; }
    Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default);
    Task<CompatibilityResult> EvaluateCompatibilityAsync(MachineSnapshot machine, CancellationToken cancellationToken = default);
    Task<TweakBackup> BackupAsync(CancellationToken cancellationToken = default);
    Task ApplyAsync(CancellationToken cancellationToken = default);
    Task<DetectionResult> VerifyAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default);
    Task<DetectionResult> VerifyRollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default);
}

public interface ISystemScanner { Task<MachineSnapshot> ScanAsync(CancellationToken cancellationToken = default); }
public interface IAuditLog { Task WriteAsync(object entry, CancellationToken cancellationToken = default); }
