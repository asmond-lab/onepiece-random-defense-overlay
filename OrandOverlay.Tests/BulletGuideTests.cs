using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideTests
{
    private readonly DataCatalog _catalog = new();
    public BulletGuideTests() => _catalog.Load(loadCarryPolicy: false);

    [Fact]
    public void WarcuryControlStageDisplaysEvaluatedArmorTarget()
    {
        var plan = new BulletGuidePolicy(_catalog).Plan(55, 13,
            Inventory("180h", "U30h", "540h"), "악몽", gorosei: GoroseiMode.Warcury);
        Assert.Equal(BulletGuideStage.ControlSupport, plan.Stage);
        Assert.Equal(120, plan.Support!.ArmorTarget);
        Assert.Contains("오라깎 120", BulletGuideAdvice.Stage(plan));
    }

    [Fact]
    public void DuplicateOwnedAirUnitsCountAsTwoMobileBodies()
    {
        var plan = Plan(25, 12, "930h", "930h", "MC0h");
        Assert.Equal(2, plan.AirCount);
        Assert.NotEqual(BulletGuideStage.AirFoundation, plan.Stage);
    }

    [Fact]
    public void TwoCurrentKingsDoNotRemainAtSecondLegendStage()
    {
        var plan = Plan(17, 9, "HA0h", "HA0h");
        Assert.Equal(2, plan.AirCount);
        Assert.NotEqual(BulletGuideStage.SecondLegend, plan.Stage);
    }

    [Fact]
    public void CurrentDuplicatesAndMissingHistoricalTypeFormKnownLegendLowerBound()
    {
        var plan = new BulletGuidePolicy(_catalog).Plan(20, 9, Inventory("HA0h", "HA0h"), "악몽",
            observedLegendIds: ["rawcode:930h", "rawcode:930h"]);
        Assert.Equal(BulletGuideStage.FourthLegendReward, plan.Stage);
    }

    [Fact]
    public void DuplicateHistoryEntriesDoNotInventAdditionalLegends()
    {
        var plan = new BulletGuidePolicy(_catalog).Plan(17, 9, Inventory("HA0h"), "악몽",
            observedLegendIds: ["rawcode:HA0h", "rawcode:HA0h"]);
        Assert.Equal(BulletGuideStage.SecondLegend, plan.Stage);
    }

    [Fact]
    public void GuideOneIsRegisteredAndDoesNotChangeManualGoal()
    {
        var settings = new AppSettings { Mode = PlayMode.Guide, ManualGoalUnitId = "rawcode:F40h" };
        PlayModes.Normalize(settings);
        Assert.Equal(1, settings.GuideNumber);
        Assert.Equal("rawcode:180h", GuideCatalog.Find(1)!.GoalId);
        Assert.Equal("rawcode:F40h", settings.ManualGoalUnitId);
        Assert.False(PlayModes.AutomaticGoals(PlayMode.Guide));
    }

    [Fact]
    public void OpeningUsesSourceOrderWhenCandidatesHaveEqualMaterialsAfterQuestWindow()
    {
        // T000 is now the first stage; retain the original legend tie-break after its deadline.
        var plan = Plan(8, 3);
        Assert.Equal(BulletGuideStage.FirstLegend, plan.Stage);
        Assert.Equal("rawcode:530h", plan.TargetUnitId);
    }

    [Fact]
    public void ThirdLegendWithKingChoosesShikiForAir()
    {
        var plan = Plan(17, 9, "HA0h", "MC0h");
        Assert.Equal(BulletGuideStage.AirFoundation, plan.Stage);
        Assert.Equal("rawcode:930h", plan.TargetUnitId);
    }

    [Fact]
    public void ThirdLegendWithShikiChoosesRedForce()
    {
        Assert.Equal("rawcode:U30h", Plan(17, 9, "930h", "MC0h").TargetUnitId);
    }

    [Fact]
    public void FourthLegendWaitsForPunkHazardRatherThanInventingReward()
    {
        var plan = Plan(20, 9, "HA0h", "MC0h", "930h");
        Assert.Equal(BulletGuideStage.FourthLegendReward, plan.Stage);
        Assert.Null(plan.TargetUnitId);
    }

    [Theory]
    [InlineData(35)]
    [InlineData(49)]
    public void CompleteRecipeIsNotOwnedBulletOrPermissionToCraftEarly(int round)
    {
        var plan = Plan(round, 13, "U20h", "V20h", "930h", "U30h", "540h");
        Assert.False(plan.OwnedBullet);
        Assert.NotEqual("rawcode:180h", plan.TargetUnitId);
        Assert.Contains("rawcode:U20h", plan.ProtectedUnitIds);
    }

    [Fact]
    public void ActuallyOwnedBulletBypassesUnfinishedOpeningEvenWithLateStory()
    {
        var plan = Plan(10, 0, "180h");
        Assert.True(plan.OwnedBullet);
        Assert.NotEqual(BulletGuideStage.FirstLegend, plan.Stage);
        Assert.NotEqual(BulletGuideStage.CraftBullet, plan.Stage);
        Assert.NotEqual("rawcode:180h", plan.TargetUnitId);
    }

    [Fact]
    public void PipelineProducesRealRecipeCandidatesForRegisteredGuide()
    {
        var plan = Plan(5, 3);
        var result = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(_catalog),
            Goal = _catalog.Unit("rawcode:180h"), Inventory = [],
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = "Unselected",
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽"
        });
        Assert.Equal(plan.TargetUnitId, Assert.Single(result.Recommendations).Route.GoalUnitId);
        Assert.NotNull(result.Recommendations[0].RecipeTree);
    }

    [Theory]
    [InlineData(false, false, "unknown", CoachActionKind.Recognition)]
    [InlineData(true, true, "unknown", CoachActionKind.Waiting)]
    [InlineData(false, false, "clear", CoachActionKind.Finished)]
    public void RegisteredGuideCannotBypassSafety(bool current, bool paused, string outcome, CoachActionKind expected)
    {
        var frame = Frame() with { IsCurrent = current, Paused = paused, Outcome = outcome };
        Assert.Equal(expected, new BeginnerCoachPlanner(_catalog).Decide(frame).Kind);
    }

    [Fact]
    public void RegisteredGuideWaitsForObservedRewardBeforeCraft()
    {
        var frame = Frame() with { RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e019", 1) };
        var result = new BeginnerCoachPlanner(_catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Reward, result.Kind);
        Assert.True(result.CraftDeferredForReward);
    }

    [Fact]
    public void RegisteredGuideDoesNotInventUpgradeResourceOrLevel()
    {
        var frame = Frame() with { Round = 60, Inventory = Inventory("180h"),
            GuidePlan = Plan(60, 13, "180h") };
        var result = new BeginnerCoachPlanner(_catalog).Decide(frame);
        Assert.NotEqual(CoachActionKind.Upgrade, result.Kind);
        Assert.NotEqual(CoachActionKind.Economy, result.Kind);
    }

    [Fact]
    public void SessionResetDoesNotCarryGuideProgressFromOwnedBullet()
    {
        var session = new BeginnerCoachSession(_catalog);
        session.Update(Frame() with { Inventory = Inventory("180h"), GuidePlan = Plan(60, 13, "180h") });
        var decision = session.Update(Frame() with { MatchGeneration = 2, Revision = 1, Round = 5,
            GuidePlan = Plan(5, 3), ConfirmedNavigation = null });
        Assert.Equal("", decision.CompletionNotice);
        Assert.NotEqual(CoachActionKind.Upgrade, decision.Kind);
    }

    private BulletGuidePlan Plan(int round, int story, params string[] codes) =>
        new BulletGuidePolicy(_catalog).Plan(round, story, Inventory(codes), "악몽");
    private static ImmutableDictionary<string, int> Inventory(params string[] codes) =>
        codes.GroupBy(code => "rawcode:" + code).ToImmutableDictionary(group => group.Key, group => group.Count());
    private CoachFrame Frame() => new()
    {
        Mode = PlayMode.Guide, GuideNumber = 1, GuidePlan = Plan(25, 13),
        MatchGeneration = 1, Revision = 1, Round = 25, CompletedStoryStage = 13,
        IsCurrent = true, Inventory = Inventory("300h"), Difficulty = "악몽",
        GoalId = "rawcode:180h", ConfirmedNavigation = "PathOfKings.BountyHunter"
    };
}
