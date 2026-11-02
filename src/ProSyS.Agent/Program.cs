namespace ProSyS.Agent;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // One agent per user session; a second launch simply exits.
        using var instance = new Mutex(true, @"Local\ProSyS.Agent", out var isFirst);
        if (!isFirst) return 0;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new AgentContext(args.Contains("--startup", StringComparer.OrdinalIgnoreCase)));
        return 0;
    }
}
