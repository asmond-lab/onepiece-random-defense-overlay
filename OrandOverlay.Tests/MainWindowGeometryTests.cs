using System.Windows;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class MainWindowGeometryTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void MissingOrInvalidValuesAreNotRestored()
    {
        Assert.Null(MainWindowGeometry.Restore(null, 0, 900, 600, Work, 800, 560));
        Assert.Null(MainWindowGeometry.Restore(0, 0, double.NaN, 600, Work, 800, 560));
        Assert.Null(MainWindowGeometry.Restore(0, 0, 900, 0, Work, 800, 560));
    }

    [Fact]
    public void UserSizeIsKeptWhenItFits()
    {
        Assert.Equal(new Rect(100, 50, 1400, 900), MainWindowGeometry.Restore(100, 50, 1400, 900, Work, 800, 560));
    }

    [Fact]
    public void SizeBelowMinimumIsRaisedAndOversizeIsShrunk()
    {
        Assert.Equal(new Rect(0, 0, 800, 560), MainWindowGeometry.Restore(0, 0, 300, 200, Work, 800, 560));
        Assert.Equal(new Rect(0, 0, 1920, 1040), MainWindowGeometry.Restore(0, 0, 4000, 3000, Work, 800, 560));
    }

    [Fact]
    public void OffscreenWindowIsPulledBackInside()
    {
        Assert.Equal(new Rect(920, 0, 1000, 700), MainWindowGeometry.Restore(3000, -400, 1000, 700, Work, 800, 560));
    }
}
