using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BeginnerCoachPlannerTests
{
    private readonly DataCatalog _catalog = new();

    [Fact]
    public void TransientRecognitionNeverIssuesAnIrreversibleCraft()
    {
        var decision = new BeginnerCoachPlanner(_catalog).Decide(ReadyFrame() with { IsCurrent = false });
        Assert.Equal(CoachActionKind.Recognition, decision.Kind);
        Assert.Null(decision.TargetUnitId);
    }

    [Fact]
    public void NavigationWindowInterruptsNonUrgentCraftUntilConfirmed()
    {
        var frame = ReadyFrame() with
        {
            Round = 21, SuggestedNavigation = "PathOfKings.BountyHunter", ConfirmedNavigation = null
        };
        var planner = new BeginnerCoachPlanner(_catalog);
        var decision = planner.Decide(frame);
        Assert.Equal(CoachActionKind.Navigation, decision.Kind);
        Assert.Equal(frame.SuggestedNavigation, decision.NavigationOptionId);
        Assert.True(decision.RequiresUserConfirmation);
        Assert.NotEqual(CoachActionKind.Navigation,
            planner.Decide(frame with { ConfirmedNavigation = frame.SuggestedNavigation }).Kind);
    }

    [Fact]
    public void StoryDeadlineInterruptsAnUnrelatedCraft()
    {
        var frame = ReadyFrame() with { Round = 34, CompletedStoryStage = 12 };
        Assert.Equal(CoachActionKind.Story, new BeginnerCoachPlanner(_catalog).Decide(frame).Kind);
    }

    [Fact]
    public void TerminalOutcomeDoesNotBecomeAnotherBuildRecommendation()
    {
        Assert.Equal(CoachActionKind.Finished,
            new BeginnerCoachPlanner(_catalog).Decide(ReadyFrame() with { Outcome = "fail" }).Kind);
    }

    [Fact]
    public void MissingAndZeroCraftResourcesBothRequireEconomyDecision()
    {
        var units = (Dictionary<string, UnitDefinition>)_catalog.UnitsById;
        units["wood"] = new UnitDefinition { Id = "wood", Name = "목재", Tier = "자원", Rawcodes = ["LUMBER"] };
        units["goal"] = new UnitDefinition { Id = "goal", Name = "목표", Recipe = new() { ["wood"] = 10 } };
        var planner = new BeginnerCoachPlanner(_catalog);
        Assert.Equal(CoachActionKind.Economy, planner.Decide(ReadyFrame()).Kind);
        Assert.Equal(CoachActionKind.Economy, planner.Decide(ReadyFrame() with
        {
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 0)
        }).Kind);
    }

    [Fact]
    public void LateUnconfirmedNavigationRequiresActualSelectionRatherThanAssumingForcedDefault()
    {
        var decision = new BeginnerCoachPlanner(_catalog).Decide(ReadyFrame() with
        {
            Round = 24, ConfirmedNavigation = null, SuggestedNavigation = "AlliedForces.DoubleBenefit"
        });
        Assert.Equal(CoachActionKind.Navigation, decision.Kind);
        Assert.True(decision.RequiresNavigationChoice);
        Assert.Null(decision.NavigationOptionId);
    }

    [Fact]
    public void UnsupportedResourceCannotBeTreatedAsFree()
    {
        var units = (Dictionary<string, UnitDefinition>)_catalog.UnitsById;
        units["random"] = new UnitDefinition { Id = "random", Name = "특수 자원", Tier = "자원", Rawcodes = ["RANDOM"] };
        units["goal"] = new UnitDefinition { Id = "goal", Name = "목표", Recipe = new() { ["random"] = 1 } };
        Assert.Equal(CoachActionKind.Economy, new BeginnerCoachPlanner(_catalog).Decide(ReadyFrame()).Kind);
    }

    [Fact]
    public void CleanupCannotSellMaterialsNeededByRemainingSupportPlan()
    {
        var units = (Dictionary<string, UnitDefinition>)_catalog.UnitsById;
        units["goal"] = new UnitDefinition { Id = "goal", Name = "목표" };
        units["rare"] = new UnitDefinition { Id = "rare", Name = "필요 희귀", Tier = "희귀함" };
        units["support"] = new UnitDefinition
            { Id = "support", Name = "필수 보완", Recipe = new() { ["rare"] = 1 } };
        var frame = ReadyFrame() with
        {
            CraftSteps = [], Inventory = ImmutableDictionary<string, int>.Empty.Add("goal", 1).Add("rare", 1),
            Rerolls = [new RareRerollAdvice("rare", "필요 희귀", 1, 0, 1, "", Sell: true)],
            Recommendations = [new Recommendation { Route = new RouteDefinition
                { Id = "craft:support", GoalUnitId = "support", Name = "필수 보완" } }]
        };
        Assert.NotEqual(CoachActionKind.Economy, new BeginnerCoachPlanner(_catalog).Decide(frame).Kind);
    }

    [Fact]
    public void SafeDismantleAdviceBecomesOneActionButGoalMaterialIsProtected()
    {
        var units = (Dictionary<string, UnitDefinition>)_catalog.UnitsById;
        units["goal"] = new UnitDefinition { Id = "goal", Name = "목표" };
        units["special"] = new UnitDefinition { Id = "special", Name = "특수함", Tier = "특수함" };
        var frame = ReadyFrame() with
        {
            CraftSteps = [], Inventory = ImmutableDictionary<string, int>.Empty.Add("special", 1),
            Dismantles = [new SpecialDismantleAdvice("special", "특수함", true, "재료 필요")]
        };
        var planner = new BeginnerCoachPlanner(_catalog);
        Assert.Equal(CoachActionKind.Economy, planner.Decide(frame).Kind);
        units["goal"] = new UnitDefinition
            { Id = "goal", Name = "목표", Recipe = new() { ["special"] = 1 } };
        Assert.NotEqual(CoachActionKind.Economy, planner.Decide(frame).Kind);
    }

    [Fact]
    public void GreenBloodPlanRequiresObservedItemAndOwnedRecipient()
    {
        var units = (Dictionary<string, UnitDefinition>)_catalog.UnitsById;
        units["goal"] = new UnitDefinition { Id = "goal", Name = "목표" };
        units["legend"] = new UnitDefinition { Id = "legend", Name = "전설", Tier = "전설" };
        var frame = ReadyFrame() with
        {
            CraftSteps = [], Inventory = ImmutableDictionary<string, int>.Empty.Add("legend", 1),
            GreenBlood = [new GreenBloodAdvice("legend", "전설", "지원 보강", null)]
        };
        var planner = new BeginnerCoachPlanner(_catalog);
        Assert.NotEqual(CoachActionKind.Item, planner.Decide(frame).Kind);
        Assert.Equal(CoachActionKind.Item, planner.Decide(frame with { GreenBloodAvailable = true }).Kind);
        Assert.NotEqual(CoachActionKind.Item, planner.Decide(frame with
            { GreenBloodAvailable = true, Inventory = ImmutableDictionary<string, int>.Empty }).Kind);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0L, false)]
    [InlineData(10L, true)]
    public void UpgradeRequiresKnownAffordableCost(long? gold, bool upgrade)
    {
        var frame = ReadyFrame() with
        {
            CraftSteps = [], Inventory = ImmutableDictionary<string, int>.Empty.Add("goal", 1),
            Signals = ImmutableDictionary<string, long?>.Empty
                .Add("gold", gold).Add("upgrade-level", 0).Add("upgrade-cost", 10),
            Recommendations = [new Recommendation
            {
                Route = new RouteDefinition { Id = "support", GoalUnitId = "support", Name = "지원" },
                CombatReadiness = new CombatReadiness(ReadinessDamageType.Physical, 0, 0, 0, 0, 0, 0, 0, 0)
            }]
        };
        Assert.Equal(upgrade, new BeginnerCoachPlanner(_catalog).Decide(frame).Kind == CoachActionKind.Upgrade);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void RewardActionRequiresAnObservedWisp(int count, bool reward)
    {
        var frame = ReadyFrame() with
        {
            Story = new StoryRewardSequenceDecision(RecommendationSequenceStage.RareReward,
                StorySequenceAction.SpendRareWisps, "", "", "", "", "", null, null, 0, false),
            RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e019", count)
        };
        Assert.Equal(reward, new BeginnerCoachPlanner(_catalog).Decide(frame).Kind == CoachActionKind.Reward);
    }

    [Fact]
    public void PauseStopsActionsWithoutInventingAnAcquiredUnit()
    {
        var decision = new BeginnerCoachPlanner(_catalog).Decide(ReadyFrame() with { Paused = true });
        Assert.Equal(CoachActionKind.Waiting, decision.Kind);
        Assert.Null(decision.TargetUnitId);
    }

    [Fact]
    public void MissingAutomaticGoalCannotLeakTheSavedDefaultGoalCraft()
    {
        var decision = new BeginnerCoachPlanner(_catalog).Decide(ReadyFrame() with { GoalId = null });
        Assert.Equal(CoachActionKind.Waiting, decision.Kind);
        Assert.Null(decision.TargetUnitId);
    }

    [Fact]
    public void EmptyMidgameHandDoesNotReuseAPreviousCraftAction()
    {
        var decision = new BeginnerCoachPlanner(_catalog).Decide(ReadyFrame() with
            { Inventory = ImmutableDictionary<string, int>.Empty });
        Assert.Equal(CoachActionKind.Waiting, decision.Kind);
        Assert.Null(decision.TargetUnitId);
    }

    [Fact]
    public void ObservedRareRewardCanBeSpentBeforeAnAutomaticGoalExists()
    {
        var decision = new BeginnerCoachPlanner(_catalog).Decide(ReadyFrame() with
        {
            Round = 20, GoalId = null, Story = null,
            RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e019", 1)
        });
        Assert.Equal(CoachActionKind.Reward, decision.Kind);
        Assert.Null(decision.TargetUnitId);
    }

    internal static CoachFrame ReadyFrame() => new()
    {
        MatchGeneration = 1, Revision = 1, Round = 26, CompletedStoryStage = 13,
        IsCurrent = true, Difficulty = "악몽", GoalId = "goal",
        Inventory = ImmutableDictionary<string, int>.Empty.Add("material", 1),
        ConfirmedNavigation = "PathOfKings.BountyHunter",
        CraftSteps = [new AutoCombineStep("goal", "목표", "material", "재료", "", "Z", [])]
    };
}
