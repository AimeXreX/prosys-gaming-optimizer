using System.Diagnostics;

namespace ProSyS.Windows;

/// <summary>Raises a game only to Windows High priority (never Realtime) and restores the exact process state.</summary>
public sealed class ProcessCpuOptimizer
{
    public CpuOptimizationSession Apply(Process process)
    {
        process.Refresh();
        if (process.HasExited) throw new InvalidOperationException("The game process has already exited.");
        var originalPriority = process.PriorityClass;
        var originalBoost = process.PriorityBoostEnabled;
        process.PriorityClass = ProcessPriorityClass.High;
        process.PriorityBoostEnabled = true;
        return new(process, originalPriority, originalBoost, process.TotalProcessorTime, DateTimeOffset.UtcNow);
    }
}

public sealed class CpuOptimizationSession
{
    private readonly Process _process;
    private readonly ProcessPriorityClass _originalPriority;
    private readonly bool _originalBoost;
    private TimeSpan _lastCpu;
    private DateTimeOffset _lastSample;

    internal CpuOptimizationSession(Process process, ProcessPriorityClass originalPriority, bool originalBoost, TimeSpan cpu, DateTimeOffset sample)
    {
        _process = process; _originalPriority = originalPriority; _originalBoost = originalBoost; _lastCpu = cpu; _lastSample = sample;
    }

    public int ProcessId => _process.Id;
    public string AppliedMode => "High priority + Windows priority boost (Realtime is never used)";

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
            _process.PriorityBoostEnabled = _originalBoost;
            _process.PriorityClass = _originalPriority;
            return _process.PriorityClass == _originalPriority && _process.PriorityBoostEnabled == _originalBoost;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return _process.HasExited; }
    }
}
