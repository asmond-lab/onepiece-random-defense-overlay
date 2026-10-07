using Xunit;

namespace OrandOverlay.Tests;

public sealed class BeginnerGoalPolicyTests
{
    [Fact]
    public void SwitchingBackToAutomaticKeepsAnAlreadyOwnedTop()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var existing = catalog.Unit("rawcode:F40h");
        var candidate = catalog.Unit("yamato_transcendent");
        var policy = new BeginnerGoalPolicy(catalog,
            ClearBuildStats.FromSamples(Samples(candidate, "악몽", 12)), "악몽");
        Assert.Equal(existing.Id, policy.Select([new InventoryEntry { UnitId = existing.Id }])?.Id);
    }

    [Fact]
    public void ControlFreeExpertBuildIsNotAnAutomaticBeginnerCandidate()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var gaban = catalog.Unit("rawcode:F40h");
        var sanji = catalog.Unit("rawcode:H90H");
        var policy = new BeginnerGoalPolicy(catalog, ClearBuildStats.FromSamples(
            Samples(gaban, "악몽", 50).Concat(Samples(sanji, "악몽", 12))), "악몽");
        Assert.DoesNotContain(policy.EligibleGoals, unit => unit.Id == gaban.Id);
        Assert.Contains(policy.EligibleGoals, unit => unit.Id == sanji.Id);
    }

    [Fact]
    public void SelectedNightmareRequiresItsSingleTopEvidenceRatherThanGodPopularity()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var yamato = catalog.Unit("yamato_transcendent");
        var gaban = catalog.Unit("rawcode:F40h");
        var stats = ClearBuildStats.FromSamples(
            Samples(yamato, "악몽", 12).Concat(Samples(gaban, "신", 100)));
        var selection = new BeginnerGoalPolicy(catalog, stats, "악몽").Select(
            [new InventoryEntry { UnitId = "rawcode:S20h" }]);
        Assert.Equal(yamato.Id, selection?.Id);
    }

    [Fact]
    public void MissingClearEvidenceDoesNotInventABeginnerBuild()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        Assert.Null(new BeginnerGoalPolicy(catalog, ClearBuildStats.Empty, "악몽").Select(
            [new InventoryEntry { UnitId = "rawcode:S20h" }]));
    }

    private static IEnumerable<ClearSample> Samples(UnitDefinition goal, string difficulty, int count) =>
        Enumerable.Range(0, count).Select(index => new ClearSample(
            $"{difficulty}-{goal.Id}-{index}", DateTimeOffset.UnixEpoch, difficulty, 1,
            [new ClearSampleUnit(goal.Rawcodes[0], 1, goal.Tier)]));

    [Fact]
    public void CommittedGoalSurvivesNewCardsUntilExplicitReset()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("yamato_transcendent");
        var policy = new BeginnerGoalPolicy(catalog, ClearBuildStats.FromSamples(Samples(goal, "악몽", 12)), "악몽");
        var first = policy.Select([new InventoryEntry { UnitId = "rawcode:S20h" }]);
        Assert.NotNull(first);
        Assert.Equal(first.Id, policy.Select([])?.Id);
        policy.Reset();
        Assert.Null(policy.CommittedGoalId);
        Assert.Null(policy.Select([]));
    }

    [Fact]
    public void MultiTopSamplesCannotMasqueradeAsSingleTopEvidence()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("yamato_transcendent");
        var other = catalog.Unit("rawcode:F40h");
        var samples = Samples(goal, "악몽", 20).Select(sample => sample with
        {
            Units = sample.Units.Append(new ClearSampleUnit(other.Rawcodes[0], 1, other.Tier)).ToArray()
        });
        Assert.Empty(new BeginnerGoalPolicy(catalog, ClearBuildStats.FromSamples(samples), "악몽").EligibleGoals);
    }

    [Fact]
    public void NavigationRoutePoolUsesOnlyTheCommittedBeginnerGoal()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var first = catalog.Unit("yamato_transcendent");
        var second = catalog.Unit("rawcode:H90H");
        var policy = new BeginnerGoalPolicy(catalog, ClearBuildStats.FromSamples(
            Samples(first, "악몽", 12).Concat(Samples(second, "악몽", 12))), "악몽");
        Assert.Equal(2, policy.RouteGoalIds.Count);
        var selected = policy.Select([new InventoryEntry { UnitId = "rawcode:S20h" }]);
        Assert.Equal([selected!.Id], policy.RouteGoalIds);
        policy.Reset();
        Assert.Equal(2, policy.RouteGoalIds.Count);
    }
}
