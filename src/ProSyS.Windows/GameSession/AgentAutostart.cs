using System.Diagnostics;
using Microsoft.Win32;

namespace ProSyS.Windows.GameSession;

/// <summary>Per-user autostart for the background agent. The Run value always points at the exact agent executable; nothing machine-wide is written.</summary>
public static class AgentAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ProSySAgent";
    public const string ExecutableName = "ProSyS.Agent.exe";

    public static string DefaultAgentPath => Path.Combine(AppContext.BaseDirectory, ExecutableName);

    public static bool IsEnabled(string agentPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string value && value.Contains(agentPath, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(string agentPath, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled) key.SetValue(RunValue, $"\"{agentPath}\" --startup", RegistryValueKind.String);
        else key.DeleteValue(RunValue, false);
    }

    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName));
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    public static bool TryStart(string agentPath)
    {
        if (IsRunning()) return true;
        if (!File.Exists(agentPath)) return false;
        Process.Start(new ProcessStartInfo(agentPath) { UseShellExecute = true })?.Dispose();
        return true;
    }
}
