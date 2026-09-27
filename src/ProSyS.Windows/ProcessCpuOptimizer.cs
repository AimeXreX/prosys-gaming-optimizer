using System.ComponentModel;
using System.Diagnostics;

namespace ProSyS.Windows;

/// <summary>
/// Raises a game to Windows Above Normal priority only (never High or Realtime, which can starve input, audio and driver threads)
/// and restores the exact original priority. The priority-boost setting is left untouched.
/// </summary>
public sealed class ProcessCpuOptimizer
{
    private const int AccessDenied = 5;

    public CpuOptimizationSession Apply(Process process)
    {
        process.Refresh();
        if (process.HasExited) throw new InvalidOperationException("The game process has already exited.");
        try
        {
            var originalPriority = process.PriorityClass;
            if (originalPriority is ProcessPriorityClass.Idle or ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Normal)
                process.PriorityClass = ProcessPriorityClass.AboveNormal;
            return new(process, originalPriority, process.TotalProcessorTime, DateTimeOffset.UtcNow);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == AccessDenied)
        {
            // Anti-cheat protected games (and elevated processes) deny PROCESS_SET_INFORMATION; that is expected, not an error to work around.
            throw new InvalidOperationException("Windows denied access to this game's process, usually because anti-cheat protects it. The game runs unchanged at its own priority.", ex);
        }
    }
}

public sealed class CpuOptimizationSession
{
    private readonly Process _process;
    private readonly ProcessPriorityClass _originalPriority;
    private TimeSpan _lastCpu;
    private DateTimeOffset _lastSample;

    internal CpuOptimizationSession(Process process, ProcessPriorityClass originalPriority, TimeSpan cpu, DateTimeOffset sample)
    {
        _process = process; _originalPriority = originalPriority; _lastCpu = cpu; _lastSample = sample;
    }

    public int ProcessId => _process.Id;
    public string AppliedMode => "Above normal priority (High and Realtime are never used; priority boost unchanged)";

    public double SampleCpuPercent()
    {
        if (_process.HasExited) return 0;
        _process.Refresh();
        var now = DateTimeOffset.UtcNow;
        var cpu = _process.TotalProcessorTime;
        var elapsed = (now - _lastSample).TotalMilliseconds;
        var used = (cpu - _lastCpu).TotalMilliseconds;
        _lastSample = now; _lastCpu = cpu;
        return elapsed <= 0 ? 0 : Math.Clamp(used / elapsed / Environment.ProcessorCount * 100d, 0, 100);
    }

    public bool TryRestore()
    {
        try
        {
            if (_process.HasExited) return true;
            _process.PriorityClass = _originalPriority;
            return _process.PriorityClass == _originalPriority;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return _process.HasExited; }
    }
}
