using System.Diagnostics;
using System.Text.Json;
using ProSyS.Core;
using ProSyS.Windows;
using ProSyS.Windows.GameSession;
using Xunit;

namespace ProSyS.Tests;

public class GameSessionTests
{
    [Fact]
    public void PerformanceCoresAreSelectedOnlyOnHybridCpus()
    {
        var hybrid = Enumerable.Range(0, 16).Select(i => new CpuSetInfo((uint)(256 + i), (byte)(i / 2), (byte)(i < 8 ? 1 : 0), false)).ToArray();
        var selected = PerformanceCoreLever.SelectPerformanceCpuSets(hybrid);
        Assert.Equal(8, selected.Length);
        Assert.All(selected, id => Assert.True(id < 264));
        Assert.Empty(PerformanceCoreLever.SelectPerformanceCpuSets(hybrid.Select(x => x with { EfficiencyClass = 0 }).ToArray()));
        Assert.Empty(PerformanceCoreLever.SelectPerformanceCpuSets(hybrid.Take(2).Concat(hybrid.Skip(8).Take(4)).ToArray()));
    }

    [Theory]
    [InlineData("chrome", 100, 200, 1, 1, true)]
    [InlineData("chrome", 200, 200, 1, 1, false)]
    [InlineData("chrome", 100, 200, 1, 2, false)]
    [InlineData("Discord", 100, 200, 1, 1, false)]
    [InlineData("obs64", 100, 200, 1, 1, false)]
    [InlineData("explorer", 100, 200, 1, 1, false)]
    [InlineData("MsMpEng", 100, 200, 1, 1, false)]
    [InlineData("vgc", 100, 200, 1, 1, false)]
    public void BackgroundThrottlingIsAllowlistOnlyAndNeverTouchesTheGame(string name, int pid, int gamePid, int session, int processSession, bool expected) =>
        Assert.Equal(expected, BackgroundThrottler.IsEligible(name, pid, gamePid, session, processSession));

    [WindowsFact]
    public async Task BackgroundThrottlingRestoresTheExactPriorityOfARealProcess()
    {
        using var child = StartIdleChild();
        try
        {
            child.Refresh();
            var before = child.PriorityClass;
            var record = BackgroundThrottler.Throttle(child.Id, child.ProcessName);
            Assert.NotNull(record);
            child.Refresh();
            Assert.Equal(ProcessPriorityClass.BelowNormal, child.PriorityClass);
            Assert.True(BackgroundThrottler.Restore(record!));
            child.Refresh();
            Assert.Equal(before, child.PriorityClass);
            Assert.True(BackgroundThrottler.Restore(record!));
        }
        finally { child.Kill(); await child.WaitForExitAsync(); }
    }

    [WindowsFact]
    public async Task CrashRecoveryRestoresJournaledBackgroundProcesses()
    {
        using var folder = new TempFolder();
        using var child = StartIdleChild();
        try
        {
            var record = BackgroundThrottler.Throttle(child.Id, child.ProcessName) ?? throw new InvalidOperationException("Throttle failed.");
            var journal = new GameSessionJournal(Guid.NewGuid(), 1, 0, "game.exe", DateTimeOffset.UtcNow, null, new() { record });
            Directory.CreateDirectory(Path.Combine(folder.Path, "Sessions"));
            await File.WriteAllTextAsync(Path.Combine(folder.Path, "Sessions", "active-session.json"), JsonSerializer.Serialize(journal));
            var outcomes = await Manager(folder.Path).RecoverAsync();
            child.Refresh();
            Assert.Contains(outcomes, x => x.LeverId == "background-apps" && x.Status == LeverStatus.Applied);
            Assert.Equal(ProcessPriorityClass.Normal, child.PriorityClass);
            Assert.False(File.Exists(Path.Combine(folder.Path, "Sessions", "active-session.json")));
        }
        finally { child.Kill(); await child.WaitForExitAsync(); }
    }

    [WindowsFact]
    public async Task OnlyOneGameSessionCanOwnTheMachineAtATime()
    {
        using var folder = new TempFolder();
        using var child = StartIdleChild();
        try
        {
            var profile = LeversOff(child.ProcessName + ".exe");
            var first = await Manager(folder.Path).StartAsync(profile, TestData.Snapshot(), null, _ => Task.FromResult(child), true);
            Assert.NotNull(first);
            Assert.Null(await Manager(folder.Path).StartAsync(profile, TestData.Snapshot(), null, _ => Task.FromResult(child), true));
            await first!.EndAsync();
            await first.EndAsync();
            var again = await Manager(folder.Path).StartAsync(profile, TestData.Snapshot(), null, _ => Task.FromResult(child), true);
            Assert.NotNull(again);
            await again!.EndAsync();
        }
        finally { child.Kill(); await child.WaitForExitAsync(); }
    }

    [Fact]
    public async Task ProfileSettingsAreRolledBackWhenTheGameDoesNotStart()
    {
        using var folder = new TempFolder();
        var tweak = new StatefulTweak("profile.value");
        var profile = LeversOff("game.exe") with { TweakIds = new[] { tweak.Metadata.Id } };
        var manager = new GameSessionManager(folder.Path, new ITweak[] { tweak }, new ITweak[] { tweak }, new NullAudit());
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(profile, TestData.Snapshot(), null,
            _ => throw new InvalidOperationException("game did not start"), false));
        Assert.Equal(1, Convert.ToInt32(tweak.Value));
        // The failed start must also release the session lock.
        using var self = Process.GetCurrentProcess();
        var next = await manager.StartAsync(LeversOff("self.exe"), TestData.Snapshot(), null, _ => Task.FromResult(self), true);
        Assert.NotNull(next);
        await next!.EndAsync();
    }

    [Fact]
    public void AbCaptureOrderAlternatesToCancelDrift()
    {
        var order = AbTestRunner.CaptureOrder(3);
        Assert.Equal(new[] { false, true, true, false, false, true }, order);
        Assert.Equal(order.Count(x => x), order.Count(x => !x));
    }

    [Fact]
    public void GameDetectionAcceptsLibraryGamesAndRejectsLaunchers()
    {
        var roots = new[] { @"D:\SteamLibrary\steamapps\common", @"C:\XboxGames" };
        Assert.True(GameDetector.IsLikelyGameExecutable(@"D:\SteamLibrary\steamapps\common\Game\bin\game.exe", roots));
        Assert.False(GameDetector.IsLikelyGameExecutable(@"D:\SteamLibrary\steamapps\common\Game\UnityCrashHandler64.exe", roots));
        Assert.False(GameDetector.IsLikelyGameExecutable(@"D:\SteamLibrary\steamapps\common\Game\GameLauncher.exe", roots));
        Assert.False(GameDetector.IsLikelyGameExecutable(@"C:\Program Files\App\app.exe", roots));
        Assert.False(GameDetector.IsLikelyGameExecutable(@"D:\SteamLibrary\steamapps\commonX\game.exe", roots));
    }

    [Fact]
    public void AutoProfileOnlyUsesTheCatalogPowerPlanAndReversibleBoosts()
    {
        var auto = GameDetector.CreateAutoProfile("game.exe", null);
        Assert.Equal(new[] { GameDetector.PowerPlanTweakId }, auto.TweakIds);
        Assert.Contains(TweakCatalog.CreateTweaks(), x => x.Metadata.Id == GameDetector.PowerPlanTweakId);
        Assert.True(auto.RestoreOnExit && auto.ThrottleBackground && !auto.PreferPerformanceCores);
    }

    [Fact]
    public void ProfilesSavedBeforeSessionBoostsLoadWithBoostsOff()
    {
        const string legacy = """[{"Id":"7b0a7f1c-1111-4c8a-9d25-3f0d5b1e2a10","Name":"Old","GameProcess":"old.exe","ExecutablePath":null,"Profile":0,"TweakIds":[],"RestoreOnExit":true,"OverlayEnabled":false,"UpdatedAt":"2026-09-25T00:00:00+00:00","CpuPriorityEnabled":true,"HighPerformanceGpu":true}]""";
        var profile = JsonSerializer.Deserialize<List<GameProfile>>(legacy)!.Single();
        Assert.True(profile.CpuPriorityEnabled && profile.HighPerformanceGpu);
        Assert.False(profile.ThrottleBackground || profile.PreferPerformanceCores);
    }

    [Fact]
    public void AdvisorReportsMpoMemoryIntegrityAndHybridCpu()
    {
        var gaming = new GamingConfiguration(true, false, true, null);
        var insights = new SystemInsights(0, Array.Empty<NetworkAdapterSnapshot>(), Array.Empty<ProcessSnapshot>(), Array.Empty<StartupSnapshot>(),
            new ServiceSummary(0, 0, 0, 0), gaming, Array.Empty<string>());
        var machine = TestData.Snapshot() with { Insights = insights };
        var advice = PerformanceAdvisor.Evaluate(machine, Array.Empty<PerformanceAdvisor.MemoryModule>(), false, null, null, multiplaneOverlayDisabled: true, performanceCoreThreads: 16);
        Assert.Contains(advice, x => x.Id == "mpo-disabled");
        Assert.Contains(advice, x => x.Id == "memory-integrity");
        Assert.Contains(advice, x => x.Id == "hybrid-cpu" && x.Title.Contains("16"));
        Assert.DoesNotContain(PerformanceAdvisor.Evaluate(TestData.Snapshot(), Array.Empty<PerformanceAdvisor.MemoryModule>(), false, null, null), x => x.Id is "mpo-disabled" or "hybrid-cpu");
    }

    private static GameSessionManager Manager(string root) => new(root, Array.Empty<ITweak>(), Array.Empty<ITweak>(), new NullAudit());

    private static GameProfile LeversOff(string process) => new(Guid.NewGuid(), "Test", process, null, OptimizationProfileKind.Safe, Array.Empty<string>(), true, false,
        DateTimeOffset.UtcNow, CpuPriorityEnabled: false, HighPerformanceGpu: false, ThrottleBackground: false, PreferPerformanceCores: false);

    private static Process StartIdleChild()
    {
        var start = new ProcessStartInfo("ping.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "-n", "60", "127.0.0.1" }) start.ArgumentList.Add(argument);
        var child = Process.Start(start) ?? throw new InvalidOperationException("Child process could not be started.");
        // A child inherits the parent's priority class, and CpuPriorityUsesAboveNormalAndRestoresExactly raises the test process
        // while other test classes run in parallel. Pin the child to Normal so the throttling assertions do not depend on timing.
        child.PriorityClass = ProcessPriorityClass.Normal;
        return child;
    }
}
