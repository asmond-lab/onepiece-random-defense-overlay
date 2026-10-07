using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RewardAwareCoachTests
{
    private readonly DataCatalog _catalog = new();

    [Theory]
    [InlineData(StorySequenceAction.WaitForStoryReward, 0)]
    [InlineData(StorySequenceAction.WaitForStoryReward, 1)]
    [InlineData(StorySequenceAction.WaitForStoryReward, 3)]
    [InlineData(StorySequenceAction.PushStoryForRareReward, 0)]
    [InlineData(StorySequenceAction.PushStoryForRareReward, 1)]
    [InlineData(StorySequenceAction.PushStoryForRareReward, 3)]
    public void RewardWaitBlocksEveryCraftPriority(StorySequenceAction action, int priority)
    {
        var decision = new BeginnerCoachPlanner(_catalog).Decide(Frame(action, priority));
        Assert.Equal(CoachActionKind.Story, decision.Kind);
        Assert.True(decision.CraftDeferredForReward);
        Assert.Null(decision.TargetUnitId);
    }

    [Theory]
    [InlineData(StorySequenceAction.SpendStoryWisps, "e016", 0)]
    [InlineData(StorySequenceAction.SpendStoryWisps, "e016", 1)]
    [InlineData(StorySequenceAction.SpendStoryWisps, "e017", 0)]
    [InlineData(StorySequenceAction.SpendStoryWisps, "e017", 1)]
    [InlineData(StorySequenceAction.SpendStoryWisps, "e017", 3)]
    [InlineData(StorySequenceAction.SpendStoryWisps, "e016", 3)]
    [InlineData(StorySequenceAction.SpendRareWisps, "e019", 0)]
    [InlineData(StorySequenceAction.SpendRareWisps, "e019", 1)]
    [InlineData(StorySequenceAction.SpendRareWisps, "e019", 3)]
    public void ObservedApplicableRewardBlocksEveryCraftPriority(StorySequenceAction action, string id, int priority)
    {
        var frame = Frame(action, priority) with { RewardWisps = ImmutableDictionary<string, int>.Empty.Add(id, 2) };
        var decision = new BeginnerCoachPlanner(_catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Reward, decision.Kind);
        Assert.True(decision.CraftDeferredForReward);
        Assert.Equal(id, decision.RewardWispId);
        Assert.Null(decision.TargetUnitId);
    }

    [Theory]
    [InlineData(StorySequenceAction.SpendStoryWisps, "e019")]
    [InlineData(StorySequenceAction.SpendStoryWisps, "unrelated")]
    [InlineData(StorySequenceAction.SpendRareWisps, "e016")]
    [InlineData(StorySequenceAction.SpendRareWisps, "e017")]
    [InlineData(StorySequenceAction.SpendRareWisps, "unrelated")]
    public void UnrelatedObservedTypesDoNotAuthorizeSpending(StorySequenceAction action, string id)
    {
        var decision = new BeginnerCoachPlanner(_catalog).Decide(Frame(action, 3) with
            { RewardWisps = ImmutableDictionary<string, int>.Empty.Add(id, 2) });
        Assert.Equal(CoachActionKind.Craft, decision.Kind);
        Assert.False(decision.CraftDeferredForReward);
        Assert.Null(decision.RewardWispId);
    }

    [Theory]
    [InlineData(StorySequenceAction.SpendStoryWisps, "e016")]
    [InlineData(StorySequenceAction.SpendStoryWisps, "e017")]
    [InlineData(StorySequenceAction.SpendRareWisps, "e019")]
    public void NavigationSummonCandidatesCannotChangeRewardAdvice(StorySequenceAction action, string id)
    {
        var frame = Frame(action, 3) with { RewardWisps = ImmutableDictionary<string, int>.Empty.Add(id, 1) };
        var planner = new BeginnerCoachPlanner(_catalog);
        Assert.Equal(planner.Decide(frame), planner.Decide(frame with
            { Wisps = [new EmergencySummonAdvice("navigation-only", "navigation-only", 9, "")] }));
    }

    [Theory]
    [InlineData(StorySequenceAction.WaitForStoryReward)]
    [InlineData(StorySequenceAction.PushStoryForRareReward)]
    [InlineData(StorySequenceAction.SpendStoryWisps)]
    [InlineData(StorySequenceAction.SpendRareWisps)]
    public void StaleRecognitionNeverAuthorizesRewardConsumption(StorySequenceAction action)
    {
        var decision = new BeginnerCoachPlanner(_catalog).Decide(Frame(action, 0) with
        {
            IsCurrent = false,
            RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e016", 1).Add("e019", 1)
        });
        Assert.Equal(CoachActionKind.Recognition, decision.Kind);
        Assert.Null(decision.TargetUnitId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void ImmediateLegendAndNoRewardCraftPoliciesArePreserved(int priority)
    {
        var frame = Frame(StorySequenceAction.CraftLegendNow, priority);
        var planner = new BeginnerCoachPlanner(_catalog);
        Assert.Equal(CoachActionKind.Craft, planner.Decide(frame).Kind);
        Assert.Equal(CoachActionKind.Craft, planner.Decide(frame with { Story = null }).Kind);
    }

    [Theory]
    [InlineData(StorySequenceAction.SpendStoryWisps, "e016")]
    [InlineData(StorySequenceAction.SpendStoryWisps, "e017")]
    [InlineData(StorySequenceAction.SpendRareWisps, "e019")]
    public void ZeroApplicableWispsDoNotBlockCraft(StorySequenceAction action, string id)
    {
        var decision = new BeginnerCoachPlanner(_catalog).Decide(Frame(action, 0) with
            { RewardWisps = ImmutableDictionary<string, int>.Empty.Add(id, 0) });
        Assert.Equal(CoachActionKind.Craft, decision.Kind);
        Assert.False(decision.CraftDeferredForReward);
    }

    [Fact]
    public void NonCraftPrioritiesSurviveRewardBlocking()
    {
        var frame = Frame(StorySequenceAction.WaitForStoryReward, 0);
        var planner = new BeginnerCoachPlanner(_catalog);
        Assert.Equal(CoachActionKind.Navigation, planner.Decide(frame with { Round = 21, ConfirmedNavigation = null }).Kind);
        Assert.Equal(CoachActionKind.Navigation, planner.Decide(frame with { Round = 24, ConfirmedNavigation = null }).Kind);
        Assert.Equal(CoachActionKind.Finished, planner.Decide(frame with { Outcome = "clear" }).Kind);
        Assert.Equal(CoachActionKind.Waiting, planner.Decide(frame with { Paused = true }).Kind);
        Assert.Equal(CoachActionKind.Maintain, planner.Decide(frame with
            { Signals = ImmutableDictionary<string, long?>.Empty.Add("line-count", 70) }).Kind);
        Assert.Equal(CoachActionKind.Story, planner.Decide(frame with { Round = 34, CompletedStoryStage = 12 }).Kind);
    }

    private static CoachFrame Frame(StorySequenceAction action, int priority) => BeginnerCoachPlannerTests.ReadyFrame() with
    {
        Story = new StoryRewardSequenceDecision(RecommendationSequenceStage.StoryReward,
            action, "", "", "", "", "", null, null, 0, false),
        Recommendations = [new Recommendation
        {
            Route = new RouteDefinition { Id = "craft:goal", GoalUnitId = "goal", Name = "goal" },
            CurrentCraft = new CurrentCraftAssessment(true, priority, new GoalStrategyProfile(0, 0))
        }]
    };
}
