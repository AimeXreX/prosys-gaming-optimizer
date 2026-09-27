using System.Text.Json;

namespace ProSyS.Core;

public sealed class OptimizationEngine
{
    private readonly string _dataRoot;
    private readonly IAuditLog _log;
    public OptimizationEngine(string dataRoot, IAuditLog log) { _dataRoot = dataRoot; _log = log; }

    public async Task<SessionSummary> ExecuteAsync(OptimizationPlan plan, IEnumerable<ITweak> allTweaks, CancellationToken ct = default)
    {
        var byId = allTweaks.ToDictionary(x => x.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        ValidatePlan(plan, byId);
        var selected = new DependencyPlanner().Order(plan.Tweaks.Where(x => x.Selected).Select(x => byId[x.TweakId])).ToList();
        await EnsurePlanIsCurrentAsync(plan, selected, ct);
        var sessionId = Guid.NewGuid();
        var folder = Path.Combine(_dataRoot, "Backups", sessionId.ToString("N"));
        Directory.CreateDirectory(folder);
        var journal = new SessionJournal(sessionId, plan.PlanId, OperationState.Created, DateTimeOffset.UtcNow, new());
        await SaveJournalAsync(folder, journal, ct);
        var backups = new Dictionary<string, TweakBackup>(StringComparer.OrdinalIgnoreCase);
        var results = new List<TweakExecutionResult>();
        try
        {
            foreach (var tweak in selected)
            {
                var backup = await tweak.BackupAsync(ct);
                backups[tweak.Metadata.Id] = backup;
            }
            await WriteJsonAtomicAsync(Path.Combine(folder, "backups.json"), backups, ct);
            journal = journal with { State = OperationState.BackedUp };
            await SaveJournalAsync(folder, journal, ct);

            foreach (var tweak in selected)
            {
                journal = journal with { State = OperationState.Applying };
                await SaveJournalAsync(folder, journal, ct);
                await tweak.ApplyAsync(ct);
                journal = journal with { State = OperationState.Verifying };
                await SaveJournalAsync(folder, journal, ct);
                var verification = await tweak.VerifyAsync(ct);
                var ok = verification.Status == DetectionStatus.Compliant;
                var result = new TweakExecutionResult(tweak.Metadata.Id, ok, ok ? "Applied and verified." : "Verification failed.", verification);
                results.Add(result);
                journal.Results.Add(result);
                await _log.WriteAsync(new { correlationId = sessionId, operation = "apply", tweak = tweak.Metadata.Id, result = ok }, ct);
                await SaveJournalAsync(folder, journal, ct);
                if (!ok) throw new InvalidOperationException($"VerificationFailure: {tweak.Metadata.Id}");
            }
            journal = journal with { State = OperationState.Completed };
            await SaveJournalAsync(folder, journal, ct);
            return new(sessionId, journal.State, folder, results);
        }
        catch (Exception ex)
        {
            // Recovery must finish even when the caller cancelled: the original token may already be signalled.
            ct = CancellationToken.None;
            journal = journal with { State = OperationState.RollbackPending, Error = ex.Message };
            await SaveJournalAsync(folder, journal, ct);
            var recoveryRequired = false;
            foreach (var tweak in selected.AsEnumerable().Reverse())
                if (backups.TryGetValue(tweak.Metadata.Id, out var backup))
                    try
                    {
                        await tweak.RollbackAsync(backup, ct);
                        var verification = await tweak.VerifyRollbackAsync(backup, ct);
                        var ok = BackupMatches(backup, verification);
                        results.Add(new(tweak.Metadata.Id, ok, ok ? "Original state restored and verified." : "Rollback verification failed.", verification));
                        if (!ok) recoveryRequired = true;
                        await _log.WriteAsync(new { correlationId = sessionId, operation = "automatic-rollback", tweak = tweak.Metadata.Id, result = ok }, ct);
                    }
                    catch (Exception rollbackError)
                    {
                        recoveryRequired = true;
                        results.Add(new(tweak.Metadata.Id, false, "Rollback failed: " + rollbackError.Message, null));
                    }
            journal = journal with { State = recoveryRequired ? OperationState.RecoveryRequired : OperationState.RolledBack };
            await SaveJournalAsync(folder, journal, ct);
            return new(sessionId, journal.State, folder, results);
        }
    }

    public async Task<SessionSummary> RollbackAsync(string sessionDirectory, IEnumerable<ITweak> allTweaks, CancellationToken ct = default)
    {
        var backupPath = Path.Combine(sessionDirectory, "backups.json");
        // Values are only changed after backups.json is written, so a session without it changed nothing and can simply be closed.
        var backups = File.Exists(backupPath)
            ? JsonSerializer.Deserialize<Dictionary<string, TweakBackup>>(await File.ReadAllTextAsync(backupPath, ct)) ?? new()
            : new Dictionary<string, TweakBackup>();
        var byId = allTweaks.ToDictionary(x => x.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        var results = new List<TweakExecutionResult>();
        foreach (var pair in backups.Reverse())
        {
            if (!byId.TryGetValue(pair.Key, out var tweak))
            {
                results.Add(new(pair.Key, false, "The capability is no longer in the catalog; restore this value manually.", null));
                continue;
            }
            try
            {
                await tweak.RollbackAsync(pair.Value, CancellationToken.None);
                var verification = await tweak.VerifyRollbackAsync(pair.Value, CancellationToken.None);
                var ok = BackupMatches(pair.Value, verification);
                results.Add(new(pair.Key, ok, ok ? "Original state restored." : "Rollback verification failed.", verification));
            }
            catch (Exception ex) { results.Add(new(pair.Key, false, "Rollback failed: " + ex.Message, null)); }
        }
        var state = results.All(x => x.Success) ? OperationState.RolledBack : OperationState.RecoveryRequired;
        var sessionId = Guid.TryParse(Path.GetFileName(sessionDirectory), out var parsed) ? parsed : Guid.Empty;
        var journalPath = Path.Combine(sessionDirectory, "journal.json");
        SessionJournal journal;
        try { journal = JsonSerializer.Deserialize<SessionJournal>(await File.ReadAllTextAsync(journalPath, CancellationToken.None)) ?? throw new InvalidDataException(); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { journal = new(sessionId, Guid.Empty, state, DateTimeOffset.UtcNow, new()); }
        journal.Results.AddRange(results);
        await SaveJournalAsync(sessionDirectory, journal with { State = state, Error = state == OperationState.RolledBack ? null : "Manual rollback could not verify every value." }, CancellationToken.None);
        await _log.WriteAsync(new { correlationId = sessionId, operation = "manual-rollback", result = state.ToString() }, CancellationToken.None);
        return new(sessionId, state, sessionDirectory, results);
    }

    /// <summary>Returns the newest session (by journal creation time) that has a captured backup, or null.</summary>
    public string? FindLatestRestorableSession()
    {
        var backupRoot = Path.Combine(_dataRoot, "Backups");
        if (!Directory.Exists(backupRoot)) return null;
        return new DirectoryInfo(backupRoot).GetDirectories()
            .Where(x => File.Exists(Path.Combine(x.FullName, "backups.json")))
            .Select(x => (x.FullName, CreatedAt: ReadCreatedAt(x)))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.FullName)
            .FirstOrDefault();
    }

    private static DateTimeOffset ReadCreatedAt(DirectoryInfo folder)
    {
        try { return JsonSerializer.Deserialize<SessionJournal>(File.ReadAllText(Path.Combine(folder.FullName, "journal.json")))?.CreatedAt ?? folder.CreationTimeUtc; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return folder.CreationTimeUtc; }
    }

    private static Task SaveJournalAsync(string folder, SessionJournal journal, CancellationToken ct) =>
        WriteJsonAtomicAsync(Path.Combine(folder, "journal.json"), journal, ct);

    public IReadOnlyList<RecoverySession> FindIncompleteSessions()
    {
        var backupRoot = Path.Combine(_dataRoot, "Backups");
        if (!Directory.Exists(backupRoot)) return Array.Empty<RecoverySession>();
        var result = new List<RecoverySession>();
        foreach (var folder in new DirectoryInfo(backupRoot).GetDirectories().OrderByDescending(x => x.LastWriteTimeUtc))
        {
            var path = Path.Combine(folder.FullName, "journal.json");
            try
            {
                var journal = JsonSerializer.Deserialize<SessionJournal>(File.ReadAllText(path));
                if (journal is not null && journal.State is OperationState.Created or OperationState.BackedUp or OperationState.Applying or OperationState.Verifying or OperationState.RollbackPending or OperationState.RecoveryRequired)
                    result.Add(new(folder.FullName, journal.SessionId, journal.State, journal.CreatedAt, journal.Error));
            }
            catch (Exception) when (File.Exists(path))
            {
                result.Add(new(folder.FullName, Guid.Empty, OperationState.RecoveryRequired, folder.CreationTimeUtc, "Journal is unreadable."));
            }
        }
        return result;
    }

    private static void ValidatePlan(OptimizationPlan plan, IReadOnlyDictionary<string, ITweak> catalog)
    {
        if (plan.Tweaks.Count == 0) throw new InvalidOperationException("PlanValidation: plan is empty.");
        if (!new PlanFactory().HasValidHash(plan)) throw new InvalidOperationException("PlanValidation: plan hash is invalid.");
        if (plan.Tweaks.Select(x => x.TweakId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Tweaks.Count)
            throw new InvalidOperationException("PlanValidation: duplicate tweak identifiers.");
        foreach (var item in plan.Tweaks.Where(x => x.Selected))
        {
            if (!catalog.ContainsKey(item.TweakId)) throw new InvalidOperationException($"PlanValidation: unknown tweak {item.TweakId}.");
            if (item.Compatibility.Status != CompatibilityStatus.Compatible || item.Current.Status == DetectionStatus.Compliant)
                throw new InvalidOperationException($"PlanValidation: {item.TweakId} is not applicable.");
        }
    }

    /// <summary>Detects every selected capability again so a plan built from an older state is never applied.</summary>
    private static async Task EnsurePlanIsCurrentAsync(OptimizationPlan plan, IEnumerable<ITweak> selected, CancellationToken ct)
    {
        var planned = plan.Tweaks.ToDictionary(x => x.TweakId, StringComparer.OrdinalIgnoreCase);
        foreach (var tweak in selected)
        {
            var current = await tweak.DetectAsync(ct);
            var expected = planned[tweak.Metadata.Id].Current;
            if (current.Status != expected.Status || !string.Equals(ValueText.Canonical(current.Value), ValueText.Canonical(expected.Value), StringComparison.Ordinal))
                throw new InvalidOperationException($"PlanValidation: {tweak.Metadata.Id} changed after the plan was created. Scan again before applying.");
        }
    }

    private static bool BackupMatches(TweakBackup backup, DetectionResult verification)
    {
        if (!backup.ValueExisted) return verification.Status == DetectionStatus.Unknown && verification.Value is null;
        return string.Equals(ValueText.Canonical(backup.OriginalValue, backup.ValueKind), ValueText.Canonical(verification.Value, backup.ValueKind), StringComparison.Ordinal);
    }

    internal static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken ct)
    {
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);
            await stream.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, true);
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record SessionJournal(Guid SessionId, Guid PlanId, OperationState State, DateTimeOffset CreatedAt, List<TweakExecutionResult> Results, string? Error = null);
}

public sealed record RecoverySession(string Directory, Guid SessionId, OperationState State, DateTimeOffset CreatedAt, string? Error);
