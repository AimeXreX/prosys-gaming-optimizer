using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using ProSyS.App;
using ProSyS.Core;
using ProSyS.Windows;
using Xunit;

namespace ProSyS.Tests;

public class RegistryTweakTests
{
    private const string TestKey = @"Software\Microsoft\GameBar\ProSySOptimizer.Tests";

    [Theory]
    [InlineData("DWord")]
    [InlineData("QWord")]
    [InlineData("Binary")]
    [InlineData("MultiString")]
    [InlineData("ExpandString")]
    public void BackupValuesConvertBackFromJson(string kind)
    {
        object original = kind switch
        {
            "DWord" => 37, "QWord" => 72_000_000_000L, "Binary" => new byte[] { 9, 8, 7 },
            "MultiString" => new[] { "a", "b" }, _ => "%SystemRoot%\\x"
        };
        var backup = new TweakBackup("id", true, original, kind, DateTimeOffset.UtcNow);
        var fromDisk = JsonSerializer.Deserialize<TweakBackup>(JsonSerializer.Serialize(backup))!;
        var restored = RegistryTweak.ConvertBackupValue(fromDisk);
        Assert.Equal(ValueText.Canonical(original, kind), ValueText.Canonical(restored, kind));
    }

    [Fact]
    public void DwordAboveIntMaxKeepsItsBitPattern()
    {
        var backup = JsonSerializer.Deserialize<TweakBackup>("""{"TweakId":"id","ValueExisted":true,"OriginalValue":4294967295,"ValueKind":"DWord","CapturedAt":"2026-01-01T00:00:00+00:00"}""")!;
        Assert.Equal(-1, RegistryTweak.ConvertBackupValue(backup));
    }

    [WindowsFact]
    public async Task RollbackRemovesKeysCreatedByApply()
    {
        const string created = TestKey + @"\Created\Deeper";
        Registry.CurrentUser.DeleteSubKeyTree(TestKey, false);
        using (Registry.CurrentUser.CreateSubKey(TestKey, true)) { }
        var tweak = new RegistryTweak(TestData.Metadata("test.created"), created, "Value", 1);
        try
        {
            var backup = await tweak.BackupAsync();
            Assert.Equal(TestKey + @"\Created", backup.MissingKeyPath);
            await tweak.ApplyAsync();
            await tweak.RollbackAsync(backup);
            using var parent = Registry.CurrentUser.OpenSubKey(TestKey)!;
            Assert.Empty(parent.GetSubKeyNames());
            Assert.Equal(DetectionStatus.Absent, (await tweak.VerifyRollbackAsync(backup)).Status);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(TestKey, false); }
    }

    [WindowsFact]
    public async Task RollbackVerificationDetectsTypeChange()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey, true)) key.SetValue("Typed", "5", RegistryValueKind.String);
        var tweak = new RegistryTweak(TestData.Metadata("test.typed"), TestKey, "Typed", 0);
        try
        {
            var backup = await tweak.BackupAsync();
            using (var key = Registry.CurrentUser.OpenSubKey(TestKey, true)!) key.SetValue("Typed", 5, RegistryValueKind.DWord);
            Assert.Equal(DetectionStatus.NonCompliant, (await tweak.VerifyRollbackAsync(backup)).Status);
            await tweak.RollbackAsync(backup);
            Assert.Equal(DetectionStatus.Present, (await tweak.VerifyRollbackAsync(backup)).Status);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(TestKey, false); }
    }

    [WindowsFact]
    public async Task ApplyVerifyRollbackIsIdempotent()
    {
        const string name = "LifecycleValue";
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey, true)) key.SetValue(name, 37, RegistryValueKind.DWord);
        var tweak = new RegistryTweak(TestData.Metadata("test.registry"), TestKey, name, 0);
        try
        {
            var backup = await tweak.BackupAsync();
            await tweak.ApplyAsync();
            Assert.Equal(DetectionStatus.Compliant, (await tweak.VerifyAsync()).Status);
            await tweak.RollbackAsync(backup);
            Assert.Equal("37", (await tweak.VerifyRollbackAsync(backup)).Value?.ToString());
            await tweak.RollbackAsync(backup);
            Assert.Equal("37", (await tweak.VerifyRollbackAsync(backup)).Value?.ToString());
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(TestKey, false); }
    }

    [WindowsFact]
    public async Task BinaryAndMultiStringValuesRestoreFromADiskBackup()
    {
        using var folder = new TempFolder();
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey, true))
        {
            key.SetValue("Bin", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
            key.SetValue("Multi", new[] { "x", "y" }, RegistryValueKind.MultiString);
        }
        var tweaks = new ITweak[]
        {
            new RegistryTweak(TestData.Metadata("test.bin"), TestKey, "Bin", 0),
            new RegistryTweak(TestData.Metadata("test.multi"), TestKey, "Multi", 0)
        };
        try
        {
            var engine = new OptimizationEngine(folder.Path, new NullAudit());
            var applied = await engine.ExecuteAsync(await new PlanFactory().CreateAsync(TestData.Snapshot(), tweaks), tweaks);
            Assert.Equal(OperationState.Completed, applied.State);
            var restored = await engine.RollbackAsync(applied.BackupDirectory, tweaks);
            Assert.Equal(OperationState.RolledBack, restored.State);
            using var key = Registry.CurrentUser.OpenSubKey(TestKey)!;
            Assert.Equal(RegistryValueKind.Binary, key.GetValueKind("Bin"));
            Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])key.GetValue("Bin")!);
            Assert.Equal(new[] { "x", "y" }, (string[])key.GetValue("Multi")!);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(TestKey, false); }
    }
}

public class CatalogTests
{
    [Fact]
    public void CatalogIsSmallCuratedAndReferenced()
    {
        var catalog = TweakCatalog.CreateTweaks();
        Assert.Equal(18, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(x => x.Metadata.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(catalog, x => Assert.False(string.IsNullOrWhiteSpace(x.Metadata.Reference), x.Metadata.Id));
        Assert.All(catalog, x => Assert.True(!x.Metadata.RequiresAdministrator && x.Metadata.Risk.Reversibility == Reversibility.Easy));
        Assert.Contains(catalog, x => x.Metadata.Category == "Preferences");
    }

    [Fact]
    public void OnlyGameModeIsSelectedByDefault()
    {
        var defaults = TweakCatalog.CreateTweaks().Where(x => x.Metadata.RecommendedByDefault).Select(x => x.Metadata.Id);
        Assert.Equal(new[] { "gaming.gamebar.autogamemodeenabled" }, defaults);
    }

    [Fact]
    public void CatalogExcludesFullscreenDwmAndInternalValues()
    {
        var ids = TweakCatalog.CreateTweaks().Select(x => x.Metadata.Id).ToList();
        Assert.DoesNotContain(ids, x => x.StartsWith("gaming.config.", StringComparison.Ordinal)); // GameDVR_FSE* / GameConfigStore
        Assert.DoesNotContain(ids, x => x.StartsWith("dwm.", StringComparison.Ordinal));
        Assert.DoesNotContain("gaming.capture.maximumrecordlength", ids);
        Assert.DoesNotContain("shell.advanced.reindexedprofile", ids);
    }

    [Fact]
    public void RollbackCatalogStillCoversEveryEarlierCapability()
    {
        var rollback = TweakCatalog.CreateRollbackCatalog();
        Assert.Equal(180 + 3, rollback.Count); // every 1.0 capability plus the 1.3.0 additions that were not in 1.0
        Assert.Equal(rollback.Count, rollback.Select(x => x.Metadata.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(rollback, x => x.Metadata.Id == "gaming.capture.maximumrecordlength");
        Assert.All(TweakCatalog.CreateTweaks(), offered => Assert.Contains(rollback, x => x.Metadata.Id == offered.Metadata.Id));
    }

    [Fact]
    public void ControlPanelValuesDeclareSignOut()
    {
        var catalog = TweakCatalog.CreateTweaks();
        Assert.All(catalog.Where(x => x.Metadata.Category is "Input" or "Accessibility & Input"), x => Assert.True(x.Metadata.RequiresSignOut, x.Metadata.Id));
        Assert.All(catalog.Where(x => x.Metadata.Category == "Gaming & Capture"), x => Assert.False(x.Metadata.RequiresSignOut, x.Metadata.Id));
    }

    [Fact]
    public void SafeProfileIsGameModeOnly()
    {
        var safe = TweakCatalog.CreateTweaks().Where(x => new RiskEngine().AllowedInSafeProfile(x.Metadata)).Select(x => x.Metadata.Id);
        Assert.Equal(new[] { "gaming.gamebar.autogamemodeenabled" }, safe);
    }

    [Fact]
    public void EveryCapabilityHasPersianTitleDescriptionAndCategory()
    {
        foreach (var tweak in TweakCatalog.CreateTweaks())
        {
            Assert.Contains(UiLocalization.Translate(tweak.Metadata.Name, true), ch => ch is >= '\u0600' and <= '\u06ff');
            Assert.Contains(UiLocalization.Translate(tweak.Metadata.Description, true), ch => ch is >= '\u0600' and <= '\u06ff');
            Assert.NotEqual(tweak.Metadata.Category, UiLocalization.Translate(tweak.Metadata.Category, true));
        }
    }

    [Fact]
    public void FpsCapabilitiesAreOfferedAsOptIn()
    {
        var ids = TweakCatalog.CreateTweaks().Where(x => !x.Metadata.RecommendedByDefault).Select(x => x.Metadata.Id).ToHashSet();
        Assert.Contains("gaming.graphics.windowed-optimizations", ids);
        Assert.Contains("power.plan.high-performance", ids);
        Assert.Contains("display.refresh.maximum", ids);
    }

    [Fact]
    public void PerGameGpuPreferenceRoundTripsThroughItsId()
    {
        var exe = Path.Combine(Path.GetTempPath(), "Games", "game.exe");
        var tweak = TweakCatalog.CreateGpuPreferenceTweak(exe);
        Assert.StartsWith(TweakCatalog.GpuPreferencePrefix, tweak.Metadata.Id);
        Assert.Equal(tweak.Metadata.Id, TweakCatalog.ResolveDynamic(tweak.Metadata.Id)!.Metadata.Id);
        Assert.Null(TweakCatalog.ResolveDynamic("unknown.id"));
    }

    [Fact]
    public async Task GpuPreferenceNeedsTwoGpus()
    {
        var tweak = TweakCatalog.CreateGpuPreferenceTweak(Path.Combine(Path.GetTempPath(), "game.exe"));
        Assert.Equal(CompatibilityStatus.Unsupported, (await tweak.EvaluateCompatibilityAsync(TestData.Snapshot())).Status);
        var hybrid = TestData.Snapshot() with { Gpus = new[] { new GpuInfo("Intel UHD", "1", "test"), new GpuInfo("NVIDIA RTX", "2", "test") } };
        Assert.Equal(CompatibilityStatus.Compatible, (await tweak.EvaluateCompatibilityAsync(hybrid)).Status);
    }

    [Theory]
    [InlineData(null, "SwapEffectUpgradeEnable=1;")]
    [InlineData("VRROptimizeEnable=0;", "VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;")]
    [InlineData("SwapEffectUpgradeEnable=0;AutoHDREnable=1;", "AutoHDREnable=1;SwapEffectUpgradeEnable=1;")]
    public void DirectXTokensAreMergedWithoutLosingOthers(string? before, string expected)
    {
        var after = RegistryTweak.WriteToken(before, "SwapEffectUpgradeEnable", "1");
        Assert.Equal(expected, after);
        Assert.Equal("1", RegistryTweak.ReadToken(after, "SwapEffectUpgradeEnable"));
    }

    [Fact]
    public void RefreshRateStateIsCanonical()
    {
        var text = DisplayRefreshTweak.Format(new Dictionary<string, int> { [@"\\.\DISPLAY2"] = 144, [@"\\.\DISPLAY1"] = 60 });
        Assert.Equal(@"\\.\DISPLAY1=60;\\.\DISPLAY2=144;", text);
        Assert.Equal(144, DisplayRefreshTweak.Parse(text)[@"\\.\display2"]);
    }

    [WindowsFact]
    public async Task PowerPlanAndDisplayDetectionWork()
    {
        var catalog = TweakCatalog.CreateTweaks();
        foreach (var tweak in catalog.Where(x => x is PowerPlanTweak or DisplayRefreshTweak))
            Assert.NotEqual(DetectionStatus.DetectionFailed, (await tweak.DetectAsync()).Status);
    }

    [WindowsFact]
    public async Task GpuPreferenceAppliesAndRestoresExactly()
    {
        using var folder = new TempFolder();
        var tweak = TweakCatalog.CreateGpuPreferenceTweak(Path.Combine(folder.Path, "prosys-test-game.exe"));
        var hybrid = TestData.Snapshot() with { Gpus = new[] { new GpuInfo("A", "1", "t"), new GpuInfo("B", "2", "t") } };
        var engine = new OptimizationEngine(folder.Path, new NullAudit());
        var applied = await engine.ExecuteAsync(await new PlanFactory().CreateAsync(hybrid, new[] { tweak }), new[] { tweak });
        Assert.Equal(OperationState.Completed, applied.State);
        var restored = await engine.RollbackAsync(applied.BackupDirectory, Array.Empty<ITweak>(), default, TweakCatalog.ResolveDynamic);
        Assert.Equal(OperationState.RolledBack, restored.State);
        Assert.Equal(DetectionStatus.NonCompliant, (await tweak.DetectAsync()).Status);
    }

    [WindowsFact]
    public async Task OptInChoicesAreNeverSelectedAutomatically()
    {
        var catalog = TweakCatalog.CreateSafeTweaks();
        var plan = await new PlanFactory().CreateAsync(TestData.Snapshot(), catalog);
        var optIn = catalog.Where(x => !x.Metadata.RecommendedByDefault).Select(x => x.Metadata.Id).ToHashSet();
        Assert.All(plan.Tweaks.Where(x => optIn.Contains(x.TweakId)), x => Assert.False(x.Selected));
    }
}

public class StorageAndUpdateTests
{
    [Fact]
    public async Task GameProfilesRoundTrip()
    {
        using var folder = new TempFolder();
        var store = new GameProfileStore(folder.Path);
        var profile = new GameProfile(Guid.NewGuid(), "Test", "game.exe", null, OptimizationProfileKind.Safe, new[] { "a" }, true, true, DateTimeOffset.UtcNow, true);
        await store.SaveAsync(new[] { profile });
        var loaded = Assert.Single(await store.LoadAsync());
        Assert.Equal(profile.Id, loaded.Id);
        Assert.True(loaded.RestoreOnExit && loaded.CpuPriorityEnabled);
    }

    [Fact]
    public async Task UnreadableProfilesAreKeptInsteadOfOverwritten()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "game-profiles.json");
        await File.WriteAllTextAsync(path, "{ not json");
        var store = new GameProfileStore(folder.Path);
        Assert.Empty(await store.LoadAsync());
        Assert.NotNull(store.QuarantinedFile);
        Assert.Equal("{ not json", await File.ReadAllTextAsync(store.QuarantinedFile!));
        await store.SaveAsync(Array.Empty<GameProfile>());
        Assert.True(File.Exists(store.QuarantinedFile));
    }

    [Fact]
    public void SignedManifestsRejectTamperingDowngradesAndExpiry()
    {
        using var rsa = RSA.Create(2048);
        var now = new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);
        var key = rsa.ExportSubjectPublicKeyInfoPem();
        SignedUpdateManifest Sign(SignedUpdateManifest manifest) => manifest with
        {
            Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(SecureUpdateVerifier.CanonicalPayload(manifest)), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
        };
        var valid = Sign(new SignedUpdateManifest(2, "1.2.3", "https://updates.example.test/prosys.exe", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("package"))), now.AddDays(30), ""));
        string Json(SignedUpdateManifest manifest) => JsonSerializer.Serialize(manifest);

        Assert.Equal("1.2.3", SecureUpdateVerifier.ParseAndVerify(Json(valid), key, new Version(1, 2, 0), now).Version);
        Assert.Throws<CryptographicException>(() => SecureUpdateVerifier.ParseAndVerify(Json(valid with { Version = "1.2.4" }), key, now: now));
        Assert.Throws<CryptographicException>(() => SecureUpdateVerifier.ParseAndVerify(Json(valid with { ExpiresAt = now.AddDays(60) }), key, now: now));
        Assert.Throws<InvalidDataException>(() => SecureUpdateVerifier.ParseAndVerify(Json(valid), key, new Version(1, 2, 3), now));
        Assert.Throws<InvalidDataException>(() => SecureUpdateVerifier.ParseAndVerify(Json(valid), key, now: now.AddDays(31)));
        Assert.Throws<InvalidDataException>(() => SecureUpdateVerifier.ParseAndVerify(Json(Sign(valid with { ExpiresAt = now.AddDays(91) })), key, now: now));
        Assert.Throws<InvalidDataException>(() => SecureUpdateVerifier.ParseAndVerify(Json(Sign(valid with { SchemaVersion = 1 })), key, now: now));
        Assert.Throws<InvalidDataException>(() => SecureUpdateVerifier.ParseAndVerify("{}", key, now: now));
    }
}

public class BenchmarkTests
{
    [Fact]
    public void ParserCalculatesFrameStatistics()
    {
        using var folder = new TempFolder();
        var csv = Path.Combine(folder.Path, "frames.csv");
        var rows = new List<string> { "Application,ProcessID,MsBetweenPresents" };
        rows.AddRange(Enumerable.Range(0, 240).Select(i => $"game.exe,42,{(i % 17 == 0 ? 20.0 : 16.6).ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        File.WriteAllLines(csv, rows);
        var result = new BenchmarkEngine(TestData.PresentMonPath, folder.Path).ParseCsv(csv, TestData.Snapshot(), "game.exe");
        Assert.Equal(240, result.FrameCount);
        Assert.True(result.AverageFps > 55 && result.OnePercentLowFps > 0);
        Assert.Equal(BenchmarkComparability.High, BenchmarkEngine.Compare(result, result with { AverageFps = result.AverageFps * 1.03 }).Comparability);
    }

    private static BenchmarkSummary Run(double averageFps, bool baseline, int duration = 30, string game = "game.exe") =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, "PresentMon 2.6.0", game, 1000, averageFps, averageFps * 0.7, averageFps * 0.5, 1000 / averageFps, 1500 / averageFps, 1,
            new BenchmarkFingerprint(game, "26100", "32.0", "Balanced", "snapshot", duration), "run.csv", baseline);

    [Fact]
    public void SetComparisonNeedsThreeRunsPerSide()
    {
        var result = BenchmarkEngine.CompareSets(new[] { Run(100, true), Run(101, true) }, new[] { Run(120, false), Run(121, false), Run(119, false) });
        Assert.Equal(BenchmarkVerdict.InsufficientRuns, result.Verdict);
    }

    [Fact]
    public void SetComparisonDetectsImprovementRegressionAndNoise()
    {
        var baseline = new[] { Run(100, true), Run(101, true), Run(99, true) };
        Assert.Equal(BenchmarkVerdict.Improvement, BenchmarkEngine.CompareSets(baseline, new[] { Run(110, false), Run(111, false), Run(109, false) }).Verdict);
        var regression = BenchmarkEngine.CompareSets(baseline, new[] { Run(90, false), Run(91, false), Run(89, false) });
        Assert.Equal(BenchmarkVerdict.Regression, regression.Verdict);
        Assert.True(regression.AverageFps!.CiHighPercent < 0);
        Assert.Equal(BenchmarkVerdict.NoSignificantChange, BenchmarkEngine.CompareSets(new[] { Run(90, true), Run(110, true), Run(100, true) }, new[] { Run(95, false), Run(112, false), Run(101, false) }).Verdict);
    }

    [Fact]
    public void SetComparisonRejectsDifferentDurations()
    {
        var result = BenchmarkEngine.CompareSets(new[] { Run(100, true), Run(101, true), Run(99, true) }, new[] { Run(110, false, 60), Run(111, false, 60), Run(109, false, 60) });
        Assert.Equal(BenchmarkVerdict.NotComparable, result.Verdict);
    }

    [PresentMonFact]
    public void PresentMonBinaryIsPinnedAndSigned()
    {
        var trust = new BenchmarkEngine(TestData.PresentMonPath, Path.GetTempPath()).VerifyTool();
        Assert.True(trust.Trusted, trust.Message);
    }

    [Fact]
    public async Task HtmlReportEscapesMarkup()
    {
        using var folder = new TempFolder();
        var plan = await new PlanFactory().CreateAsync(TestData.Snapshot(), new[] { new FakeTweak(TestData.Metadata("<unsafe>")) });
        var path = Path.Combine(folder.Path, "report.html");
        await ReportExporter.ExportHtmlAsync(path, TestData.Snapshot(), plan);
        var html = await File.ReadAllTextAsync(path);
        Assert.Contains("<!doctype html>", html);
        Assert.Contains("&lt;unsafe&gt;", html);
        Assert.DoesNotContain("<unsafe>", html);
    }
}

public class AdvisorTests
{
    private static MachineSnapshot Machine(bool laptop = false, int gpus = 1, long freeGb = 200) => TestData.Snapshot() with
    {
        IsLaptop = laptop,
        Gpus = Enumerable.Range(0, gpus).Select(i => new GpuInfo($"GPU {i}", "1", "t")).ToArray(),
        Drives = new[] { new DriveInfoSnapshot("C:\\", "NTFS", 500L * 1073741824, freeGb * 1073741824, true) }
    };

    [Fact]
    public void HealthyDesktopHasNoAdvice() =>
        Assert.Empty(PerformanceAdvisor.Evaluate(Machine(), new[] { new PerformanceAdvisor.MemoryModule(34, 6000, 16UL << 30), new PerformanceAdvisor.MemoryModule(34, 6000, 16UL << 30) }, false, 2, null));

    [Fact]
    public void CommonLimitersAreReportedMostSevereFirst()
    {
        var refresh = new DetectionResult(DetectionStatus.NonCompliant, @"\\.\DISPLAY1=60;", "t", Confidence.Verified, DateTimeOffset.UtcNow, "DISPLAY1: 60 Hz → 144 Hz available");
        var advice = PerformanceAdvisor.Evaluate(Machine(laptop: true, gpus: 2, freeGb: 5), new[] { new PerformanceAdvisor.MemoryModule(26, 2400, 8UL << 30) }, true, 1, refresh);
        var ids = advice.Select(x => x.Id).ToList();
        Assert.Equal(new[] { "on-battery", "refresh-rate" }, ids.Take(2).OrderBy(x => x));
        Assert.Contains("hybrid-gpu", ids);
        Assert.Contains("single-channel", ids);
        Assert.Contains("gpu-scheduling", ids);
        Assert.Contains("disk-space", ids);
        Assert.DoesNotContain("memory-base-speed", ids); // laptops rarely expose XMP; not advised there
        Assert.All(advice, x => Assert.False(string.IsNullOrWhiteSpace(x.TitleFa)));
    }

    [Fact]
    public void DesktopMemoryAtJedecSpeedIsFlagged() =>
        Assert.Contains(PerformanceAdvisor.Evaluate(Machine(), new[] { new PerformanceAdvisor.MemoryModule(34, 4800, 16UL << 30), new PerformanceAdvisor.MemoryModule(34, 4800, 16UL << 30) }, false, null, null),
            x => x.Id == "memory-base-speed");
}

public class SystemIntegrationTests
{
    [Theory]
    [InlineData("10.0.0.5", 0x5000u, "10.0.0.5:80")]
    [InlineData("::1", 0xBB01u, "[::1]:443")]
    public void EndpointsUseNetworkByteOrderPorts(string address, uint rawPort, string expected) =>
        Assert.Equal(expected, NetworkConnectionScanner.Endpoint(IPAddress.Parse(address), rawPort));

    [WindowsFact]
    public async Task ScannerReturnsExtendedInsights()
    {
        var snapshot = await new WindowsSystemScanner().ScanAsync();
        Assert.True(snapshot.WindowsBuild > 0 && snapshot.TotalMemoryBytes > 0);
        Assert.NotNull(snapshot.Insights);
        Assert.NotEmpty(snapshot.Insights!.TopProcesses);
        Assert.True(snapshot.Insights.Performance is { CpuUtilizationPercent: >= 0 and <= 100 });
    }

    [WindowsFact]
    public void NetworkConnectionsKeepOwningProcessIds() =>
        Assert.All(NetworkConnectionScanner.Scan(), x => Assert.True(x.ProcessId >= 0 && !string.IsNullOrWhiteSpace(x.ProcessName)));

    [WindowsFact]
    public async Task LiveMetricsStayInRange()
    {
        var sampler = new LiveMetricsSampler();
        _ = sampler.Sample();
        await Task.Delay(40);
        var value = sampler.Sample();
        Assert.InRange(value.CpuPercent, 0, 100);
        Assert.InRange(value.MemoryLoadPercent, 0u, 100u);
        Assert.True(value.NetworkBytesPerSecond >= 0);
    }

    [WindowsFact]
    public void CpuPriorityUsesAboveNormalAndRestoresExactly()
    {
        using var process = Process.GetCurrentProcess();
        var priority = process.PriorityClass;
        var boost = process.PriorityBoostEnabled;
        var session = new ProcessCpuOptimizer().Apply(process);
        process.Refresh();
        Assert.Equal(ProcessPriorityClass.AboveNormal, process.PriorityClass);
        Assert.Equal(boost, process.PriorityBoostEnabled);
        Assert.Contains("never", session.AppliedMode, StringComparison.OrdinalIgnoreCase);
        Assert.True(session.TryRestore());
        process.Refresh();
        Assert.Equal(priority, process.PriorityClass);
        Assert.Equal(boost, process.PriorityBoostEnabled);
    }
}
