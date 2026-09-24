using System.Net;
using System.Text;
using ProSyS.Core;

namespace ProSyS.Windows;

public static class ReportExporter
{
    public static async Task ExportHtmlAsync(string path, MachineSnapshot snapshot, OptimizationPlan plan, CancellationToken ct = default)
    {
        static string H(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "Unknown");
        var rows = new StringBuilder();
        foreach (var item in plan.Tweaks)
            rows.Append($"<tr><td>{H(item.Name)}</td><td>{H(item.Current.Value ?? item.Current.Status)}</td><td>{H(item.Risk.Level)}</td><td>{H(item.Benefit)}</td><td>{H(item.Compatibility.Status)}</td><td>{(item.Selected ? "Selected" : "Optional")}</td></tr>");
        var html = $$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
        <title>ProSyS system report</title><style>body{font:14px Segoe UI,Arial;background:#0b1220;color:#e8edf6;margin:0;padding:32px}main{max-width:1100px;margin:auto}h1{color:#66e3c4}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}.card,table{background:#121d2d;border:1px solid #263852;border-radius:10px}.card{padding:16px}table{width:100%;border-collapse:collapse;margin-top:20px;overflow:hidden}th,td{text-align:left;padding:10px;border-bottom:1px solid #263852}th{color:#7fc7ff}.muted{color:#aab8cc}</style></head>
        <body><main><h1>ProSyS Gaming Optimizer</h1><p class="muted">Local report generated {{H(DateTimeOffset.Now)}} • Plan {{H(plan.PlanId)}} • SHA-256 {{H(plan.Sha256)}}</p>
        <section class="cards"><div class="card"><b>Windows</b><br>{{H(snapshot.WindowsEdition)}} {{H(snapshot.WindowsVersion)}}<br>Build {{snapshot.WindowsBuild}}</div>
        <div class="card"><b>CPU</b><br>{{H(snapshot.Cpu)}}<br>{{snapshot.LogicalProcessors}} logical processors</div>
        <div class="card"><b>Memory</b><br>{{snapshot.TotalMemoryBytes / 1073741824d:F1}} GB</div>
        <div class="card"><b>Graphics</b><br>{{H(string.Join(", ", snapshot.Gpus.Select(x => x.Name)))}}</div></section>
        <h2>Capabilities</h2><table><thead><tr><th>Capability</th><th>Current</th><th>Risk</th><th>Benefit</th><th>Compatibility</th><th>Plan</th></tr></thead><tbody>{{rows}}</tbody></table>
        <p class="muted">No guaranteed performance gain is claimed. Keep changes only after a repeatable benchmark.</p></main></body></html>
        """;
        await File.WriteAllTextAsync(path, html, Encoding.UTF8, ct);
    }
}
