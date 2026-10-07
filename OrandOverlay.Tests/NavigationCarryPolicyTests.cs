using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class NavigationCarryPolicyTests
{
    [Theory]
    [InlineData(GoalCarryMode.SoloPreferred, TopScope.SoloTop)]
    [InlineData(GoalCarryMode.MultiAllowed, TopScope.SoloTop)]
    [InlineData(GoalCarryMode.Unknown, TopScope.SoloTop)]
    [InlineData(GoalCarryMode.MultiRequired, TopScope.MultiTop)]
    public void SampleVolumeCannotOverrideExplicitCarryMode(
        GoalCarryMode mode, TopScope expected) =>
        Assert.Equal(expected, NavigationCarryPolicy.PreferredScope(mode));

    [Fact]
    public void MultiRequiredFitsOnlyMultiNavigation()
    {
        var solo = NavigationProfiles.Find("PathOfKings.BountyHunter");
        var multi = NavigationProfiles.Find("AlliedForces.EmergencyCall");

        Assert.False(NavigationCarryPolicy.Fits(
            GoalCarryMode.MultiRequired, solo));
        Assert.True(NavigationCarryPolicy.Fits(
            GoalCarryMode.MultiRequired, multi));
    }

    [Fact]
    public void UnknownDefaultsToSingleTopNavigation()
    {
        var solo = NavigationProfiles.Find("PathOfKings.BountyHunter");
        var multi = NavigationProfiles.Find("AlliedForces.EmergencyCall");

        Assert.True(NavigationCarryPolicy.Fits(GoalCarryMode.Unknown, solo));
        Assert.False(NavigationCarryPolicy.Fits(GoalCarryMode.Unknown, multi));
    }
}
