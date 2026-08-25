using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class OverlayLayoutPolicyTests
{
    [Fact]
    public void CompactStatsLayoutKeepsCoreAndCollapsesNonCore()
    {
        var compact = OverlayLayoutPolicy.StatsLayout(
            OverlayDisplayMode.StatsOnlyCompact);
        var full = OverlayLayoutPolicy.StatsLayout(
            OverlayDisplayMode.Full);

        Assert.Equal(228, compact.Width);
        Assert.Equal(360, compact.Height);
        Assert.False(compact.NonCoreVisible);
        Assert.Equal(700, full.Height);
        Assert.True(full.NonCoreVisible);
    }

    [Fact]
    public void ScrollableBoardDoesNotForceFooterOutsideFixedWindow()
    {
        Assert.Equal(0, OverlayLayoutPolicy.ScrollableBoardMinimumHeight);
    }
}
