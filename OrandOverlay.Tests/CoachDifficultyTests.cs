using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CoachDifficultyTests
{
    [Fact]
    public void UnobservedDifficultyCannotSelectFromNightmareEvidence()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("yamato_transcendent");
        var policy = new BeginnerGoalPolicy(catalog, ClearBuildStats.FromSamples(Samples(goal, "악몽")));
        Assert.Empty(policy.EligibleGoals);
        Assert.Null(policy.Select([new InventoryEntry { UnitId = "rawcode:S20h" }]));
    }

    [Theory]
    [InlineData("쉬움")]
    [InlineData("보통")]
    [InlineData("어려움")]
    [InlineData("지옥")]
    public void ExactDifficultyStatisticsRetainLowerDifficultySamples(string difficulty)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("yamato_transcendent");
        var stats = ClearBuildStats.FromSamples(Samples(goal, difficulty));
        Assert.False(stats.HasData); // Legacy aggregate remains God+ only.
        Assert.Equal(12, stats.ForDifficulty(difficulty).GoalProfile(goal.Rawcodes)?.SampleCount);
        Assert.Null(stats.ForDifficulty("악몽").GoalProfile(goal.Rawcodes));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("쉬움")]
    [InlineData("보통")]
    [InlineData("어려움")]
    [InlineData("지옥")]
    public void CoachDoesNotOfferGreenBloodOutsideConfirmedGodPlus(string difficulty)
    {
        var catalog = new DataCatalog();
        var units = (Dictionary<string, UnitDefinition>)catalog.UnitsById;
        units["goal"] = new UnitDefinition { Id = "goal", Name = "목표" };
        units["legend"] = new UnitDefinition { Id = "legend", Name = "전설", Tier = "전설" };
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with
        {
            Difficulty = difficulty, CraftSteps = [], GreenBloodAvailable = true,
            Inventory = ImmutableDictionary<string, int>.Empty.Add("legend", 1),
            GreenBlood = [new GreenBloodAdvice("legend", "전설", "지원 보강", null)]
        };
        Assert.NotEqual(CoachActionKind.Item, new BeginnerCoachPlanner(catalog).Decide(frame).Kind);
    }

    [Theory]
    [InlineData("쉬움", 40)]
    [InlineData("보통", 50)]
    [InlineData("어려움", 65)]
    [InlineData("지옥", 65)]
    [InlineData("신", 65)]
    [InlineData("악몽", 65)]
    [InlineData("unknown", null)]
    [InlineData("", null)]
    [InlineData("unrecognized", null)]
    public void CompletedStoryMilestoneUsesObservedClearRound(string difficulty, int? clearRound)
    {
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with { Difficulty = difficulty };
        var decision = new BeginnerCoachPlanner(new DataCatalog()).Decide(frame);
        Assert.Equal(clearRound, frame.ClearRound);
        Assert.Equal(clearRound, decision.MilestoneRound);
        Assert.Equal(clearRound.HasValue, frame.HasKnownDifficulty);
    }

    [Theory]
    [InlineData("쉬움")]
    [InlineData("보통")]
    [InlineData("어려움")]
    [InlineData("지옥")]
    [InlineData("신")]
    [InlineData("악몽")]
    [InlineData("unknown")]
    public void StoryDeadlineAndLineLimitsRemainNightmareOnly(string difficulty)
    {
        var planner = new BeginnerCoachPlanner(new DataCatalog());
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with
            { Difficulty = difficulty, Round = 34, CompletedStoryStage = 12 };
        var story = planner.Decide(frame);
        Assert.Equal(difficulty == "악몽", story.Kind == CoachActionKind.Story);
        Assert.Equal(difficulty == "악몽" ? (int?)35 : null, story.MilestoneRound);
        var line = planner.Decide(frame with
            { Signals = ImmutableDictionary<string, long?>.Empty.Add("line-count", 70) });
        Assert.Equal(difficulty == "악몽", line.Id == "line-survival");
    }

    [Theory]
    [InlineData("쉬움")]
    [InlineData("보통")]
    [InlineData("어려움")]
    [InlineData("지옥")]
    [InlineData("신")]
    public void ChangingDifficultyReplacesUnbuiltCommitmentAndScopesStatistics(string difficulty)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var nightmare = catalog.Unit("yamato_transcendent");
        var selected = catalog.Unit("rawcode:H90H");
        var stats = ClearBuildStats.FromSamples(Samples(nightmare, "악몽").Concat(Samples(selected, difficulty)));
        var policy = new BeginnerGoalPolicy(catalog, stats, "악몽");
        InventoryEntry[] hand = [new() { UnitId = "rawcode:S20h" }];
        Assert.Equal(nightmare.Id, policy.Select(hand)?.Id);
        policy.UpdateContext(stats, difficulty);
        Assert.Null(policy.CommittedGoalId);
        Assert.Equal([selected.Id], policy.RouteGoalIds);
        Assert.Equal(0, policy.Samples(nightmare));
        Assert.Equal(12, policy.Samples(selected));
        Assert.Equal(selected.Id, policy.Select(hand)?.Id);
        policy.UpdateContext(stats, difficulty);
        Assert.Equal(selected.Id, policy.CommittedGoalId);
        policy.UpdateContext(stats, "unknown");
        Assert.Empty(policy.EligibleGoals);
        Assert.Null(policy.Select(hand));
        Assert.Equal(nightmare.Id, policy.Select([new InventoryEntry { UnitId = nightmare.Id }])?.Id);
    }

    [Fact]
    public void RefreshedSamplesUnlockCandidatesWithoutChangingACommittedGoal()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("yamato_transcendent");
        var policy = new BeginnerGoalPolicy(catalog, ClearBuildStats.Empty, "신");
        Assert.Empty(policy.EligibleGoals);
        var stats = ClearBuildStats.FromSamples(Samples(goal, "신"));
        policy.UpdateContext(stats, "신");
        Assert.Equal(goal.Id, policy.Select([new InventoryEntry { UnitId = "rawcode:S20h" }])?.Id);
        policy.UpdateContext(ClearBuildStats.FromSamples(Samples(goal, "신")), "신");
        Assert.Equal(goal.Id, policy.CommittedGoalId);
    }

    internal static IEnumerable<ClearSample> Samples(UnitDefinition goal, string difficulty) =>
        Enumerable.Range(0, 12).Select(index => new ClearSample(
            $"{difficulty}-{goal.Id}-{index}", DateTimeOffset.UnixEpoch, difficulty, 1,
            [new ClearSampleUnit(goal.Rawcodes[0], 1, goal.Tier)]));
}
