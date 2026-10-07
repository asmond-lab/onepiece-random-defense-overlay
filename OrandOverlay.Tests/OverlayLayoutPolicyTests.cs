using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class OverlayLayoutPolicyTests
{
    [Fact]
    public void StatsOnlyKeepsSharedFocusStatsLayout()
    {
        var compact = OverlayLayoutPolicy.StatsLayout(
            OverlayDisplayMode.StatsOnly);
        var full = OverlayLayoutPolicy.StatsLayout(
            OverlayDisplayMode.Full);

        Assert.Equal(326, compact.Width);
        Assert.Equal(440, compact.Height);
        Assert.True(compact.NonCoreVisible);
        Assert.Equal(326, full.Width);
        Assert.Equal(440, full.Height);
        Assert.True(full.NonCoreVisible);
    }

    [Fact]
    public void ScrollableBoardDoesNotForceFooterOutsideFixedWindow()
    {
        Assert.Equal(0, OverlayLayoutPolicy.ScrollableBoardMinimumHeight);
    }
}
