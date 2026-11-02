using System.Diagnostics;
using System.Text.Json;
using ProSyS.Core;

namespace ProSyS.Windows.GameSession;

public sealed record GameSessionJournal(Guid SessionId, int ProcessId, long ProcessStartTicks, string GameProcess, DateTimeOffset StartedAt,
    string? RegistryBackupDirectory, List<ThrottledProcess> Throttled);

/// <summary>
/// Runs one game session for the control center and the background agent alike: profile settings through the transactional engine,
/// then process-level boosts, and a reverse-order restore when the game exits. Only one session can be active per user at a time
/// (an exclusive lock file shared by the app and the agent). Every persistent change is journaled before the next step so
/// <see cref="RecoverAsync"/> can restore it after a crash or power loss.
/// </summary>
public sealed class GameSessionManager
{
    private readonly string _dataRoot;
    private readonly string _sessionsRoot;
    private readonly IReadOnlyList<ITweak> _tweaks;
    private readonly IReadOnlyList<ITweak> _rollbackTweaks;
    private readonly IAuditLog _log;

    public GameSessionManager(string dataRoot, IReadOnlyList<ITweak> tweaks, IReadOnlyList<ITweak> rollbackTweaks, IAuditLog log)
    {
        _dataRoot = dataRoot; _tweaks = tweaks; _rollbackTweaks = rollbackTweaks; _log = log;
        _sessionsRoot = Path.Combine(dataRoot, "Sessions");
        Directory.CreateDirectory(_sessionsRoot);
    }

    private string LockPath => Path.Combine(_sessionsRoot, "session.lock");
    private string JournalPath => Path.Combine(_sessionsRoot, "active-session.json");

    /// <summary>
    /// Applies the profile, then obtains the game from <paramref name="acquireGame"/> (launch it, or return an already running process).
    /// Returns null when another ProSyS process already owns a game session.
    /// </summary>
    /// <param name="gameAlreadyRunning">
    /// The game started before ProSyS saw it (background agent). Windows reads the GPU preference only at launch, so it is then applied
    /// as a separate, kept change for the next launch instead of being undone when this session ends.
    /// </param>
    public async Task<ActiveGameSession?> StartAsync(GameProfile profile, MachineSnapshot snapshot, string? executablePath,
        Func<CancellationToken, Task<Process>> acquireGame, bool gameAlreadyRunning, CancellationToken ct = default)
    {
        FileStream sessionLock;
        try { sessionLock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (IOException) { return null; }
        var outcomes = new List<LeverOutcome>();
        var journal = new GameSessionJournal(Guid.NewGuid(), 0, 0, profile.GameProcess, DateTimeOffset.UtcNow, null, new());
        CpuOptimizationSession? cpuSession = null;
        PerformanceCoreLever? coreSession = null;
        try
        {
            if (File.Exists(JournalPath)) await RecoverOwnedAsync(ct);

            var gpuTweak = profile.HighPerformanceGpu && executablePath is not null ? TweakCatalog.CreateGpuPreferenceTweak(executablePath) : null;
            if (gpuTweak is not null && gameAlreadyRunning) outcomes.Add(await ApplyGpuForNextLaunchAsync(gpuTweak, snapshot, ct));

            var sessionTweaks = _tweaks.ToList();
            var selected = profile.TweakIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (gpuTweak is not null && !gameAlreadyRunning) { sessionTweaks.Add(gpuTweak); selected.Add(gpuTweak.Metadata.Id); }
            if (selected.Count > 0)
            {
                var plan = new PlanFactory().Select(await new PlanFactory().CreateAsync(snapshot, sessionTweaks.Where(x => selected.Contains(x.Metadata.Id)).ToArray(), ct), selected);
                if (plan.Tweaks.Any(x => x.Selected))
                {
                    var result = await Engine().ExecuteAsync(plan, sessionTweaks, ct);
                    if (result.State != OperationState.Completed)
                        throw new InvalidOperationException("Profile settings could not be verified and were rolled back; the game session was not started.");
                    outcomes.Add(new("profile-settings", LeverStatus.Applied, $"{result.Results.Count} profile settings applied and verified.", $"{result.Results.Count} تنظیم پروفایل اعمال و تأیید شد."));
                    if (profile.RestoreOnExit) journal = journal with { RegistryBackupDirectory = result.BackupDirectory };
                }
                else outcomes.Add(new("profile-settings", LeverStatus.AlreadyOptimal, "Profile settings are already in place.", "تنظیمات پروفایل از قبل اعمال شده‌اند."));
                await SaveJournalAsync(journal, ct);
            }

            var game = await acquireGame(ct);
            journal = journal with { ProcessId = game.Id, ProcessStartTicks = SessionNative.GetStartTicks(game.Id) ?? 0 };
            await SaveJournalAsync(journal, ct);

            if (profile.CpuPriorityEnabled)
                try
                {
                    cpuSession = new ProcessCpuOptimizer().Apply(game);
                    outcomes.Add(new("cpu-priority", LeverStatus.Applied, "Game raised to Above normal priority.", "اولویت بازی روی Above normal قرار گرفت."));
                }
                catch (InvalidOperationException)
                {
                    outcomes.Add(new("cpu-priority", LeverStatus.Skipped, "Windows denied the priority change (protected game or anti-cheat).", "ویندوز اجازه تغییر اولویت را نداد (بازی محافظت‌شده یا ضدتقلب)."));
                }

            if (profile.PreferPerformanceCores)
            {
                var (session, outcome) = PerformanceCoreLever.Apply(game.Id);
                coreSession = session; outcomes.Add(outcome);
            }

            var active = new ActiveGameSession(this, sessionLock, game, profile, journal, outcomes, cpuSession, coreSession);
            if (profile.ThrottleBackground) await active.RefreshBackgroundAsync(ct);
            await _log.WriteAsync(new { correlationId = journal.SessionId, operation = "game-session-start", game = profile.GameProcess, outcomes = outcomes.Select(x => new { x.LeverId, x.Status }) }, ct);
            return active;
        }
        catch
        {
            await RestoreAsync(journal, cpuSession, coreSession, CancellationToken.None);
            sessionLock.Dispose();
            throw;
        }
    }

    /// <summary>Restores state left by a session whose owner crashed. Skips silently when another live process holds the session lock.</summary>
    public async Task<IReadOnlyList<LeverOutcome>> RecoverAsync(CancellationToken ct = default)
    {
        if (!File.Exists(JournalPath)) return Array.Empty<LeverOutcome>();
        try
        {
            using var sessionLock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            return await RecoverOwnedAsync(ct);
        }
        catch (IOException) { return Array.Empty<LeverOutcome>(); }
    }

    private async Task<LeverOutcome> ApplyGpuForNextLaunchAsync(ITweak gpuTweak, MachineSnapshot snapshot, CancellationToken ct)
    {
        const string id = "gpu-preference";
        var plan = await new PlanFactory().CreateAsync(snapshot, new[] { gpuTweak }, ct);
        plan = new PlanFactory().Select(plan, new HashSet<string>(new[] { gpuTweak.Metadata.Id }, StringComparer.OrdinalIgnoreCase));
        if (!plan.Tweaks.Any(x => x.Selected))
            return plan.Tweaks[0].Current.Status == DetectionStatus.Compliant
                ? new(id, LeverStatus.AlreadyOptimal, "Game already uses the high-performance GPU.", "بازی از قبل روی کارت گرافیک قدرتمند تنظیم است.")
                : new(id, LeverStatus.Skipped, plan.Tweaks[0].Compatibility.Reason, "انتخاب GPU برای این سیستم لازم نیست.");
        var result = await Engine().ExecuteAsync(plan, new[] { gpuTweak }, ct);
        return result.State == OperationState.Completed
            ? new(id, LeverStatus.Applied, "High-performance GPU selected for this game; it takes effect from the next launch (undo it in History & Backups).", "کارت گرافیک قدرتمند برای این بازی انتخاب شد؛ از اجرای بعدی اعمال می‌شود (بازگردانی از تاریخچه و پشتیبان).")
            : new(id, LeverStatus.Failed, "The GPU preference could not be verified and was rolled back.", "انتخاب GPU تأیید نشد و بازگردانده شد.");
    }

    private async Task<IReadOnlyList<LeverOutcome>> RecoverOwnedAsync(CancellationToken ct)
    {
        GameSessionJournal? journal;
        try { journal = JsonSerializer.Deserialize<GameSessionJournal>(await File.ReadAllTextAsync(JournalPath, ct)); }
        catch (JsonException) { journal = null; }
        if (journal is null) { File.Delete(JournalPath); return Array.Empty<LeverOutcome>(); }
        var outcomes = await RestoreAsync(journal, null, null, ct);
        await _log.WriteAsync(new { correlationId = journal.SessionId, operation = "game-session-recovery", outcomes = outcomes.Select(x => new { x.LeverId, x.Status }) }, ct);
        return outcomes;
    }

    internal async Task<IReadOnlyList<LeverOutcome>> RestoreAsync(GameSessionJournal journal, CpuOptimizationSession? cpu, PerformanceCoreLever? cores, CancellationToken ct)
    {
        var outcomes = new List<LeverOutcome>();
        if (cores is not null) outcomes.Add(cores.TryRestore()
            ? new("performance-cores", LeverStatus.Applied, "Default CPU scheduling restored.", "زمان‌بندی پیش‌فرض هسته‌ها بازگردانده شد.")
            : new("performance-cores", LeverStatus.Failed, "CPU sets could not be restored.", "بازگردانی هسته‌ها ناموفق بود."));
        if (cpu is not null) outcomes.Add(cpu.TryRestore()
            ? new("cpu-priority", LeverStatus.Applied, "Original process priority restored.", "اولویت اصلی پردازش بازگردانده شد.")
            : new("cpu-priority", LeverStatus.Failed, "Process priority could not be restored.", "بازگردانی اولویت پردازش ناموفق بود."));
        if (journal.Throttled.Count > 0)
        {
            var restored = journal.Throttled.Count(BackgroundThrottler.Restore);
            outcomes.Add(new("background-apps", restored == journal.Throttled.Count ? LeverStatus.Applied : LeverStatus.Failed,
                $"{restored}/{journal.Throttled.Count} background apps returned to normal.", $"{restored} از {journal.Throttled.Count} برنامه پس‌زمینه به حالت عادی برگشت."));
        }
        if (journal.RegistryBackupDirectory is { } directory && Directory.Exists(directory))
        {
            var rollback = await Engine().RollbackAsync(directory, _rollbackTweaks, ct, TweakCatalog.ResolveDynamic);
            var ok = rollback.State == OperationState.RolledBack;
            outcomes.Add(new("profile-settings", ok ? LeverStatus.Applied : LeverStatus.Failed,
                $"Profile settings restored: {rollback.Results.Count(x => x.Success)}/{rollback.Results.Count}.",
                $"تنظیمات پروفایل بازگردانده شد: {rollback.Results.Count(x => x.Success)} از {rollback.Results.Count}."));
        }
        if (File.Exists(JournalPath)) File.Delete(JournalPath);
        return outcomes;
    }

    internal Task SaveJournalAsync(GameSessionJournal journal, CancellationToken ct)
    {
        var temp = JournalPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, JournalPath, true);
        return Task.CompletedTask;
    }

    private OptimizationEngine Engine() => new(_dataRoot, _log);
}

public sealed class ActiveGameSession
{
    private readonly GameSessionManager _manager;
    private readonly FileStream _lock;
    private readonly CpuOptimizationSession? _cpu;
    private readonly PerformanceCoreLever? _cores;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GameSessionJournal _journal;
    private IReadOnlyList<LeverOutcome>? _restoreOutcomes;

    internal ActiveGameSession(GameSessionManager manager, FileStream sessionLock, Process game, GameProfile profile, GameSessionJournal journal,
        List<LeverOutcome> outcomes, CpuOptimizationSession? cpu, PerformanceCoreLever? cores)
    {
        _manager = manager; _lock = sessionLock; Game = game; Profile = profile; _journal = journal; StartOutcomes = outcomes; _cpu = cpu; _cores = cores;
    }

    public Process Game { get; }
    public GameProfile Profile { get; }
    public IReadOnlyList<LeverOutcome> StartOutcomes { get; }
    public int ThrottledCount => _journal.Throttled.Count;
    public bool HasEnded => _restoreOutcomes is not null;

    /// <summary>Throttles allowlisted background apps that started after the game; safe to call periodically.</summary>
    public async Task RefreshBackgroundAsync(CancellationToken ct = default)
    {
        if (!Profile.ThrottleBackground) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (HasEnded) return;
            var known = _journal.Throttled.Select(x => x.ProcessId).ToHashSet();
            var added = BackgroundThrottler.ThrottleEligible(Game.Id, known);
            if (added.Count == 0) return;
            _journal = _journal with { Throttled = _journal.Throttled.Concat(added).ToList() };
            await _manager.SaveJournalAsync(_journal, ct);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Restores every boost in reverse order. Idempotent.</summary>
    public async Task<IReadOnlyList<LeverOutcome>> EndAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_restoreOutcomes is not null) return _restoreOutcomes;
            _restoreOutcomes = await _manager.RestoreAsync(_journal, _cpu, _cores, CancellationToken.None);
            _lock.Dispose();
            return _restoreOutcomes;
        }
        finally { _gate.Release(); }
    }
}
