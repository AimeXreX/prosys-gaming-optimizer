using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;

// Deliberately short-lived and allowlisted: no arbitrary command, registry path or shell execution is accepted.
// Exit codes: 0 created and verified, 2 Windows reported an error, 3 Windows accepted the call but did not create a
// point (24-hour frequency limit), 4 System Restore / System Protection is off, 64 unsupported request.
const int BeginSystemChange = 100, EndSystemChange = 101, ApplicationInstall = 0;
const int ErrorServiceDisabled = 1058;

if (args is ["capabilities"])
{
    Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 2, capabilities = new[] { "create-restore-point" } }));
    return 0;
}
if (args is ["create-restore-point", var description] && description.Length is > 0 and <= 128)
{
    var begin = new RestorePointInfo { EventType = BeginSystemChange, RestorePointType = ApplicationInstall, SequenceNumber = 0, Description = description };
    if (!SRSetRestorePoint(ref begin, out var status) || status.Status != 0)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = status.Status }));
        return status.Status == ErrorServiceDisabled ? 4 : 2;
    }
    // Windows requires every BEGIN_SYSTEM_CHANGE to be closed with END_SYSTEM_CHANGE for the same sequence number.
    var end = new RestorePointInfo { EventType = EndSystemChange, RestorePointType = ApplicationInstall, SequenceNumber = status.SequenceNumber, Description = description };
    if (!SRSetRestorePoint(ref end, out var endStatus) || endStatus.Status != 0)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = endStatus.Status, stage = "end" }));
        return 2;
    }
    // Within the 24-hour frequency window Windows reports success without creating a point; confirm it exists.
    if (!RestorePointExists(status.SequenceNumber, description))
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = "not-created", sequence = status.SequenceNumber }));
        return 3;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, sequence = status.SequenceNumber }));
    return 0;
}
Console.Error.WriteLine("Unsupported operation.");
return 64;

static bool RestorePointExists(long sequence, string description)
{
    try
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\default"), new ObjectQuery("SELECT SequenceNumber, Description FROM SystemRestore"));
        foreach (var item in searcher.Get())
            using (item)
                if (Convert.ToInt64(item["SequenceNumber"]) == sequence && string.Equals(item["Description"] as string, description, StringComparison.Ordinal))
                    return true;
        return false;
    }
    catch (ManagementException) { return false; }
}

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
