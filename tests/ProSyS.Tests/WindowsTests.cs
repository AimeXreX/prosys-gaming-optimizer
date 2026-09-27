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
    public void CatalogIdentifiersAreUniqueAndUserLevel()
    {
        var catalog = TweakCatalog.CreateTweaks();
        Assert.Equal(catalog.Count, catalog.Select(x => x.Metadata.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.InRange(catalog.Count(x => x.Metadata.RecommendedByDefault), 1, 10);
        Assert.All(catalog, x => Assert.True(!x.Metadata.RequiresAdministrator && x.Metadata.Risk.Reversibility == Reversibility.Easy));
    }

    [Fact]
    public void CatalogExcludesInternalAndMistypedValues()
    {
        var ids = TweakCatalog.CreateTweaks().Select(x => x.Metadata.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("gaming.capture.maximumrecordlength", ids);
        Assert.DoesNotContain("shell.advanced.reindexedprofile", ids);
        Assert.DoesNotContain("dwm.forceeffectmode", ids);
    }

    [Fact]
    public void ControlPanelValuesDeclareSignOut()
    {
        var catalog = TweakCatalog.CreateTweaks();
        Assert.All(catalog.Where(x => x.Metadata.Category is "Input" or "Desktop Responsiveness" or "Accessibility & Input"), x => Assert.True(x.Metadata.RequiresSignOut, x.Metadata.Id));
        Assert.All(catalog.Where(x => x.Metadata.Category == "Gaming & Capture"), x => Assert.False(x.Metadata.RequiresSignOut, x.Metadata.Id));
    }

    [Fact]
    public void SafeProfileOnlyContainsDefaultRecommendations()
    {
        var catalog = TweakCatalog.CreateTweaks();
        var safe = catalog.Where(x => new RiskEngine().AllowedInSafeProfile(x.Metadata)).ToList();
        Assert.NotEmpty(safe);
        Assert.All(safe, x => Assert.True(x.Metadata.RecommendedByDefault));
        Assert.True(safe.Count < catalog.Count);
    }

    [Fact]
    public void EveryCapabilityHasPersianTitleDescriptionAndCategory()
    {
        foreach (var tweak in TweakCatalog.CreateTweaks())
        {
            Assert.Contains(UiLocalization.Translate(tweak.Metadata.Name, true), ch => ch is >= '؀' and <= 'ۿ');
            Assert.False(UiLocalization.Translate(tweak.Metadata.Description, true).StartsWith("Configure the current-user", StringComparison.Ordinal), tweak.Metadata.Id);
            Assert.NotEqual(tweak.Metadata.Category, UiLocalization.Translate(tweak.Metadata.Category, true));
        }
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
    public void SignedManifestsRejectTamperingAndDowngrades()
    {
        using var rsa = RSA.Create(2048);
        var unsigned = new SignedUpdateManifest("1.2.3", "https://updates.example.test/prosys.msix", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("package"))), "");
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(SecureUpdateVerifier.CanonicalPayload(unsigned)), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var signed = unsigned with { Signature = Convert.ToBase64String(signature) };
        var key = rsa.ExportSubjectPublicKeyInfoPem();

        Assert.Equal("1.2.3", SecureUpdateVerifier.ParseAndVerify(JsonSerializer.Serialize(signed), key, new Version(1, 2, 0)).Version);
        Assert.Throws<CryptographicException>(() => SecureUpdateVerifier.ParseAndVerify(JsonSerializer.Serialize(signed with { Version = "1.2.4" }), key));
        Assert.Throws<InvalidDataException>(() => SecureUpdateVerifier.ParseAndVerify(JsonSerializer.Serialize(signed), key, new Version(1, 2, 3)));
        Assert.Throws<InvalidDataException>(() => SecureUpdateVerifier.ParseAndVerify("{}", key));
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
    public void CpuPriorityUsesHighAndRestoresExactly()
    {
        using var process = Process.GetCurrentProcess();
        var priority = process.PriorityClass;
        var boost = process.PriorityBoostEnabled;
        var session = new ProcessCpuOptimizer().Apply(process);
        Assert.Equal(ProcessPriorityClass.High, process.PriorityClass);
        Assert.Contains("never", session.AppliedMode, StringComparison.OrdinalIgnoreCase);
        Assert.True(session.TryRestore());
        process.Refresh();
        Assert.Equal(priority, process.PriorityClass);
        Assert.Equal(boost, process.PriorityBoostEnabled);
    }
}
