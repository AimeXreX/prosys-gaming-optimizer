using System.ComponentModel;
using System.Diagnostics;

namespace ProSyS.Windows.GameSession;

public enum LeverStatus { Applied, AlreadyOptimal, Skipped, Failed }

/// <summary>What one lever did during a game session. Both languages are produced here so the agent and the app report identically.</summary>
public sealed record LeverOutcome(string LeverId, LeverStatus Status, string Message, string MessageFa)
{
    public string Text(bool persian) => persian ? MessageFa : Message;
}

/// <summary>On hybrid CPUs (P-cores + E-cores) steers the game's threads to the highest-efficiency-class cores. The OS default is restored exactly.</summary>
public sealed class PerformanceCoreLever
{
    private readonly int _processId;
    private readonly uint[] _original;
    private PerformanceCoreLever(int processId, uint[] original) { _processId = processId; _original = original; }

    /// <summary>Returns the CPU set IDs of the fastest core class, or empty when the CPU is not hybrid or the split would leave too few cores.</summary>
    public static uint[] SelectPerformanceCpuSets(IReadOnlyList<CpuSetInfo> sets)
    {
        if (sets.Select(x => x.EfficiencyClass).Distinct().Count() < 2) return Array.Empty<uint>();
        var best = sets.Max(x => x.EfficiencyClass);
        var selected = sets.Where(x => x.EfficiencyClass == best).Select(x => x.Id).OrderBy(x => x).ToArray();
        return selected.Length >= 4 ? selected : Array.Empty<uint>();
    }

    public static (PerformanceCoreLever? Session, LeverOutcome Outcome) Apply(int processId)
    {
        const string id = "performance-cores";
        var ids = SelectPerformanceCpuSets(SessionNative.GetCpuSets());
        if (ids.Length == 0) return (null, new(id, LeverStatus.Skipped, "CPU is not hybrid; Windows scheduling already uses identical cores.", "پردازنده هیبریدی نیست؛ همه هسته‌ها هم‌توان هستند."));
        try
        {
            using var handle = SessionNative.Open(processId, SessionNative.ProcessQueryLimitedInformation | SessionNative.ProcessSetLimitedInformation);
            var original = SessionNative.GetProcessCpuSets(handle);
            SessionNative.SetProcessCpuSets(handle, ids);
            return (new(processId, original), new(id, LeverStatus.Applied, $"Game steered to {ids.Length} performance-core threads.", $"بازی روی {ids.Length} رشته هسته‌های قدرتمند (P-core) متمرکز شد."));
        }
        catch (Win32Exception ex)
        {
            return (null, new(id, LeverStatus.Skipped, $"Windows denied CPU-set access (protected game or anti-cheat): {ex.Message}", "ویندوز اجازه تغییر هسته‌ها را نداد (بازی محافظت‌شده یا ضدتقلب)."));
        }
    }

    public bool TryRestore()
    {
        try
        {
            using var handle = SessionNative.Open(_processId, SessionNative.ProcessQueryLimitedInformation | SessionNative.ProcessSetLimitedInformation);
            SessionNative.SetProcessCpuSets(handle, _original);
            return true;
        }
        catch (Win32Exception) { return !IsAlive(_processId); }
    }

    private static bool IsAlive(int processId) { try { using var p = Process.GetProcessById(processId); return !p.HasExited; } catch (ArgumentException) { return false; } }
}

public sealed record ThrottledProcess(int ProcessId, long StartTicks, string Name, uint OriginalPriority, bool PriorityLowered, uint OriginalControlMask, uint OriginalStateMask);

/// <summary>
/// Applies EcoQoS and Below Normal priority to an explicit allowlist of non-critical user apps (browsers, sync clients, launchers) while a game runs.
/// Voice chat, recording/streaming tools, security software and Windows components are never touched.
/// </summary>
public static class BackgroundThrottler
{
    public static readonly IReadOnlySet<string> AllowedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "msedgewebview2",
        "OneDrive", "Dropbox", "GoogleDriveFS", "Teams", "ms-teams", "slack", "Spotify", "WhatsApp", "Telegram",
        "EpicGamesLauncher", "EpicWebHelper", "EADesktop", "upc", "UbisoftConnect", "GalaxyClient", "Battle.net",
        "AdobeCollabSync", "CCXProcess", "Creative Cloud", "iCloudDrive", "iCloudServices"
    };

    public static bool IsEligible(string processName, int processId, int gameProcessId, int currentSessionId, int processSessionId) =>
        processId != gameProcessId && processId != Environment.ProcessId && processSessionId == currentSessionId && AllowedProcessNames.Contains(processName);

    public static IReadOnlyList<ThrottledProcess> ThrottleEligible(int gameProcessId, ISet<int> alreadyThrottled)
    {
        var result = new List<ThrottledProcess>();
        using var self = Process.GetCurrentProcess();
        var session = self.SessionId;
        foreach (var process in Process.GetProcesses())
            using (process)
            {
                try
                {
                    if (alreadyThrottled.Contains(process.Id) || !IsEligible(process.ProcessName, process.Id, gameProcessId, session, process.SessionId)) continue;
                    if (Throttle(process.Id, process.ProcessName) is { } record) result.Add(record);
                }
                catch (InvalidOperationException) { }
            }
        return result;
    }

    public static ThrottledProcess? Throttle(int processId, string name)
    {
        try
        {
            var start = SessionNative.GetStartTicks(processId);
            if (start is null) return null;
            using var handle = SessionNative.Open(processId, SessionNative.ProcessQueryLimitedInformation | SessionNative.ProcessSetInformation);
            var priority = SessionNative.GetPriority(handle);
            var throttling = SessionNative.GetThrottling(handle);
            SessionNative.SetThrottling(handle, SessionNative.PowerThrottlingExecutionSpeed, SessionNative.PowerThrottlingExecutionSpeed);
            var lower = priority == SessionNative.NormalPriorityClass;
            if (lower) SessionNative.SetPriority(handle, SessionNative.BelowNormalPriorityClass);
            return new(processId, start.Value, name, priority, lower, throttling.ControlMask, throttling.StateMask);
        }
        catch (Win32Exception) { return null; }
    }

    /// <summary>Restores a process only when it is the same process instance (PID and creation time) that was throttled.</summary>
    public static bool Restore(ThrottledProcess record)
    {
        if (SessionNative.GetStartTicks(record.ProcessId) != record.StartTicks) return true;
        try
        {
            using var handle = SessionNative.Open(record.ProcessId, SessionNative.ProcessQueryLimitedInformation | SessionNative.ProcessSetInformation);
            SessionNative.SetThrottling(handle, record.OriginalControlMask, record.OriginalStateMask);
            if (record.PriorityLowered && SessionNative.GetPriority(handle) == SessionNative.BelowNormalPriorityClass)
                SessionNative.SetPriority(handle, record.OriginalPriority);
            return true;
        }
        catch (Win32Exception) { return SessionNative.GetStartTicks(record.ProcessId) != record.StartTicks; }
    }
}
