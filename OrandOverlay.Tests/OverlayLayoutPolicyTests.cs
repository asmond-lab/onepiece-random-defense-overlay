using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class OverlayLayoutPolicyTests
{
    [Fact]
    public void ScrollableBoardDoesNotForceFooterOutsideFixedWindow()
    {
        Assert.Equal(0, OverlayLayoutPolicy.ScrollableBoardMinimumHeight);
    }
}
