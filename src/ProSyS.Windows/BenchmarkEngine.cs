using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using ProSyS.Core;

namespace ProSyS.Windows;

public sealed class BenchmarkEngine
{
    public const string ExpectedSha256 = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";
    private readonly string _presentMonPath;
    private readonly string _dataRoot;

    public BenchmarkEngine(string presentMonPath, string dataRoot) { _presentMonPath = presentMonPath; _dataRoot = dataRoot; }

    public ToolTrustResult VerifyTool()
    {
        if (!File.Exists(_presentMonPath)) return new(false, "PresentMon binary is missing.", null, null);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_presentMonPath)));
        if (!hash.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase)) return new(false, "PresentMon SHA-256 does not match the pinned 2.6.0 binary.", hash, null);
        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(_presentMonPath));
            var intelSigned = certificate.Subject.Contains("Intel Corporation", StringComparison.OrdinalIgnoreCase);
            // The pinned SHA-256 is the trust anchor. The certificate subject is only read from the file; the Authenticode chain is not validated here.
            return new(intelSigned, intelSigned ? "Pinned SHA-256 matches the official PresentMon 2.6.0 release; embedded signer is Intel Corporation." : "Unexpected embedded signer.", hash, certificate.Subject);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException) { return new(false, "Authenticode verification failed: " + ex.Message, hash, null); }
    }

    public async Task<BenchmarkSummary> CaptureAsync(string processName, int durationSeconds, MachineSnapshot machine, CancellationToken ct = default)
    {
        var trust = VerifyTool();
        if (!trust.Trusted) throw new InvalidOperationException(trust.Message);
        if (!Regex.IsMatch(processName, "^[A-Za-z0-9_.-]{1,128}$")) throw new ArgumentException("Process name contains unsupported characters.", nameof(processName));
        durationSeconds = Math.Clamp(durationSeconds, 5, 600);
        var folder = Path.Combine(_dataRoot, "Benchmarks");
        Directory.CreateDirectory(folder);
        var csv = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(processName)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        var start = new ProcessStartInfo(_presentMonPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "--process_name", processName, "--timed", durationSeconds.ToString(CultureInfo.InvariantCulture), "--terminate_after_timed", "--v1_metrics", "--output_file", csv, "--no_console_stats" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PresentMon could not be started.");
        // Drain both pipes so a chatty PresentMon can never block on a full buffer.
        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        _ = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"PresentMon exited with code {process.ExitCode}: {error.Trim()} " +
            "Frame capture needs membership in Performance Log Users (the installer adds you; sign out and back in once) or running ProSyS as administrator.");
        return ParseCsv(csv, machine, processName, durationSeconds);
    }

    public BenchmarkSummary ParseCsv(string csvPath, MachineSnapshot machine, string processName, int durationSeconds = 0)
    {
        var lines = File.ReadLines(csvPath).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (lines.Count < 3) throw new InvalidDataException("PresentMon capture does not contain enough frames.");
        var headers = SplitCsv(lines[0]);
        var frameIndex = FindColumn(headers, "MsBetweenPresents", "CPUFrameTime", "DisplayedTime");
        if (frameIndex < 0) throw new InvalidDataException("No supported frame-time column exists in the PresentMon CSV.");
        var frames = new List<double>(lines.Count - 1);
        foreach (var line in lines.Skip(1))
        {
            var fields = SplitCsv(line);
            if (frameIndex < fields.Count && double.TryParse(fields[frameIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var frame) && frame > 0 && frame < 1000) frames.Add(frame);
        }
        if (frames.Count < 30) throw new InvalidDataException("Fewer than 30 valid frames were captured.");
        frames.Sort();
        var fps = frames.Select(x => 1000d / x).OrderBy(x => x).ToArray();
        var averageFps = 1000d / frames.Average();
        var oneCount = Math.Max(1, (int)Math.Ceiling(fps.Length * 0.01));
        var pointOneCount = Math.Max(1, (int)Math.Ceiling(fps.Length * 0.001));
        var mean = frames.Average();
        var deviation = Math.Sqrt(frames.Sum(x => Math.Pow(x - mean, 2)) / frames.Count);
        var fingerprint = new BenchmarkFingerprint(processName, machine.WindowsBuild.ToString(CultureInfo.InvariantCulture),
            string.Join(";", machine.Gpus.Select(x => x.DriverVersion)), machine.PowerPlan, machine.Id, durationSeconds);
        return new(Guid.NewGuid(), DateTimeOffset.UtcNow, "PresentMon 2.6.0", processName, frames.Count, averageFps,
            fps.Take(oneCount).Average(), fps.Take(pointOneCount).Average(), Percentile(frames, 50), Percentile(frames, 99), deviation, fingerprint, csvPath);
    }

    public static BenchmarkComparison Compare(BenchmarkSummary before, BenchmarkSummary after)
    {
        var sameGame = before.Fingerprint.GameProcess.Equals(after.Fingerprint.GameProcess, StringComparison.OrdinalIgnoreCase);
        var sameEnvironment = before.Fingerprint.WindowsBuild == after.Fingerprint.WindowsBuild && before.Fingerprint.GpuDriver == after.Fingerprint.GpuDriver;
        var comparability = !sameGame ? BenchmarkComparability.Invalid : sameEnvironment ? BenchmarkComparability.High : BenchmarkComparability.Low;
        var reason = !sameGame ? "Game process changed." : sameEnvironment ? "Core environment fingerprint matches." : "Windows build or GPU driver changed.";
        var average = Delta(before.AverageFps, after.AverageFps);
        var low = Delta(before.OnePercentLowFps, after.OnePercentLowFps);
        var p99 = Delta(before.P99FrameTimeMs, after.P99FrameTimeMs);
        var noise = Math.Max(before.StandardDeviationMs, after.StandardDeviationMs);
        var meaningful = comparability == BenchmarkComparability.High && average > 1.0 && Math.Abs(after.MedianFrameTimeMs - before.MedianFrameTimeMs) > noise * 0.15;
        return new(comparability, reason, average, low, p99, meaningful);
    }

    public const int MinimumRunsPerSide = 3;

    /// <summary>
    /// Compares repeated runs before and after a change. At least <see cref="MinimumRunsPerSide"/> runs per side are required;
    /// a change is reported only when the Welch 95% confidence interval of the difference excludes zero, and regressions are reported as such.
    /// </summary>
    public static BenchmarkSetComparison CompareSets(IReadOnlyList<BenchmarkSummary> before, IReadOnlyList<BenchmarkSummary> after)
    {
        if (before.Count < MinimumRunsPerSide || after.Count < MinimumRunsPerSide)
            return new(BenchmarkComparability.Invalid, $"At least {MinimumRunsPerSide} runs are needed on each side (have {before.Count} before, {after.Count} after).",
                before.Count, after.Count, null, null, BenchmarkVerdict.InsufficientRuns);
        var all = before.Concat(after).ToList();
        var first = all[0].Fingerprint;
        if (all.Any(x => !x.Fingerprint.GameProcess.Equals(first.GameProcess, StringComparison.OrdinalIgnoreCase)))
            return new(BenchmarkComparability.Invalid, "Runs belong to different games.", before.Count, after.Count, null, null, BenchmarkVerdict.NotComparable);
        if (all.Any(x => x.Fingerprint.DurationSeconds != first.DurationSeconds))
            return new(BenchmarkComparability.Invalid, "Runs have different capture durations.", before.Count, after.Count, null, null, BenchmarkVerdict.NotComparable);
        var sameEnvironment = all.All(x => x.Fingerprint.WindowsBuild == first.WindowsBuild && x.Fingerprint.GpuDriver == first.GpuDriver);
        var average = Welch(before.Select(x => x.AverageFps).ToList(), after.Select(x => x.AverageFps).ToList());
        var low = Welch(before.Select(x => x.OnePercentLowFps).ToList(), after.Select(x => x.OnePercentLowFps).ToList());
        var verdict = !sameEnvironment ? BenchmarkVerdict.NotComparable
            : average.Verdict == BenchmarkVerdict.Regression || low.Verdict == BenchmarkVerdict.Regression ? BenchmarkVerdict.Regression
            : average.Verdict;
        return new(sameEnvironment ? BenchmarkComparability.High : BenchmarkComparability.Low,
            sameEnvironment ? "Game, capture duration, Windows build and GPU driver match." : "Windows build or GPU driver changed between runs.",
            before.Count, after.Count, average, low, verdict);
    }

    private static MetricChange Welch(IReadOnlyList<double> before, IReadOnlyList<double> after)
    {
        double Mean(IReadOnlyList<double> x) => x.Average();
        double Variance(IReadOnlyList<double> x) { var m = Mean(x); return x.Sum(v => (v - m) * (v - m)) / (x.Count - 1); }
        var (m1, m2) = (Mean(before), Mean(after));
        var (a, b) = (Variance(before) / before.Count, Variance(after) / after.Count);
        var se = Math.Sqrt(a + b);
        var df = se == 0 ? double.PositiveInfinity : Math.Pow(a + b, 2) / (a * a / (before.Count - 1) + b * b / (after.Count - 1));
        var margin = StudentT975(df) * se;
        var diff = m2 - m1;
        double Percent(double value) => m1 == 0 ? 0 : value * 100d / m1;
        var verdict = diff - margin > 0 ? BenchmarkVerdict.Improvement : diff + margin < 0 ? BenchmarkVerdict.Regression : BenchmarkVerdict.NoSignificantChange;
        return new(m1, m2, Percent(diff), Percent(diff - margin), Percent(diff + margin), verdict);
    }

    /// <summary>Two-sided 95% critical value of Student's t for (possibly fractional) degrees of freedom.</summary>
    internal static double StudentT975(double df)
    {
        double[] table = { 12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228, 2.201, 2.179, 2.160, 2.145, 2.131,
            2.120, 2.110, 2.101, 2.093, 2.086, 2.080, 2.074, 2.069, 2.064, 2.060, 2.056, 2.052, 2.048, 2.045, 2.042 };
        if (double.IsNaN(df) || df < 1) df = 1;
        if (df <= table.Length)
        {
            var low = (int)Math.Floor(df);
            var high = Math.Min(low + 1, table.Length);
            return table[low - 1] + (table[high - 1] - table[low - 1]) * (df - low);
        }
        // Cornish–Fisher expansion around the normal quantile; accurate to about 0.001 above 30 degrees of freedom.
        const double z = 1.959964;
        return z + (Math.Pow(z, 3) + z) / (4 * df) + (5 * Math.Pow(z, 5) + 16 * Math.Pow(z, 3) + 3 * z) / (96 * df * df);
    }

    private static double Delta(double before, double after) => before == 0 ? 0 : (after - before) * 100d / before;
    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        var index = (sorted.Count - 1) * percentile / 100d;
        var low = (int)Math.Floor(index);
        var high = (int)Math.Ceiling(index);
        return low == high ? sorted[low] : sorted[low] + (sorted[high] - sorted[low]) * (index - low);
    }

    private static int FindColumn(IReadOnlyList<string> headers, params string[] names)
    {
        foreach (var name in names)
        {
            var index = headers.ToList().FindIndex(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return index;
        }
        return -1;
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }
}

public sealed record ToolTrustResult(bool Trusted, string Message, string? Sha256, string? Signer);
