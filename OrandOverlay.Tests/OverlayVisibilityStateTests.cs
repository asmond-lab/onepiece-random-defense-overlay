using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class OverlayVisibilityStateTests
{
    [Fact]
    public void Hidden_overlay_stays_hidden_when_hand_temporarily_disappears_and_returns()
    {
        var state = new OverlayVisibilityState(HandAvailable: true, HiddenByUser: false)
            .HideByUser()
            .WithHandAvailability(false)
            .WithHandAvailability(true);

        Assert.True(state.HiddenByUser);
        Assert.False(state.ShouldShow);
    }
}
