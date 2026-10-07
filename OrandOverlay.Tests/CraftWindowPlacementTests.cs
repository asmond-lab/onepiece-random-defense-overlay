using System.Windows;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CraftWindowPlacementTests
{
    [Fact]
    public void DefaultFollowsActualStatsBoundsWithEightDipGap()
    {
        var bounds = CraftWindowPlacement.Calculate(new(120, 64, 326, 440), new(0, 0, 1920, 1040), new(326, 440));
        Assert.Equal(new Rect(120, 512, 326, 440), bounds);
    }

    [Fact]
    public void ShortensBelowStatsBeforeMovingElsewhere()
    {
        var bounds = CraftWindowPlacement.Calculate(new(100, 100, 326, 440), new(0, 0, 1920, 900), new(326, 440));
        Assert.Equal(new Rect(100, 548, 326, 352), bounds);
    }

    [Fact]
    public void UsesSpaceAboveWhenBelowCannotFitMinimumHeight()
    {
        var stats = new Rect(100, 600, 326, 300);
        var bounds = CraftWindowPlacement.Calculate(stats, new(0, 0, 1920, 1040), new(326, 440));
        Assert.Equal(new Rect(100, 152, 326, 440), bounds);
        Assert.False(bounds.IntersectsWith(stats));
    }

    [Fact]
    public void UsesAdjacentSpaceWhenNeitherVerticalSideFits()
    {
        var stats = new Rect(100, 100, 326, 440);
        var bounds = CraftWindowPlacement.Calculate(stats, new(0, 0, 1200, 700), new(326, 440));
        Assert.Equal(new Rect(434, 100, 326, 440), bounds);
        Assert.False(bounds.IntersectsWith(stats));
    }

    [Fact]
    public void TinyWorkAreaStillKeepsWholeWindowReachable()
    {
        var work = new Rect(-200, -100, 250, 200);
        var bounds = CraftWindowPlacement.Calculate(new(-190, -90, 326, 440), work, new(326, 440));
        Assert.Equal(work, bounds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NegativeMonitorOriginsAreNeverScaled(double dpi)
    {
        var stats = new Rect(-2300, -1300, 326 * dpi, 440 * dpi);
        var work = new Rect(-2560, -1440, 2560, 2160);
        var bounds = CraftWindowPlacement.Calculate(stats, work, new(326, 440), dpi);
        Assert.Equal(stats.Left, bounds.Left);
        Assert.Equal(stats.Bottom + 8 * dpi, bounds.Top);
        Assert.Equal(326 * dpi, bounds.Width);
        Assert.True(work.Contains(bounds));
    }

    [Theory]
    [InlineData(-6000, -5000, 1500, 2000, 1)]
    [InlineData(9000, 9000, 900, 900, 1.5)]
    [InlineData(-1500, 100, 1, 1, 2)]
    public void RestoreClampsRemovedMonitorOversizeAndUndersize(double left, double top, double width, double height, double dpi)
    {
        var work = new Rect(-1920, 0, 1920, 1040);
        var saved = new CraftWindowGeometry(left, top, width, height);
        var bounds = CraftWindowPlacement.Restore(saved, work, dpi);
        Assert.True(work.Contains(bounds));
        Assert.InRange(bounds.Width, Math.Min(320 * dpi, work.Width), work.Width);
        Assert.InRange(bounds.Height, Math.Min(260 * dpi, work.Height), work.Height);
    }

    [Fact]
    public void RestoreUsesCurrentMonitorDpiAndPreservesNativeOrigin()
    {
        var saved = new CraftWindowGeometry(-2200, 150, 480, 500) { DpiScale = 1 };
        var bounds = CraftWindowPlacement.Restore(saved, new(-2560, 0, 2560, 1440), 1.5);
        Assert.Equal(new Rect(-2200, 150, 720, 750), bounds);
    }

    [Theory]
    [InlineData(double.NaN, double.PositiveInfinity)]
    [InlineData(0, 0)]
    public void NonFiniteOrZeroSizesUseCompactDefaults(double width, double height)
    {
        var bounds = CraftWindowPlacement.Restore(new(double.NaN, double.NaN, width, height), new(0, 0, 1920, 1040));
        Assert.Equal(new Rect(0, 0, 326, 440), bounds);
    }
}
