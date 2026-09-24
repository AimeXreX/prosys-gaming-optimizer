using System.Text.Json;
using ProSyS.Core;

namespace ProSyS.Windows;

public sealed class JsonAuditLog : IAuditLog
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public JsonAuditLog(string path) { _path = path; Directory.CreateDirectory(Path.GetDirectoryName(path)!); }
    public async Task WriteAsync(object entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, entry });
            await File.AppendAllTextAsync(_path, line + Environment.NewLine, cancellationToken);
        }
        finally { _gate.Release(); }
    }
}
