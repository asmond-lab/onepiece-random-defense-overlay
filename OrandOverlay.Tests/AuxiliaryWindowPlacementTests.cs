using System.Windows;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AuxiliaryWindowPlacementTests
{
    [Fact]
    public void RoomOnLeftPlacesPanelOutsideMainWindow()
    {
        var owner = new Rect(700, 120, 1000, 800);
        var actual = AuxiliaryWindowPlacement.Calculate(owner, new Rect(0, 0, 1920, 1040), 1);
        Assert.Equal(new Rect(328, 120, 360, 620), actual);
        Assert.True(actual.Right < owner.Left);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1920, 1.5)]
    [InlineData(1920, 2)]
    public void EdgeAndMonitorScalingKeepWholePanelInsideWorkArea(double origin, double scale)
    {
        var work = new Rect(origin, -200, 1000, 600);
        var result = AuxiliaryWindowPlacement.Calculate(new Rect(origin, 300, 900, 800), work, scale);
        Assert.True(work.Contains(result));
        Assert.True(result.Width <= work.Width);
        Assert.True(result.Height <= work.Height);
    }

    [Fact]
    public void InvalidScaleIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AuxiliaryWindowPlacement.Calculate(
            new Rect(0, 0, 900, 700), new Rect(0, 0, 1920, 1080), double.NaN));
    }
}
