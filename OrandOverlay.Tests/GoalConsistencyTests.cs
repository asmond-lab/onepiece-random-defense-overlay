using Xunit;

namespace OrandOverlay.Tests;

public sealed class GoalConsistencyTests
{
    private readonly DataCatalog _catalog = new();

    public GoalConsistencyTests() => _catalog.Load(loadCarryPolicy: false);

    [Fact]
    public void OwnedLawReplacesAnUnbuiltAutomaticGoalAndDoesNotOscillate()
    {
        var planned = _catalog.Unit("yamato_transcendent");
        var law = _catalog.Unit("rawcode:690H");
        var policy = new BeginnerGoalPolicy(_catalog,
            ClearBuildStats.FromSamples(CoachDifficultyTests.Samples(planned, "신")), "신");
        Assert.Equal(planned.Id, policy.Select([Entry("rawcode:S20h")])?.Id);

        var selected = policy.Select([Entry(law.Id)]);

        Assert.Equal(law.Id, selected?.Id);
        Assert.Equal(law.Id, policy.Select([Entry(planned.Id), Entry(law.Id)])?.Id);
        Assert.Equal(law.Id, policy.Select([Entry(planned.Id)])?.Id);
        policy.UpdateContext(ClearBuildStats.Empty, "unknown");
        Assert.Equal(law.Id, policy.Select([])?.Id);
        Assert.Equal([law.Id], policy.RouteGoalIds);
        var readiness = CombatReadinessCalculator.Calculate(_catalog, selected!, [Entry(law.Id)], "신");
        Assert.Equal(ReadinessDamageType.Magic, readiness.DamageType);
        Assert.Equal(0, readiness.RequiredArmorReduction);
        Assert.Equal(1, readiness.RequiredMagicArmorSources);
        policy.Reset();
        Assert.Null(policy.Select([]));
    }

    [Fact]
    public void OrdinaryObservationsCraftedProgressAndSampleRefreshRetainTheAutomaticGoal()
    {
        var goal = _catalog.Unit("rawcode:690H");
        var competing = _catalog.Unit("yamato_transcendent");
        var policy = new BeginnerGoalPolicy(_catalog,
            ClearBuildStats.FromSamples(CoachDifficultyTests.Samples(goal, "신")), "신");
        Assert.Equal(goal.Id, policy.Select([Entry("rawcode:S20h")])?.Id);

        foreach (var hand in new IReadOnlyList<InventoryEntry>[]
                 { [Entry("rawcode:S20h")], [Entry("rawcode:C30h")], [], [Entry("rawcode:830h")] })
        {
            policy.UpdateContext(ClearBuildStats.FromSamples(CoachDifficultyTests.Samples(competing, "신")), "신");
            Assert.Equal(goal.Id, policy.Select(hand)?.Id);
            Assert.Equal([goal.Id], policy.RouteGoalIds);
        }
    }

    [Theory]
    [InlineData(RecommendationSurface.StoryLegend)]
    [InlineData(RecommendationSurface.FastRare)]
    public void OwnedLawCannotFallBackToEarlyStoryPhysicalReadiness(RecommendationSurface initial)
    {
        var law = _catalog.Unit("rawcode:690H");
        var story = new StoryRewardSequenceDecision(RecommendationSequenceStage.FirstLegend,
            StorySequenceAction.BuildNearestLegend, "", "", "", "", "",
            "rawcode:Z90h", "", 0, false);
        var request = new RecommendationPipelineRequest
        {
            Engine = new RecommendationEngine(_catalog), Goal = law,
            Inventory = [Entry(law.Id), Entry("rawcode:S20h")],
            InitialSurface = initial, StorySequence = story, NavigationMode = "Unselected",
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "신"
        };

        var candidates = RecommendationPipeline.ComputeCandidates(request);
        var result = RecommendationPipeline.Finalize(candidates, _catalog, law, request.Inventory,
            new FirstRareTargetPolicy(), 0, 0, "신");

        Assert.Equal(RecommendationSurface.TopAndNavigation, result.Surface);
        Assert.Null(result.StorySequence);
        Assert.NotEmpty(result.Recommendations);
        Assert.All(result.Recommendations, item =>
        {
            Assert.Equal(ReadinessDamageType.Magic, item.CombatReadiness!.DamageType);
            Assert.Equal(0, item.CombatReadiness.RequiredArmorReduction);
            Assert.Equal(1, item.CombatReadiness.RequiredMagicArmorSources);
        });
    }

    [Fact]
    public void ExplicitManualTargetIsNotReplacedByAnUnselectedOwnedTop()
    {
        var manual = _catalog.Unit("yamato_transcendent");
        var inventory = new[] { Entry("rawcode:690H") };
        var plan = ManualGoalPlan.Create(_catalog, [manual.Id], inventory, null, "신");
        var result = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Mode = PlayMode.Manual, Engine = new RecommendationEngine(_catalog), Goal = manual,
            ManualPlan = plan, Inventory = inventory, InitialSurface = RecommendationSurface.TopAndNavigation,
            NavigationMode = "Unselected", Gorosei = GoroseiMode.None,
            BuildVariant = BuildVariants.AutoId, Difficulty = "신"
        });
        Assert.NotEmpty(result.Recommendations);
        Assert.All(result.Recommendations, item =>
            Assert.Equal(ReadinessDamageType.Physical, item.CombatReadiness!.DamageType));
    }

    private static InventoryEntry Entry(string id) => new() { UnitId = id, Count = 1 };
}
