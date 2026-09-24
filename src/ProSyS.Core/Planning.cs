using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProSyS.Core;

public sealed class RiskEngine
{
    public bool AllowedInSafeProfile(TweakMetadata metadata) =>
        metadata.Risk.Level <= RiskLevel.Low &&
        metadata.Risk.SecurityImpact == 0 &&
        metadata.Risk.StabilityRisk <= 1 &&
        metadata.Risk.Reversibility == Reversibility.Easy &&
        !metadata.AffectsSecurity &&
        metadata.Evidence is not (EvidenceType.Experimental or EvidenceType.Unknown);
}

public sealed class DependencyPlanner
{
    public IReadOnlyList<ITweak> Order(IEnumerable<ITweak> tweaks)
    {
        var selected = tweaks.ToDictionary(t => t.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var tweak in selected.Values)
        {
            var conflict = tweak.Metadata.ConflictsWith.FirstOrDefault(selected.ContainsKey);
            if (conflict is not null) throw new InvalidOperationException($"DependencyConflict: {tweak.Metadata.Id} conflicts with {conflict}.");
            var missing = tweak.Metadata.Requires.FirstOrDefault(id => !selected.ContainsKey(id));
            if (missing is not null) throw new InvalidOperationException($"DependencyConflict: {tweak.Metadata.Id} requires {missing}.");
        }

        var result = new List<ITweak>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(ITweak tweak)
        {
            if (visited.Contains(tweak.Metadata.Id)) return;
            if (!visiting.Add(tweak.Metadata.Id)) throw new InvalidOperationException("DependencyConflict: cycle detected.");
            foreach (var id in tweak.Metadata.MustRunAfter.Concat(tweak.Metadata.Requires))
                if (selected.TryGetValue(id, out var dependency)) Visit(dependency);
            visiting.Remove(tweak.Metadata.Id);
            visited.Add(tweak.Metadata.Id);
            result.Add(tweak);
        }
        foreach (var tweak in selected.Values.OrderBy(x => x.Metadata.Id, StringComparer.Ordinal)) Visit(tweak);
        return result;
    }
}

public sealed class PlanFactory
{
    public bool HasValidHash(OptimizationPlan plan) => string.Equals(plan.Sha256,
        ComputeHash(plan.PlanId, plan.CreatedAt, plan.MachineSnapshotId, plan.Tweaks), StringComparison.OrdinalIgnoreCase);

    public async Task<OptimizationPlan> CreateAsync(MachineSnapshot snapshot, IEnumerable<ITweak> tweaks, CancellationToken ct = default)
    {
        var ordered = new DependencyPlanner().Order(tweaks);
        var items = new List<PlannedTweak>();
        foreach (var tweak in ordered)
        {
            var detection = await tweak.DetectAsync(ct);
            var compatibility = await tweak.EvaluateCompatibilityAsync(snapshot, ct);
            var selected = tweak.Metadata.RecommendedByDefault && compatibility.Status == CompatibilityStatus.Compatible && detection.Status != DetectionStatus.Disabled;
            items.Add(new(tweak.Metadata.Id, tweak.Metadata.Name, detection, compatibility, tweak.Metadata.Risk,
                tweak.Metadata.Benefit, selected, $"Set {tweak.Metadata.Name} to the recommended state"));
        }
        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        var hash = ComputeHash(id, created, snapshot.Id, items);
        return new(id, created, snapshot.Id, items.AsReadOnly(), hash);
    }

    public OptimizationPlan Select(OptimizationPlan source, IReadOnlySet<string> selectedIds)
    {
        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        var items = source.Tweaks.Select(x => x with
        {
            Selected = selectedIds.Contains(x.TweakId) && x.Compatibility.Status == CompatibilityStatus.Compatible && x.Current.Status != DetectionStatus.Disabled
        }).ToList();
        return new(id, created, source.MachineSnapshotId, items.AsReadOnly(), ComputeHash(id, created, source.MachineSnapshotId, items));
    }

    internal static string ComputeHash(Guid id, DateTimeOffset created, string snapshotId, IReadOnlyList<PlannedTweak> items)
    {
        var canonical = JsonSerializer.Serialize(new { id, created, snapshotId, items });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
