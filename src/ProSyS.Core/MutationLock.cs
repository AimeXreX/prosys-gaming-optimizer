namespace ProSyS.Core;

/// <summary>
/// Serializes system mutations across ProSyS processes (GUI and CLI). A lock file opened with FileShare.None is used
/// instead of a named Mutex because a Mutex is thread-affine and cannot be released safely across awaits; the OS
/// releases the file handle automatically if the process crashes.
/// </summary>
public sealed class MutationLock : IDisposable
{
    private readonly FileStream _handle;
    private MutationLock(FileStream handle) => _handle = handle;

    public static MutationLock Acquire(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        var path = Path.Combine(dataRoot, "mutation.lock");
        try { return new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)); }
        catch (IOException ex) { throw new InvalidOperationException("Another ProSyS operation is changing system settings. Wait for it to finish and try again.", ex); }
    }

    public void Dispose() => _handle.Dispose();
}
