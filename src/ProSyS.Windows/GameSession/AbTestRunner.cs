using ProSyS.Core;

namespace ProSyS.Windows.GameSession;

public sealed record AbTestResult(IReadOnlyList<BenchmarkSummary> Baseline, IReadOnlyList<BenchmarkSummary> Optimized, BenchmarkSetComparison Comparison);

/// <summary>
/// Measures whether the game-session boosts help this game on this PC. Captures alternate A-B, B-A, … so thermal drift and
/// shader-cache warm-up do not systematically favour either side; the verdict comes from <see cref="BenchmarkEngine.CompareSets"/>
/// (Welch 95% confidence interval), so "Improvement" is only reported when the difference is larger than the run-to-run noise.
/// </summary>
public sealed class AbTestRunner
{
    private readonly BenchmarkEngine _engine;
    public AbTestRunner(BenchmarkEngine engine) => _engine = engine;

    /// <summary>True = optimized (B) capture. Pairs alternate their order: A B, B A, A B, …</summary>
    public static bool[] CaptureOrder(int runsPerSide) =>
        Enumerable.Range(0, Math.Clamp(runsPerSide, 1, 10)).SelectMany(pair => pair % 2 == 0 ? new[] { false, true } : new[] { true, false }).ToArray();

    public async Task<AbTestResult> RunAsync(string processName, int secondsPerCapture, int runsPerSide, MachineSnapshot snapshot,
        Func<CancellationToken, Task> applyOptimized, Func<CancellationToken, Task> restoreBaseline, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var order = CaptureOrder(runsPerSide);
        var baseline = new List<BenchmarkSummary>();
        var optimized = new List<BenchmarkSummary>();
        try
        {
            for (var i = 0; i < order.Length; i++)
            {
                if (order[i]) await applyOptimized(ct); else await restoreBaseline(ct);
                progress?.Report($"{i + 1}/{order.Length} {(order[i] ? "B" : "A")}");
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                var capture = await _engine.CaptureAsync(processName, secondsPerCapture, snapshot, ct);
                if (order[i]) optimized.Add(capture); else baseline.Add(capture with { IsBaseline = true });
            }
        }
        finally { await restoreBaseline(CancellationToken.None); }
        return new(baseline, optimized, BenchmarkEngine.CompareSets(baseline, optimized));
    }
}
