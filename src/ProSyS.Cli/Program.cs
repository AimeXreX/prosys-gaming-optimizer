using System.Text.Json;
using ProSyS.Core;
using ProSyS.Windows;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProSySOptimizer");
Directory.CreateDirectory(root);
var scanner = new WindowsSystemScanner();
var tweaks = TweakCatalog.CreateSafeTweaks();
var log = new JsonAuditLog(Path.Combine(root, "Logs", "audit.jsonl"));

if (args.Length == 0 || args[0] is "help" or "--help")
{
    Console.WriteLine("ProSyS Gaming Optimizer CLI\n\n  scan\n  audit [--json]\n  list-tweaks [--json]\n  recovery list\n  plan\n  optimize --profile safe --confirm\n  restore last\n");
    return;
}

switch (args[0].ToLowerInvariant())
{
    case "scan":
        Print(await scanner.ScanAsync());
        break;
    case "audit":
    {
        var snapshot = await scanner.ScanAsync();
        var plan = await new PlanFactory().CreateAsync(snapshot, tweaks);
        if (args.Contains("--json", StringComparer.OrdinalIgnoreCase)) Print(new { snapshot, plan });
        else PrintPlan(plan);
        break;
    }
    case "list-tweaks":
        if (args.Contains("--json", StringComparer.OrdinalIgnoreCase))
            Print(tweaks.Select(x => x.Metadata));
        else
        {
            Console.WriteLine($"{tweaks.Count} reversible capabilities across {tweaks.Select(x => x.Metadata.Category).Distinct().Count()} categories.");
            foreach (var tweak in tweaks) Console.WriteLine($"{tweak.Metadata.Id,-55} {tweak.Metadata.Category,-24} default={tweak.Metadata.RecommendedByDefault,-5} {tweak.Metadata.Name}");
        }
        break;
    case "recovery" when args.Length > 1 && args[1].Equals("list", StringComparison.OrdinalIgnoreCase):
        Print(new OptimizationEngine(root, log).FindIncompleteSessions());
        break;
    case "plan":
    {
        var plan = await new PlanFactory().CreateAsync(await scanner.ScanAsync(), tweaks);
        PrintPlan(plan);
        break;
    }
    case "optimize":
    {
        var profileIndex = Array.FindIndex(args, x => x.Equals("--profile", StringComparison.OrdinalIgnoreCase));
        var safeProfile = profileIndex >= 0 && profileIndex + 1 < args.Length && args[profileIndex + 1].Equals("safe", StringComparison.OrdinalIgnoreCase);
        if (!safeProfile || !args.Contains("--confirm", StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("No changes made. Review `prosys plan`, then repeat with --profile safe --confirm.");
            Environment.ExitCode = 2;
            break;
        }
        var snapshot = await scanner.ScanAsync();
        var safe = tweaks.Where(x => new RiskEngine().AllowedInSafeProfile(x.Metadata)).ToList();
        var plan = await new PlanFactory().CreateAsync(snapshot, safe);
        var summary = await new OptimizationEngine(root, log).ExecuteAsync(plan, safe);
        Print(summary);
        Environment.ExitCode = summary.State == OperationState.Completed ? 0 : 1;
        break;
    }
    case "restore" when args.Length > 1 && args[1].Equals("last", StringComparison.OrdinalIgnoreCase):
    {
        var backupRoot = Path.Combine(root, "Backups");
        var latest = Directory.Exists(backupRoot) ? new DirectoryInfo(backupRoot).GetDirectories().OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault()?.FullName : null;
        if (latest is null) { Console.Error.WriteLine("No backup session exists."); Environment.ExitCode = 2; break; }
        Print(await new OptimizationEngine(root, log).RollbackAsync(latest, tweaks));
        break;
    }
    default:
        Console.Error.WriteLine("Unknown command. Run with --help.");
        Environment.ExitCode = 2;
        break;
}

static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
static void PrintPlan(OptimizationPlan plan)
{
    Console.WriteLine($"Plan {plan.PlanId} | Snapshot {plan.MachineSnapshotId} | SHA-256 {plan.Sha256}");
    foreach (var item in plan.Tweaks)
        Console.WriteLine($"[{(item.Selected ? 'x' : ' ')}] {item.Name} | {item.Risk.Level} | current={item.Current.Value ?? "not set"} | {item.Compatibility.Status}");
    Console.WriteLine("Dry run complete. No system state was changed.");
}
