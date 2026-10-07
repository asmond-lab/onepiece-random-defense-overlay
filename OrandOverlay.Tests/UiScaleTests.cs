using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class UiScaleTests
{
    [Theory]
    [InlineData(1280, 720, 1.0, 1.0, 0.50)]
    [InlineData(1920, 1080, 1.0, 1.0, 0.75)]
    [InlineData(1920, 1080, 1.25, 1.25, 0.60)]
    [InlineData(2560, 1440, 1.0, 1.0, 1.00)]
    [InlineData(3840, 2160, 1.0, 1.0, 1.50)]
    [InlineData(3840, 2160, 1.5, 1.5, 1.00)]
    [InlineData(3440, 1440, 1.0, 1.0, 1.00)]
    [InlineData(1920, 1200, 1.0, 1.0, 0.75)]
    [InlineData(1280, 1024, 1.0, 1.0, 0.50)]
    public void Overlay_keeps_the_same_screen_share_across_resolution_and_dpi(
        double physicalWidth, double physicalHeight, double dpiScaleX, double dpiScaleY,
        double expected)
    {
        Assert.Equal(expected,
            UiScale.FromScreen(physicalWidth, physicalHeight, dpiScaleX, dpiScaleY),
            precision: 3);
    }

    [Fact]
    public void Fhd_and_2k_overlays_take_the_same_horizontal_share()
    {
        const double overlayDesignWidth = 540 + 228;
        var fhdShare = overlayDesignWidth * UiScale.FromScreen(1080, 1.0) / 1920;
        var qhdShare = overlayDesignWidth * UiScale.FromScreen(1440, 1.0) / 2560;

        Assert.Equal(qhdShare, fhdShare, precision: 3);
    }
}
