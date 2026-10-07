using Xunit;

namespace OrandOverlay.Tests;

public sealed class LoginClearRewardTests
{
    [Theory]
    [InlineData(4, 0, 0, 0, 0)]
    [InlineData(5, 1, 0, 0, 0)]
    [InlineData(9, 1, 0, 0, 0)]
    [InlineData(10, 1, 1, 0, 0)]
    [InlineData(14, 1, 1, 0, 0)]
    [InlineData(15, 2, 1, 0, 0)]
    [InlineData(19, 2, 1, 0, 0)]
    [InlineData(20, 2, 1, 1, 0)]
    [InlineData(24, 2, 1, 1, 0)]
    [InlineData(25, 3, 1, 1, 0)]
    [InlineData(29, 3, 1, 1, 0)]
    [InlineData(30, 3, 1, 1, 1)]
    [InlineData(34, 3, 1, 1, 1)]
    [InlineData(35, 3, 1, 1, 2)]
    [InlineData(39, 3, 1, 1, 2)]
    [InlineData(40, 3, 1, 1, 3)]
    [InlineData(50, 3, 1, 1, 3)]
    public void ThresholdsMatchMapAndLateAwardsAreExclusive(int count, int traits, int wisps, int lumber, int late)
    {
        var result = LoginClearRewards.ForCount(count);
        Assert.Equal(new LoginClearRewards(count, traits, wisps, lumber, late), result);
        Assert.Equal(result, LoginClearRewards.ForCount(count));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void UnknownCountDoesNotBecomeZeroOrEarnedReward(int? count) =>
        Assert.Null(LoginClearRewards.ForCount(count));
}
