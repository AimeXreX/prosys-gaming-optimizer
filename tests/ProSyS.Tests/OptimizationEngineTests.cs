using System.Text.Json;
using ProSyS.Core;
using Xunit;

namespace ProSyS.Tests;

public class OptimizationEngineTests
{
    [Fact]
    public async Task TamperedPlansAreRejectedBeforeMutation()
    {
        using var folder = new TempFolder();
        var tweak = new StatefulTweak("a");
        var plan = await new PlanFactory().CreateAsync(TestData.Snapshot(), new[] { tweak });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OptimizationEngine(folder.Path, new NullAudit()).ExecuteAsync(plan with { Sha256 = new string('0', 64) }, new[] { tweak }));
        Assert.Contains("hash", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, tweak.Value);
    }

    [Fact]
    public async Task StalePlansAreRejectedBeforeMutation()
    {
        using var folder = new TempFolder();
        var tweak = new StatefulTweak("a");
        var plan = await new PlanFactory().CreateAsync(TestData.Snapshot(), new[] { tweak });
        tweak.Value = 7; // the user changed the setting after the plan was built
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new OptimizationEngine(folder.Path, new NullAudit()).ExecuteAsync(plan, new[] { tweak }));
        Assert.Contains("changed after the plan", error.Message);
        Assert.Equal(7, tweak.Value);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "Backups")));
    }

    [Fact]
    public async Task FailedRollbackVerificationRequiresRecovery()
    {
        using var folder = new TempFolder();
        var first = new StatefulTweak("first") { CorruptRollback = true };
        var second = new StatefulTweak("second") { FailApply = true };
        var plan = await new PlanFactory().CreateAsync(TestData.Snapshot(), new ITweak[] { first, second });
        var result = await new OptimizationEngine(folder.Path, new NullAudit()).ExecuteAsync(plan, new ITweak[] { first, second });
        Assert.Equal(OperationState.RecoveryRequired, result.State);
        Assert.Contains(result.Results, x => x.TweakId == "first" && !x.Success && x.Message.Contains("Rollback"));
    }

    [Fact]
    public async Task CancellationDuringApplyStillRollsBackEverything()
    {
        using var folder = new TempFolder();
        using var cts = new CancellationTokenSource();
        var first = new StatefulTweak("a");
        var second = new StatefulTweak("b") { OnApply = ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); } };
        var plan = await new PlanFactory().CreateAsync(TestData.Snapshot(), new ITweak[] { first, second });
        var result = await new OptimizationEngine(folder.Path, new NullAudit()).ExecuteAsync(plan, new ITweak[] { first, second }, cts.Token);
        Assert.Equal(OperationState.RolledBack, result.State);
        Assert.Equal(1, first.Value);
        Assert.Equal(1, second.Value);
        Assert.Equal("RolledBack", JournalState(result.BackupDirectory));
    }

    [Fact]
    public async Task ManualRollbackFromDiskRestoresBinaryValuesAndClosesTheSession()
    {
        using var folder = new TempFolder();
        var original = new byte[] { 0x01, 0x02, 0xFE };
        var tweak = new StatefulTweak("binary") { Value = original, Recommended = new byte[] { 0 }, Kind = "Binary" };
        var engine = new OptimizationEngine(folder.Path, new NullAudit());
        var plan = await new PlanFactory().CreateAsync(TestData.Snapshot(), new[] { tweak });
        var applied = await engine.ExecuteAsync(plan, new[] { tweak });
        Assert.Equal(OperationState.Completed, applied.State);

        var rolledBack = await engine.RollbackAsync(applied.BackupDirectory, new[] { tweak });

        Assert.Equal(OperationState.RolledBack, rolledBack.State);
        Assert.Equal(original, Assert.IsType<byte[]>(tweak.Value));
        Assert.Equal("RolledBack", JournalState(applied.BackupDirectory));
    }

    [Fact]
    public async Task ManualRollbackResolvesAnIncompleteSession()
    {
        using var folder = new TempFolder();
        var first = new StatefulTweak("first") { CorruptRollback = true };
        var second = new StatefulTweak("second") { FailApply = true };
        var engine = new OptimizationEngine(folder.Path, new NullAudit());
        var plan = await new PlanFactory().CreateAsync(TestData.Snapshot(), new ITweak[] { first, second });
        var failed = await engine.ExecuteAsync(plan, new ITweak[] { first, second });
        Assert.Single(engine.FindIncompleteSessions());

        first.CorruptRollback = false;
        var repaired = await engine.RollbackAsync(failed.BackupDirectory, new ITweak[] { first, second });

        Assert.Equal(OperationState.RolledBack, repaired.State);
        Assert.Empty(engine.FindIncompleteSessions());
    }

    [Fact]
    public async Task SessionInterruptedBeforeBackupIsClosedWithoutChanges()
    {
        using var folder = new TempFolder();
        var engine = new OptimizationEngine(folder.Path, new NullAudit());
        var session = Directory.CreateDirectory(Path.Combine(folder.Path, "Backups", Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(session, "journal.json"), $$"""{"SessionId":"{{Guid.NewGuid()}}","PlanId":"{{Guid.NewGuid()}}","State":0,"CreatedAt":"2026-01-01T00:00:00+00:00","Results":[]}""");
        Assert.Single(engine.FindIncompleteSessions());

        var result = await engine.RollbackAsync(session, Array.Empty<ITweak>());

        Assert.Equal(OperationState.RolledBack, result.State);
        Assert.Empty(result.Results);
        Assert.Empty(engine.FindIncompleteSessions());
    }

    [Fact]
    public async Task LatestRestorableSessionUsesJournalTimeAndSkipsSessionsWithoutBackup()
    {
        using var folder = new TempFolder();
        var engine = new OptimizationEngine(folder.Path, new NullAudit());
        var tweak = new StatefulTweak("a");
        var older = await engine.ExecuteAsync(await new PlanFactory().CreateAsync(TestData.Snapshot(), new[] { tweak }), new[] { tweak });
        tweak.Value = 1;
        var newer = await engine.ExecuteAsync(await new PlanFactory().CreateAsync(TestData.Snapshot(), new[] { tweak }), new[] { tweak });
        Directory.CreateDirectory(Path.Combine(folder.Path, "Backups", Guid.NewGuid().ToString("N"))); // aborted before backup
        Directory.SetLastWriteTimeUtc(older.BackupDirectory, DateTime.UtcNow.AddHours(1)); // directory times are not trusted

        Assert.Equal(newer.BackupDirectory, engine.FindLatestRestorableSession());
    }

    private static string? JournalState(string folder)
    {
        using var journal = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "journal.json")));
        var state = journal.RootElement.GetProperty("State");
        return state.ValueKind == JsonValueKind.Number ? ((OperationState)state.GetInt32()).ToString() : state.GetString();
    }
}
