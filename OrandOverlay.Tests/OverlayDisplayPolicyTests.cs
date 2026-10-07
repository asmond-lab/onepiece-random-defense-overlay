using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class OverlayDisplayPolicyTests
{
    [Theory]
    [InlineData(OverlayDisplayMode.Full, true, true, true)]
    [InlineData(OverlayDisplayMode.StatsOnly, true, false, true)]
    [InlineData(OverlayDisplayMode.Hidden, true, false, false)]
    [InlineData(OverlayDisplayMode.Full, false, false, false)]
    [InlineData(OverlayDisplayMode.StatsOnly, false, false, false)]
    [InlineData(OverlayDisplayMode.Hidden, false, false, false)]
    public void VisibilityMatrixMatchesModeAndAvailability(
        OverlayDisplayMode mode, bool available,
        bool expectedRecommendation, bool expectedStats)
    {
        var visibility = OverlayDisplayPolicy.Visibility(
            new OverlayDisplayState(mode, OverlayDisplayMode.Full,
                available));

        Assert.Equal(expectedRecommendation,
            visibility.RecommendationVisible);
        Assert.Equal(expectedStats, visibility.StatsVisible);
    }

    [Fact]
    public void HiddenToggleRestoresLastCompactMode()
    {
        var hidden = new OverlayDisplayState(OverlayDisplayMode.Hidden,
            OverlayDisplayMode.StatsOnly, true);

        var restored = OverlayDisplayPolicy.Toggle(hidden);

        Assert.Equal(OverlayDisplayMode.StatsOnly, restored.Mode);
        Assert.True(OverlayDisplayPolicy.Visibility(restored).StatsVisible);
        Assert.False(OverlayDisplayPolicy.Visibility(restored)
            .RecommendationVisible);
    }

    [Fact]
    public void AvailabilityLossNeverChangesStoredMode()
    {
        var compact = new OverlayDisplayState(
            OverlayDisplayMode.StatsOnly,
            OverlayDisplayMode.StatsOnly, true);

        var unavailable = OverlayDisplayPolicy.WithAvailability(
            compact, false);
        var restored = OverlayDisplayPolicy.WithAvailability(
            unavailable, true);

        Assert.Equal(compact with { Available = false }, unavailable);
        Assert.Equal(compact, restored);
    }

    [Fact]
    public void ClosingVisibleWindowPersistsHiddenAndKeepsLastMode()
    {
        var full = new OverlayDisplayState(OverlayDisplayMode.Full,
            OverlayDisplayMode.Full, true);

        var hidden = OverlayDisplayPolicy.Select(
            full, OverlayDisplayMode.Hidden);

        Assert.Equal(OverlayDisplayMode.Hidden, hidden.Mode);
        Assert.Equal(OverlayDisplayMode.Full, hidden.LastVisibleMode);
    }
}
