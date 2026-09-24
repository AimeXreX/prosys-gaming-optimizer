using Microsoft.Win32;
using ProSyS.Core;
using ProSyS.Windows;
using ProSyS.App;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Safe risk policy accepts reversible evidence-backed tweaks", TestRiskPolicy),
    ("Dependency planner is deterministic", TestPlannerOrder),
    ("Dependency cycles are rejected", TestCycle),
    ("Registry apply/verify/rollback is idempotent", TestRegistryLifecycle),
    ("Optimization plan is immutable and hashed", TestPlanHash),
    ("Plan selection creates a new immutable hash", TestPlanSelection),
    ("Windows scanner returns real extended insights", TestExtendedScanner),
    ("Catalog exposes default and explicit opt-in choices", TestCatalogProfiles),
    ("PresentMon binary is pinned and signed", TestPresentMonTrust),
    ("Benchmark parser calculates frame statistics", TestBenchmarkParser),
    ("Game profile store round-trips atomically", TestGameProfileStore),
    ("Network connections retain owning process IDs", TestNetworkConnections),
    ("Signed update manifests reject tampering", TestSignedUpdateManifest),
    ("Live metrics remain inside valid ranges", TestLiveMetrics)
    ,("Production catalog exposes more than 100 unique reversible capabilities", TestProductionCatalog)
    ,("Tampered plans are rejected before mutation", TestTamperedPlan)
    ,("Automatic rollback is verified and reports recovery failures", TestVerifiedAutomaticRollback)
    ,("Readable HTML reports are generated without leaking raw markup", TestHtmlReport)
    ,("Every production capability has a Persian title, description and category", TestPersianCatalogCoverage)
    ,("CPU-bound game mode uses High priority and restores the exact process policy", TestCpuPrioritySession)
};
var failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
return failed;

static Task TestRiskPolicy()
{
    var accepted = new RiskEngine().AllowedInSafeProfile(TestTweak("a").Metadata);
    Assert(accepted, "Expected safe tweak to be accepted.");
    return Task.CompletedTask;
}

static Task TestPlannerOrder()
{
    var a = TestTweak("a");
    var b = TestTweak("b", after: new[] { "a" });
    var order = new DependencyPlanner().Order(new[] { b, a });
    Assert(order[0].Metadata.Id == "a" && order[1].Metadata.Id == "b", "Dependency order was not honored.");
    return Task.CompletedTask;
}

static Task TestCycle()
{
    var a = TestTweak("a", after: new[] { "b" });
    var b = TestTweak("b", after: new[] { "a" });
    try { _ = new DependencyPlanner().Order(new[] { a, b }); }
    catch (InvalidOperationException) { return Task.CompletedTask; }
    throw new InvalidOperationException("Cycle was accepted.");
}

static async Task TestRegistryLifecycle()
{
    const string path = @"Software\Microsoft\GameBar\ProSySOptimizer.Tests";
    const string name = "LifecycleValue";
    using (var key = Registry.CurrentUser.CreateSubKey(path, true)) key.SetValue(name, 37, RegistryValueKind.DWord);
    var tweak = new RegistryTweak(Metadata("test.registry"), path, name, 0);
    try
    {
        var backup = await tweak.BackupAsync();
        await tweak.ApplyAsync();
        Assert((await tweak.VerifyAsync()).Status == DetectionStatus.Disabled, "Apply verification failed.");
        await tweak.RollbackAsync(backup);
        Assert((await tweak.VerifyRollbackAsync(backup)).Value?.ToString() == "37", "Original value was not restored.");
        await tweak.RollbackAsync(backup);
        Assert((await tweak.VerifyRollbackAsync(backup)).Value?.ToString() == "37", "Second rollback was not stable.");
    }
    finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
}

static async Task TestPlanHash()
{
    var snapshot = new MachineSnapshot("snapshot", DateTimeOffset.UtcNow, "hash", "Windows 11", "24H2", 26100, "X64", "CPU", 8, 16UL * 1024 * 1024 * 1024,
        Array.Empty<GpuInfo>(), Array.Empty<DriveInfoSnapshot>(), false, true, "Balanced", Array.Empty<string>());
    var plan = await new PlanFactory().CreateAsync(snapshot, new[] { TestTweak("a") });
    Assert(plan.Sha256.Length == 64, "Plan hash is not SHA-256.");
    Assert(((ICollection<PlannedTweak>)plan.Tweaks).IsReadOnly, "Plan items are mutable.");
}

static async Task TestPlanSelection()
{
    var snapshot = TestSnapshot();
    var factory = new PlanFactory();
    var plan = await factory.CreateAsync(snapshot, new[] { TestTweak("a"), TestTweak("b") });
    var selected = factory.Select(plan, new HashSet<string>(new[] { "b" }, StringComparer.OrdinalIgnoreCase));
    Assert(selected.PlanId != plan.PlanId && selected.Sha256 != plan.Sha256, "Selection did not create a new immutable plan.");
    Assert(selected.Tweaks.Single(x => x.TweakId == "b").Selected, "Selected tweak was not retained.");
    Assert(!selected.Tweaks.Single(x => x.TweakId == "a").Selected, "Unselected tweak remained selected.");
}

static async Task TestExtendedScanner()
{
    var snapshot = await new WindowsSystemScanner().ScanAsync();
    Assert(snapshot.WindowsBuild > 0 && snapshot.TotalMemoryBytes > 0, "Base scanner data is unavailable.");
    Assert(snapshot.Insights is not null, "Extended insights were not attached.");
    Assert(snapshot.Insights!.TopProcesses.Count > 0, "No live processes were detected.");
    Assert(snapshot.Insights.NetworkAdapters.Count > 0, "No network adapters were detected.");
    Assert(snapshot.Insights.Performance is { CpuUtilizationPercent: >= 0 and <= 100 }, "Live CPU sampling is unavailable.");
}

static async Task TestCatalogProfiles()
{
    var catalog = TweakCatalog.CreateSafeTweaks();
    Assert(catalog.Count >= 14, "Expanded catalog is missing expected options.");
    Assert(catalog.Any(x => x.Metadata.Category == "Input" && !x.Metadata.RecommendedByDefault), "Input opt-in choices are missing.");
    var plan = await new PlanFactory().CreateAsync(TestSnapshot(), catalog);
    Assert(plan.Tweaks.Where(x => catalog.Single(t => t.Metadata.Id == x.TweakId).Metadata.RecommendedByDefault == false).All(x => !x.Selected), "Opt-in choices were selected automatically.");
}

static Task TestPresentMonTrust()
{
    var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "PresentMon", "2.6.0", "PresentMon.exe"));
    var trust = new BenchmarkEngine(path, Path.GetTempPath()).VerifyTool();
    Assert(trust.Trusted, trust.Message);
    return Task.CompletedTask;
}

static Task TestBenchmarkParser()
{
    var folder = Path.Combine(Path.GetTempPath(), "ProSyS.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    var csv = Path.Combine(folder, "frames.csv");
    try
    {
        var rows = new List<string> { "Application,ProcessID,MsBetweenPresents" };
        rows.AddRange(Enumerable.Range(0, 240).Select(i => $"game.exe,42,{(i % 17 == 0 ? 20.0 : 16.6).ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        File.WriteAllLines(csv, rows);
        var tool = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "PresentMon", "2.6.0", "PresentMon.exe"));
        var result = new BenchmarkEngine(tool, folder).ParseCsv(csv, TestSnapshot(), "game.exe");
        Assert(result.FrameCount == 240 && result.AverageFps > 55 && result.OnePercentLowFps > 0, "Frame statistics are invalid.");
        var comparison = BenchmarkEngine.Compare(result, result with { AverageFps = result.AverageFps * 1.03 });
        Assert(comparison.Comparability == BenchmarkComparability.High, "Identical fingerprints should be highly comparable.");
    }
    finally { Directory.Delete(folder, true); }
    return Task.CompletedTask;
}

static async Task TestGameProfileStore()
{
    var folder = Path.Combine(Path.GetTempPath(), "ProSyS.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new GameProfileStore(folder);
        var profile = new GameProfile(Guid.NewGuid(), "Test", "game.exe", null, OptimizationProfileKind.Safe, new[] { "a" }, true, true, DateTimeOffset.UtcNow, true);
        await store.SaveAsync(new[] { profile });
        var loaded = await store.LoadAsync();
        Assert(loaded.Count == 1 && loaded[0].Id == profile.Id && loaded[0].RestoreOnExit && loaded[0].CpuPriorityEnabled, "Profile data did not round-trip.");
    }
    finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}

static Task TestNetworkConnections()
{
    var rows = NetworkConnectionScanner.Scan();
    Assert(rows.All(x => x.ProcessId >= 0 && !string.IsNullOrWhiteSpace(x.ProcessName)), "Connection ownership is invalid.");
    return Task.CompletedTask;
}

static Task TestSignedUpdateManifest()
{
    using var rsa = RSA.Create(2048);
    var package = Encoding.UTF8.GetBytes("package");
    var unsigned = new SignedUpdateManifest("1.2.3", "https://updates.example.test/prosys.msix", Convert.ToHexString(SHA256.HashData(package)), "");
    var signature = rsa.SignData(Encoding.UTF8.GetBytes(SecureUpdateVerifier.CanonicalPayload(unsigned)), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    var signed = unsigned with { Signature = Convert.ToBase64String(signature) };
    var verified = SecureUpdateVerifier.ParseAndVerify(JsonSerializer.Serialize(signed), rsa.ExportSubjectPublicKeyInfoPem());
    Assert(verified.Version == "1.2.3", "Valid manifest was rejected.");
    try { _ = SecureUpdateVerifier.ParseAndVerify(JsonSerializer.Serialize(signed with { Version = "1.2.4" }), rsa.ExportSubjectPublicKeyInfoPem()); }
    catch (CryptographicException) { return Task.CompletedTask; }
    throw new InvalidOperationException("Tampered manifest was accepted.");
}

static async Task TestLiveMetrics()
{
    var sampler = new LiveMetricsSampler();
    _ = sampler.Sample();
    await Task.Delay(40);
    var value = sampler.Sample();
    Assert(value.CpuPercent is >= 0 and <= 100 && value.MemoryLoadPercent is >= 0 and <= 100 && value.NetworkBytesPerSecond >= 0, "Live metric range is invalid.");
}

static Task TestProductionCatalog()
{
    var catalog = TweakCatalog.CreateTweaks();
    Assert(catalog.Count > 100, $"Expected more than 100 capabilities, found {catalog.Count}.");
    Assert(catalog.Select(x => x.Metadata.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalog.Count, "Catalog identifiers are not unique.");
    Assert(catalog.Count(x => x.Metadata.RecommendedByDefault) <= 10, "Too many preferences are enabled by default.");
    Assert(catalog.All(x => !x.Metadata.RequiresAdministrator && x.Metadata.Risk.Reversibility == Reversibility.Easy), "Catalog contains a non-user-level capability.");
    return Task.CompletedTask;
}

static async Task TestTamperedPlan()
{
    var tweak = TestTweak("a");
    var plan = await new PlanFactory().CreateAsync(TestSnapshot(), new[] { tweak });
    var tampered = plan with { Sha256 = new string('0', 64) };
    var folder = Path.Combine(Path.GetTempPath(), "ProSyS.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        try { _ = await new OptimizationEngine(folder, new NullAudit()).ExecuteAsync(tampered, new[] { tweak }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("hash", StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException("Tampered plan was accepted.");
    }
    finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}

static async Task TestVerifiedAutomaticRollback()
{
    var first = new StatefulTweak("first", failApply: false, failRollbackVerification: true);
    var second = new StatefulTweak("second", failApply: true, failRollbackVerification: false);
    var plan = await new PlanFactory().CreateAsync(TestSnapshot(), new ITweak[] { first, second });
    var folder = Path.Combine(Path.GetTempPath(), "ProSyS.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        var result = await new OptimizationEngine(folder, new NullAudit()).ExecuteAsync(plan, new ITweak[] { first, second });
        Assert(result.State == OperationState.RecoveryRequired, "Failed rollback verification did not require recovery.");
        Assert(result.Results.Any(x => x.TweakId == "first" && !x.Success && x.Message.Contains("Rollback")), "Rollback verification failure was not recorded.");
    }
    finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}

static async Task TestHtmlReport()
{
    var folder = Path.Combine(Path.GetTempPath(), "ProSyS.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var plan = await new PlanFactory().CreateAsync(TestSnapshot(), new[] { TestTweak("<unsafe>") });
        var path = Path.Combine(folder, "report.html");
        await ReportExporter.ExportHtmlAsync(path, TestSnapshot(), plan);
        var html = await File.ReadAllTextAsync(path);
        Assert(html.Contains("<!doctype html>") && html.Contains("&lt;unsafe&gt;") && !html.Contains("<unsafe>"), "HTML report is missing or unescaped.");
    }
    finally { Directory.Delete(folder, true); }
}

static Task TestPersianCatalogCoverage()
{
    var catalog = TweakCatalog.CreateTweaks();
    var missingTitles = new List<string>();
    foreach (var tweak in catalog)
    {
        var title = UiLocalization.Translate(tweak.Metadata.Name, true);
        var description = UiLocalization.Translate(tweak.Metadata.Description, true);
        var category = UiLocalization.Translate(tweak.Metadata.Category, true);
        if (!title.Any(ch => ch >= '\u0600' && ch <= '\u06ff')) missingTitles.Add($"{tweak.Metadata.Id}={title}");
        Assert(!description.StartsWith("Configure the current-user", StringComparison.Ordinal), $"Persian description missing for {tweak.Metadata.Id}.");
        Assert(category != tweak.Metadata.Category, $"Persian category missing for {tweak.Metadata.Category}.");
    }
    Assert(missingTitles.Count == 0, "Persian titles missing: " + string.Join(" | ", missingTitles));
    return Task.CompletedTask;
}

static Task TestCpuPrioritySession()
{
    using var process = Process.GetCurrentProcess();
    var priority = process.PriorityClass;
    var boost = process.PriorityBoostEnabled;
    var session = new ProcessCpuOptimizer().Apply(process);
    Assert(process.PriorityClass == ProcessPriorityClass.High && process.PriorityBoostEnabled, "CPU priority mode was not applied.");
    Assert(session.AppliedMode.Contains("never", StringComparison.OrdinalIgnoreCase), "Realtime safety statement is missing.");
    Assert(session.TryRestore(), "CPU process policy could not be restored.");
    process.Refresh();
    Assert(process.PriorityClass == priority && process.PriorityBoostEnabled == boost, "Original CPU process policy was not restored exactly.");
    return Task.CompletedTask;
}

static MachineSnapshot TestSnapshot() => new("snapshot", DateTimeOffset.UtcNow, "hash", "Windows 11", "24H2", 26100, "X64", "CPU", 8, 16UL * 1024 * 1024 * 1024,
    Array.Empty<GpuInfo>(), Array.Empty<DriveInfoSnapshot>(), false, true, "Balanced", Array.Empty<string>());

static FakeTweak TestTweak(string id, string[]? after = null) => new(Metadata(id, after));
static TweakMetadata Metadata(string id, string[]? after = null) => new(id, 1, id, id, "Test", "Test", RiskProfile.Safe(), BenefitLevel.Low,
    EvidenceType.GenerallySupportedBehavior, false, false, false, false, false, new[] { "Windows 11" }, Array.Empty<string>(), Array.Empty<string>(),
    after ?? Array.Empty<string>(), "Restore", "2026-09-23");
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class FakeTweak(TweakMetadata metadata) : ITweak
{
    public TweakMetadata Metadata { get; } = metadata;
    public Task ApplyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<TweakBackup> BackupAsync(CancellationToken cancellationToken = default) => Task.FromResult(new TweakBackup(Metadata.Id, false, null, "None", DateTimeOffset.UtcNow));
    public Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DetectionResult(DetectionStatus.Enabled, 1, "Fake boundary", Confidence.Verified, DateTimeOffset.UtcNow));
    public Task<CompatibilityResult> EvaluateCompatibilityAsync(MachineSnapshot machine, CancellationToken cancellationToken = default) => Task.FromResult(new CompatibilityResult(CompatibilityStatus.Compatible, "Test"));
    public Task RollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<DetectionResult> VerifyAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DetectionResult(DetectionStatus.Disabled, 0, "Fake boundary", Confidence.Verified, DateTimeOffset.UtcNow));
    public Task<DetectionResult> VerifyRollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default) => Task.FromResult(new DetectionResult(DetectionStatus.Unknown, null, "Fake boundary", Confidence.Verified, DateTimeOffset.UtcNow));
}

sealed class NullAudit : IAuditLog { public Task WriteAsync(object entry, CancellationToken cancellationToken = default) => Task.CompletedTask; }

sealed class StatefulTweak(string id, bool failApply, bool failRollbackVerification) : ITweak
{
    private int _value = 1;
    public TweakMetadata Metadata { get; } = MetadataFactory(id);
    private static TweakMetadata MetadataFactory(string value) => new(value, 1, value, value, "Test", "Test", RiskProfile.Safe(), BenefitLevel.Low,
        EvidenceType.ControlledBenchmark, false, false, false, false, false, new[] { "Windows 11" }, Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), "Restore", "2026-09-25");
    public Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DetectionResult(_value == 0 ? DetectionStatus.Disabled : DetectionStatus.Enabled, _value, "test", Confidence.Verified, DateTimeOffset.UtcNow));
    public Task<CompatibilityResult> EvaluateCompatibilityAsync(MachineSnapshot machine, CancellationToken cancellationToken = default) => Task.FromResult(new CompatibilityResult(CompatibilityStatus.Compatible, "test"));
    public Task<TweakBackup> BackupAsync(CancellationToken cancellationToken = default) => Task.FromResult(new TweakBackup(id, true, 1, "DWord", DateTimeOffset.UtcNow));
    public Task ApplyAsync(CancellationToken cancellationToken = default) { if (failApply) throw new InvalidOperationException("Injected apply failure"); _value = 0; return Task.CompletedTask; }
    public Task<DetectionResult> VerifyAsync(CancellationToken cancellationToken = default) => DetectAsync(cancellationToken);
    public Task RollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default) { _value = failRollbackVerification ? 99 : 1; return Task.CompletedTask; }
    public Task<DetectionResult> VerifyRollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default) => Task.FromResult(new DetectionResult(DetectionStatus.Enabled, _value, "test", Confidence.Verified, DateTimeOffset.UtcNow));
}
