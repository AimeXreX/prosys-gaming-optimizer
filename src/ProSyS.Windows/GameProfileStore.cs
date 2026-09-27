using System.Text.Json;
using ProSyS.Core;

namespace ProSyS.Windows;

public sealed class GameProfileStore
{
    private readonly string _path;
    public GameProfileStore(string dataRoot) { Directory.CreateDirectory(dataRoot); _path = Path.Combine(dataRoot, "game-profiles.json"); }

    /// <summary>Path of the copy an unreadable profile file was moved to during the last load, if any.</summary>
    public string? QuarantinedFile { get; private set; }

    public async Task<IReadOnlyList<GameProfile>> LoadAsync(CancellationToken ct = default)
    {
        QuarantinedFile = null;
        if (!File.Exists(_path)) return Array.Empty<GameProfile>();
        try { return JsonSerializer.Deserialize<List<GameProfile>>(await File.ReadAllTextAsync(_path, ct)) ?? new(); }
        catch (JsonException)
        {
            // Keep the unreadable file so the next save cannot silently destroy the user's profiles.
            QuarantinedFile = Path.Combine(Path.GetDirectoryName(_path)!, $"game-profiles.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            File.Move(_path, QuarantinedFile, true);
            return Array.Empty<GameProfile>();
        }
    }

    public async Task SaveAsync(IEnumerable<GameProfile> profiles, CancellationToken ct = default)
    {
        var safe = profiles.Select(x => x with { GameProcess = Path.GetFileName(x.GameProcess), ExecutablePath = NormalizeOptionalPath(x.ExecutablePath) }).ToArray();
        var temp = _path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, safe, new JsonSerializerOptions { WriteIndented = true }, ct);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, _path, true);
    }

    private static string? NormalizeOptionalPath(string? path) => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
}
