using OrandOverlay;
using Xunit;
namespace PlannerEvidenceCapture.Tests;
public sealed class RandipickCleanupViewportTests
{
    [Theory]
    [InlineData("overlay", 1080, 540, 740)]
    [InlineData("main", 1080, 1080, 720)]
    [InlineData("main", 920, 920, 620)]
    public void LoadedMonitorScaleCannotChangeCleanupCaptureContract(string surface, int width, double expectedWidth, double expectedHeight)
    {
        var scale = UiScale.FromScreen(1920, 1080, 1, 1);
        Assert.Equal(0.75, scale);
        var actual = RandipickCleanupViewport.Resolve(surface, 540 * scale, 740 * scale, scale, width);
        Assert.Equal(expectedWidth, actual.Width); Assert.Equal(expectedHeight, actual.Height);
        Assert.Equal(1, actual.ContentScale);
    }
}
