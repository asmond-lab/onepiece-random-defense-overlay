using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletUpgradeCountTests
{
    [Fact]
    public void CountsAndTiersMustDescribeTheSameUpgradeState()
    {
        var tiers = new BulletGuideRuntimeState(true, 2, 1, 1, "fixture");
        Assert.Equal(new BulletUpgradeCounts(1, 1, 15),
            tiers.WithExactCounts(new(1, 1, 15)).ExactCounts);
        Assert.False(tiers.WithExactCounts(new(1, 1, 14)).IsCurrent);
    }

    [Fact]
    public void ExactChargesRemainDistinctWithinTheSameTier()
    {
        var items = new Dictionary<int, (string, int)>
        {
            [0] = ("I091", 14), [2] = ("I092", 15), [4] = ("I093", 29)
        };
        Assert.Equal(new BulletUpgradeCounts(14, 15, 29), BulletUpgradeCounts.FromItems(items));
    }

    [Fact]
    public void MaxReplacementItemsMeanThirtyNotTheirDefaultCharges()
    {
        var items = new Dictionary<int, (string, int)>
        {
            [0] = ("I006", 0), [2] = ("I007", 1), [4] = ("I008", 0)
        };
        Assert.Equal(new BulletUpgradeCounts(30, 30, 30), BulletUpgradeCounts.FromItems(items));
    }

    [Theory]
    [InlineData("I093", 15)]
    [InlineData("I091", 0)]
    [InlineData("I091", 31)]
    [InlineData("other", 15)]
    public void WrongSlotOrInvalidNormalCountStaysUnknown(string attackCode, int charges)
    {
        var items = new Dictionary<int, (string, int)>
        {
            [0] = (attackCode, charges), [2] = ("I092", 15), [4] = ("I093", 15)
        };
        Assert.Null(BulletUpgradeCounts.FromItems(items));
    }
}
