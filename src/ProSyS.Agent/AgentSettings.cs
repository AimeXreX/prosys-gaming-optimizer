using System.Text.Json;

namespace ProSyS.Agent;

public sealed record AgentSettings(bool AutoOptimize = true, bool AutoDetectLibraryGames = true, bool Persian = true, bool Notifications = true)
{
    public static string PathIn(string dataRoot) => Path.Combine(dataRoot, "agent-settings.json");

    public static AgentSettings Load(string dataRoot)
    {
        try { return File.Exists(PathIn(dataRoot)) ? JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(PathIn(dataRoot))) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }

    public void Save(string dataRoot)
    {
        var temp = PathIn(dataRoot) + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, PathIn(dataRoot), true);
    }
}
