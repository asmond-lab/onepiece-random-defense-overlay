using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CoachNavigationGuidanceTests
{
    private readonly BeginnerCoachPlanner _planner = new(new DataCatalog());
    private const string Bounty = "PathOfKings.BountyHunter";

    [Theory]
    [InlineData(18, false, false, false)]
    [InlineData(19, true, false, false)]
    [InlineData(20, false, false, false)]
    [InlineData(21, false, true, false)]
    [InlineData(22, false, true, false)]
    [InlineData(23, false, true, false)]
    [InlineData(24, false, true, true)]
    [InlineData(25, false, true, true)]
    public void TimingSeparatesPreparationSelectionAndActualSelectionConfirmation(
        int round, bool tip, bool confirmation, bool actualChoice)
    {
        var frame = Frame() with { Round = round };
        var decision = _planner.Decide(frame);
        Assert.Equal(tip, decision.ShowBountyHunterPreparationTip);
        Assert.Equal(confirmation, decision.RequiresUserConfirmation);
        Assert.Equal(actualChoice, decision.RequiresNavigationChoice);
        Assert.Equal(round is >= 21 and <= 23 ? Bounty : null, decision.NavigationOptionId);
        Assert.Equal(confirmation, decision.Kind == CoachActionKind.Navigation);
        Assert.Null(frame.ConfirmedNavigation);
        var advice = AutomaticNavigationAdvisor.Select(new DataCatalog(),
            [new InventoryEntry { UnitId = "material" }], [], round, 1, null);
        Assert.Equal(round is >= 21 and <= 23, advice!.CanSelectNow);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("AlliedForces.DoubleBenefit", null, false)]
    [InlineData(Bounty, null, true)]
    [InlineData(Bounty, Bounty, false)]
    [InlineData(Bounty, "AlliedForces.DoubleBenefit", false)]
    [InlineData(null, Bounty, false)]
    public void OnlyTheUnconfirmedBountySuggestionEnablesPreparation(string? suggested, string? confirmed, bool tip)
    {
        var decision = _planner.Decide(Frame() with
            { SuggestedNavigation = suggested, ConfirmedNavigation = confirmed });
        Assert.Equal(tip, decision.ShowBountyHunterPreparationTip);
        Assert.False(decision.RequiresUserConfirmation);
        Assert.Null(decision.NavigationOptionId);
    }

    [Fact]
    public void AdvisoryDoesNotReplaceOrChangeThePrimaryAction()
    {
        var frame = Frame();
        var normal = _planner.Decide(frame with { SuggestedNavigation = null });
        var advised = _planner.Decide(frame);
        Assert.True(advised.ShowBountyHunterPreparationTip);
        Assert.Equal(normal.Id, advised.Id);
        Assert.Equal(normal.Kind, advised.Kind);
        Assert.Equal(normal.TargetUnitId, advised.TargetUnitId);
        Assert.Equal(normal.IsUrgent, advised.IsUrgent);
        Assert.Equal(normal.Controls, advised.Controls);
        Assert.Equal(normal.Reason, advised.Reason);
        Assert.Equal(normal.Confirmation, advised.Confirmation);
        Assert.Equal(normal.PreservedMaterialCounts, advised.PreservedMaterialCounts);
        Assert.False(advised.RequiresUserConfirmation);
        Assert.Null(advised.NavigationOptionId);
    }

    [Theory]
    [MemberData(nameof(SafetyHolds))]
    public void SafetyAndRewardHoldsSuppressPreparation(CoachFrame frame, CoachActionKind expected)
    {
        var decision = _planner.Decide(frame);
        Assert.Equal(expected, decision.Kind);
        Assert.False(decision.ShowBountyHunterPreparationTip);
    }

    public static IEnumerable<object[]> SafetyHolds()
    {
        var frame = Frame();
        yield return [frame with { IsCurrent = false }, CoachActionKind.Recognition];
        yield return [frame with { Paused = true }, CoachActionKind.Waiting];
        yield return [frame with { Outcome = "fail" }, CoachActionKind.Finished];
        yield return [frame with { Inventory = ImmutableDictionary<string, int>.Empty }, CoachActionKind.Waiting];
        yield return [frame with { Mode = PlayMode.Guide }, CoachActionKind.Waiting];
        yield return [frame with { Signals = ImmutableDictionary<string, long?>.Empty.Add("line-count", 70) }, CoachActionKind.Maintain];
        foreach (var priority in new[] { 0, 1 })
            yield return [frame with { Recommendations = [Recommendation(priority)] }, CoachActionKind.Craft];
        foreach (var action in new[] { StorySequenceAction.WaitForStoryReward, StorySequenceAction.PushStoryForRareReward })
            yield return [frame with { Story = Story(action) }, CoachActionKind.Story];
        foreach (var (action, wisp) in new[]
        {
            (StorySequenceAction.SpendStoryWisps, "e016"),
            (StorySequenceAction.SpendStoryWisps, "e017"),
            (StorySequenceAction.SpendRareWisps, "e019")
        })
            yield return [frame with { Story = Story(action), RewardWisps = ImmutableDictionary<string, int>.Empty.Add(wisp, 1) }, CoachActionKind.Reward];
    }

    [Fact]
    public void SuggestionChangesAndRecognitionRecoveryCannotLeaveStaleAdvice()
    {
        var session = new BeginnerCoachSession(new DataCatalog());
        var frame = Frame();
        Assert.True(session.Update(frame).ShowBountyHunterPreparationTip);
        Assert.False(session.Update(frame with { Revision = 2, IsCurrent = false }).ShowBountyHunterPreparationTip);
        Assert.False(session.Update(frame with { Revision = 3, SuggestedNavigation = null }).ShowBountyHunterPreparationTip);
        Assert.True(session.Update(frame with { Revision = 4 }).ShowBountyHunterPreparationTip);
        Assert.False(session.Update(frame with { Revision = 5, Round = 20 }).ShowBountyHunterPreparationTip);
    }

    private static CoachFrame Frame() => BeginnerCoachPlannerTests.ReadyFrame() with
        { Round = 19, ConfirmedNavigation = null, SuggestedNavigation = Bounty };

    private static Recommendation Recommendation(int priority) => new()
    {
        Route = new RouteDefinition { Id = "craft:goal", GoalUnitId = "goal", Name = "goal" },
        CurrentCraft = new CurrentCraftAssessment(true, priority, new GoalStrategyProfile(0, 0))
    };

    private static StoryRewardSequenceDecision Story(StorySequenceAction action) => new(
        RecommendationSequenceStage.StoryReward, action, "", "", "", "", "", null, null, 0, false);
}
