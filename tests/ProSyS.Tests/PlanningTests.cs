using ProSyS.Core;
using Xunit;

namespace ProSyS.Tests;

public class PlanningTests
{
    [Fact]
    public void SafePolicyAcceptsReversibleEvidenceBackedDefaults() =>
        Assert.True(new RiskEngine().AllowedInSafeProfile(TestData.Metadata("a")));

    [Theory]
    [InlineData(EvidenceType.Legacy, true)]
    [InlineData(EvidenceType.Experimental, true)]
    [InlineData(EvidenceType.GenerallySupportedBehavior, false)]
    public void SafePolicyRejectsWeakEvidenceAndOptInPreferences(EvidenceType evidence, bool recommended) =>
        Assert.False(new RiskEngine().AllowedInSafeProfile(TestData.Metadata("a", evidence: evidence, recommended: recommended)));

    [Fact]
    public void DependencyOrderIsHonored()
    {
        var order = new DependencyPlanner().Order(new[] { new FakeTweak(TestData.Metadata("b", after: new[] { "a" })), new FakeTweak(TestData.Metadata("a")) });
        Assert.Equal(new[] { "a", "b" }, order.Select(x => x.Metadata.Id));
    }

    [Fact]
    public void DependencyCyclesAreRejected() =>
        Assert.Throws<InvalidOperationException>(() => new DependencyPlanner().Order(new[]
        {
            new FakeTweak(TestData.Metadata("a", after: new[] { "b" })), new FakeTweak(TestData.Metadata("b", after: new[] { "a" }))
        }));

    [Fact]
    public async Task PlanIsReadOnlyAndHashed()
    {
        var plan = await new PlanFactory().CreateAsync(TestData.Snapshot(), new[] { new FakeTweak(TestData.Metadata("a")) });
        Assert.Equal(64, plan.Sha256.Length);
        Assert.True(((ICollection<PlannedTweak>)plan.Tweaks).IsReadOnly);
        Assert.True(new PlanFactory().HasValidHash(plan));
    }

    [Fact]
    public async Task SelectionCreatesANewHashedPlan()
    {
        var factory = new PlanFactory();
        var plan = await factory.CreateAsync(TestData.Snapshot(), new[] { new FakeTweak(TestData.Metadata("a")), new FakeTweak(TestData.Metadata("b")) });
        var selected = factory.Select(plan, new HashSet<string>(new[] { "b" }, StringComparer.OrdinalIgnoreCase));
        Assert.NotEqual(plan.PlanId, selected.PlanId);
        Assert.NotEqual(plan.Sha256, selected.Sha256);
        Assert.True(selected.Tweaks.Single(x => x.TweakId == "b").Selected);
        Assert.False(selected.Tweaks.Single(x => x.TweakId == "a").Selected);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 255 }, "Binary")]
    [InlineData(new[] { "one", "two" }, "MultiString")]
    [InlineData(5000000000L, "QWord")]
    [InlineData(-3, "DWord")]
    [InlineData("%TEMP%", "ExpandString")]
    public void CanonicalTextMatchesAfterJsonRoundTrip(object value, string kind)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToElement(value);
        Assert.Equal(ValueText.Canonical(value, kind), ValueText.Canonical(json, kind));
    }
}
