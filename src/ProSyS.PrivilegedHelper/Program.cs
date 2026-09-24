using System.Runtime.InteropServices;
using System.Text.Json;

// Deliberately short-lived and allowlisted: no arbitrary command, registry path or shell execution is accepted.
if (args is ["capabilities"])
{
    Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, capabilities = new[] { "create-restore-point" } }));
    return 0;
}
if (args is ["create-restore-point", var description] && description.Length is > 0 and <= 128)
{
    var info = new RestorePointInfo { EventType = 100, RestorePointType = 0, SequenceNumber = 0, Description = description };
    if (!SRSetRestorePoint(ref info, out var status) || status.Status != 0)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = status.Status }));
        return 2;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, sequence = status.SequenceNumber }));
    return 0;
}
Console.Error.WriteLine("Unsupported operation.");
return 64;

[DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern bool SRSetRestorePoint(ref RestorePointInfo restorePointInfo, out StateManagerStatus stateManagerStatus);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct RestorePointInfo
{
    public int EventType;
    public int RestorePointType;
    public long SequenceNumber;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
}
[StructLayout(LayoutKind.Sequential)] struct StateManagerStatus { public int Status; public long SequenceNumber; }
