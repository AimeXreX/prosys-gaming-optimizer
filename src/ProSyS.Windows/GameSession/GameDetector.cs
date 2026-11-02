using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ProSyS.Core;

namespace ProSyS.Windows.GameSession;

public sealed record DetectedGame(Process Process, GameProfile Profile, bool AutoDetected);

/// <summary>
/// Finds a running game by polling the process list (no driver, no injection, no elevation). Profiled games match by process name;
/// unprofiled games match when their executable lives in a known store library and has a visible window.
/// </summary>
public sealed class GameDetector
{
    private static readonly string[] ExcludedNameParts =
    {
        "launcher", "crash", "report", "setup", "install", "unins", "redist", "helper", "update", "service", "cefprocess",
        "webhelper", "dxsetup", "anticheat", "easyanticheat", "battleye", "beservice", "prereq", "overlay", "bootstrap", "handler"
    };
    private static readonly TimeSpan CandidateWindow = TimeSpan.FromSeconds(90);
    private readonly IReadOnlyList<string> _libraryRoots;
    private readonly Dictionary<int, DateTimeOffset> _candidates = new();
    private readonly HashSet<int> _rejected = new();

    public GameDetector(IReadOnlyList<string> libraryRoots) => _libraryRoots = libraryRoots;

    public IReadOnlyList<string> LibraryRoots => _libraryRoots;

    public DetectedGame? Poll(IReadOnlyList<GameProfile> profiles, bool autoDetectLibraryGames)
    {
        var byName = profiles.GroupBy(x => Path.GetFileNameWithoutExtension(x.GameProcess), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(p => p.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);
        var processes = Process.GetProcesses();
        DetectedGame? found = null;
        var alive = new HashSet<int>();
        foreach (var process in processes)
        {
            alive.Add(process.Id);
            if (found is not null) { process.Dispose(); continue; }
            if (!_rejected.Contains(process.Id) && byName.TryGetValue(process.ProcessName, out var profile)) { found = new(process, profile, false); continue; }
            if (autoDetectLibraryGames && IsAutoCandidate(process)) { found = new(process, CreateAutoProfile(process.ProcessName + ".exe", SessionNative.GetImagePath(process.Id)), true); continue; }
            process.Dispose();
        }
        _rejected.IntersectWith(alive);
        foreach (var gone in _candidates.Keys.Where(x => !alive.Contains(x)).ToArray()) _candidates.Remove(gone);
        return found;
    }

    /// <summary>Forget a PID so a later poll does not re-detect a game that was just handled.</summary>
    public void Ignore(int processId) { _rejected.Add(processId); _candidates.Remove(processId); }

    private bool IsAutoCandidate(Process process)
    {
        if (_rejected.Contains(process.Id)) return false;
        if (!_candidates.TryGetValue(process.Id, out var firstSeen))
        {
            var path = SessionNative.GetImagePath(process.Id);
            if (path is null || !IsLikelyGameExecutable(path, _libraryRoots)) { _rejected.Add(process.Id); return false; }
            _candidates[process.Id] = firstSeen = DateTimeOffset.UtcNow;
        }
        try
        {
            if (process.MainWindowHandle != IntPtr.Zero) { _candidates.Remove(process.Id); return true; }
        }
        catch (InvalidOperationException) { }
        if (DateTimeOffset.UtcNow - firstSeen > CandidateWindow) { _candidates.Remove(process.Id); _rejected.Add(process.Id); }
        return false;
    }

    /// <summary>Reads the image path with limited query rights, which anti-cheat protected games still allow.</summary>
    public static string? TryGetImagePath(int processId) => SessionNative.GetImagePath(processId);

    public static bool IsLikelyGameExecutable(string imagePath, IReadOnlyList<string> libraryRoots)
    {
        if (!imagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var name = Path.GetFileNameWithoutExtension(imagePath);
        if (ExcludedNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase))) return false;
        return libraryRoots.Any(root => imagePath.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
    }

    public const string PowerPlanTweakId = "power.plan.high-performance";

    /// <summary>
    /// Conservative defaults for a game without a saved profile: the high-performance power plan (restored on exit), Above normal priority,
    /// background EcoQoS and the GPU choice for the next launch. No other registry settings are touched.
    /// </summary>
    public static GameProfile CreateAutoProfile(string processName, string? executablePath) =>
        new(Guid.Empty, Path.GetFileNameWithoutExtension(processName), processName, executablePath, OptimizationProfileKind.Safe,
            new[] { PowerPlanTweakId }, true, false, DateTimeOffset.UtcNow, CpuPriorityEnabled: true, HighPerformanceGpu: true,
            ThrottleBackground: true, PreferPerformanceCores: false);

    public static IReadOnlyList<string> DiscoverLibraryRoots()
    {
        var roots = new List<string>();
        var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath")?.ToString()?.Replace('/', '\\');
        if (!string.IsNullOrWhiteSpace(steam))
        {
            roots.Add(Path.Combine(steam, "steamapps", "common"));
            var folders = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(folders))
                foreach (Match match in Regex.Matches(File.ReadAllText(folders), "\"path\"\\s+\"(?<p>[^\"]+)\""))
                    roots.Add(Path.Combine(match.Groups["p"].Value.Replace(@"\\", @"\"), "steamapps", "common"));
        }
        var epicManifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (Directory.Exists(epicManifests))
            foreach (var item in Directory.EnumerateFiles(epicManifests, "*.item").Take(500))
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(item));
                    if (document.RootElement.TryGetProperty("InstallLocation", out var location) && location.GetString() is { Length: > 3 } path) roots.Add(path);
                }
                catch (Exception ex) when (ex is JsonException or IOException) { }
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        roots.AddRange(new[]
        {
            Path.Combine(programFiles, "Epic Games"), Path.Combine(programFiles, "EA Games"), Path.Combine(programFiles, "Riot Games"),
            Path.Combine(programFilesX86, "Ubisoft", "Ubisoft Game Launcher", "games"), Path.Combine(programFilesX86, "GOG Galaxy", "Games")
        });
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed))
        {
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "XboxGames"));
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "Riot Games"));
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "Games"));
        }
        return roots.Where(Directory.Exists).Select(x => Path.GetFullPath(x).TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
