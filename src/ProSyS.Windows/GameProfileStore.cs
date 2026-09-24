using System.Text.Json;
using ProSyS.Core;

namespace ProSyS.Windows;

public sealed class GameProfileStore
{
    private readonly string _path;
    public GameProfileStore(string dataRoot) { Directory.CreateDirectory(dataRoot); _path = Path.Combine(dataRoot, "game-profiles.json"); }
    public async Task<IReadOnlyList<GameProfile>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return Array.Empty<GameProfile>();
        try { return JsonSerializer.Deserialize<List<GameProfile>>(await File.ReadAllTextAsync(_path, ct)) ?? new(); }
        catch (JsonException) { return Array.Empty<GameProfile>(); }
    }
    public async Task SaveAsync(IEnumerable<GameProfile> profiles, CancellationToken ct = default)
    {
        var safe = profiles.Select(x => x with { GameProcess = Path.GetFileName(x.GameProcess), ExecutablePath = NormalizeOptionalPath(x.ExecutablePath) }).ToArray();
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(safe, new JsonSerializerOptions { WriteIndented = true }), ct);
        File.Move(temp, _path, true);
    }
    private static string? NormalizeOptionalPath(string? path) => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
}
