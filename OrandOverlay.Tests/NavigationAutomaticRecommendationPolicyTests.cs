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
            true, ManualLatches.None, state, "PathOfKings.BountyHunter"));
    }

    [Theory]
    [InlineData(NavigationRecommendationState.Waiting)]
    [InlineData(NavigationRecommendationState.Provisional)]
    [InlineData(NavigationRecommendationState.ManualOverride)]
    [InlineData(NavigationRecommendationState.NoSafeRecommendation)]
    public void DoesNotApplyNonActionableStates(NavigationRecommendationState state)
    {
        Assert.False(NavigationAutomaticRecommendationPolicy.ShouldApply(
            true, ManualLatches.None, state, "PathOfKings.BountyHunter"));
    }

    [Fact]
    public void DisabledOrManualModeNeverChangesTheOverlaySetting()
    {
        Assert.False(NavigationAutomaticRecommendationPolicy.ShouldApply(
            false, ManualLatches.None, NavigationRecommendationState.Actionable,
            "PathOfKings.BountyHunter"));
        Assert.False(NavigationAutomaticRecommendationPolicy.ShouldApply(
            true, new ManualLatches(false, true), NavigationRecommendationState.Actionable,
            "PathOfKings.BountyHunter"));
        Assert.False(NavigationAutomaticRecommendationPolicy.ShouldApply(
            true, ManualLatches.None, NavigationRecommendationState.Actionable, null));
    }
}
