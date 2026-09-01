using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class NavigationAutomaticRecommendationPolicyTests
{
    [Theory]
    [InlineData(NavigationRecommendationState.Actionable)]
    [InlineData(NavigationRecommendationState.Locked)]
    [InlineData(NavigationRecommendationState.SourceExpectedForced)]
    public void AppliesOnlyActionableOverlayRecommendations(
        NavigationRecommendationState state)
    {
        Assert.True(NavigationAutomaticRecommendationPolicy.ShouldApply(
            true, ManualLatches.None, PlannerPhase.Committed,
            state, "PathOfKings.BountyHunter"));
    }

    [Theory]
    [InlineData(NavigationRecommendationState.Waiting)]
    [InlineData(NavigationRecommendationState.Provisional)]
    [InlineData(NavigationRecommendationState.ManualOverride)]
    [InlineData(NavigationRecommendationState.NoSafeRecommendation)]
    public void DoesNotApplyNonActionableStates(NavigationRecommendationState state)
    {
        Assert.False(NavigationAutomaticRecommendationPolicy.ShouldApply(
            true, ManualLatches.None, PlannerPhase.Committed,
            state, "PathOfKings.BountyHunter"));
    }

    [Fact]
    public void DisabledOrManualModeNeverChangesTheOverlaySetting()
    {
        Assert.False(NavigationAutomaticRecommendationPolicy.ShouldApply(
            false, ManualLatches.None, PlannerPhase.Committed,
            NavigationRecommendationState.Actionable,
            "PathOfKings.BountyHunter"));
        Assert.False(NavigationAutomaticRecommendationPolicy.ShouldApply(
            true, new ManualLatches(false, true), PlannerPhase.Committed,
            NavigationRecommendationState.Actionable,
            "PathOfKings.BountyHunter"));
        Assert.False(NavigationAutomaticRecommendationPolicy.ShouldApply(
            true, ManualLatches.None, PlannerPhase.Committed,
            NavigationRecommendationState.Actionable, null));
    }

    [Theory]
    [InlineData(PlannerPhase.AccumulateSpecialUncommon)]
    [InlineData(PlannerPhase.ChooseLegend)]
    [InlineData(PlannerPhase.AwaitMarineford)]
    [InlineData(PlannerPhase.SpendRares)]
    [InlineData(PlannerPhase.CommitRound20)]
    public void DoesNotApplyBeforeStoryRewardSequenceCommits(PlannerPhase phase)
    {
        Assert.False(NavigationAutomaticRecommendationPolicy.ShouldApply(
            true, ManualLatches.None, phase,
            NavigationRecommendationState.Actionable,
            "PathOfKings.BountyHunter"));
    }
}
