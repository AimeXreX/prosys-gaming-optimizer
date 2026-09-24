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
        var selected = plan.Tweaks.Where(x => x.Selected).Select(x => byId[x.TweakId]).ToList();
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
                var ok = verification.Status == DetectionStatus.Disabled;
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
        var backups = JsonSerializer.Deserialize<Dictionary<string, TweakBackup>>(await File.ReadAllTextAsync(Path.Combine(sessionDirectory, "backups.json"), ct)) ?? new();
        var byId = allTweaks.ToDictionary(x => x.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        var results = new List<TweakExecutionResult>();
        foreach (var pair in backups.Reverse())
        {
            if (!byId.TryGetValue(pair.Key, out var tweak)) continue;
            await tweak.RollbackAsync(pair.Value, ct);
            var verification = await tweak.VerifyRollbackAsync(pair.Value, ct);
            var ok = BackupMatches(pair.Value, verification);
            results.Add(new(pair.Key, ok, ok ? "Original state restored." : "Rollback verification failed.", verification));
        }
        var state = results.All(x => x.Success) ? OperationState.RolledBack : OperationState.RecoveryRequired;
        return new(Guid.Parse(Path.GetFileName(sessionDirectory)), state, sessionDirectory, results);
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
            if (item.Compatibility.Status != CompatibilityStatus.Compatible || item.Current.Status == DetectionStatus.Disabled)
                throw new InvalidOperationException($"PlanValidation: {item.TweakId} is not applicable.");
        }
    }

    private static bool BackupMatches(TweakBackup backup, DetectionResult verification)
    {
        if (!backup.ValueExisted) return verification.Status == DetectionStatus.Unknown && verification.Value is null;
        return string.Equals(Normalize(backup.OriginalValue), Normalize(verification.Value), StringComparison.Ordinal);
    }

    private static string? Normalize(object? value) => value is JsonElement element ? element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    } : value?.ToString();
    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken ct)
    {
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, JsonOptions), ct);
        File.Move(temp, path, true);
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record SessionJournal(Guid SessionId, Guid PlanId, OperationState State, DateTimeOffset CreatedAt, List<TweakExecutionResult> Results, string? Error = null);
}

public sealed record RecoverySession(string Directory, Guid SessionId, OperationState State, DateTimeOffset CreatedAt, string? Error);
