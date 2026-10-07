using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GoroseiMemoryDetectorTests
{
    [Theory]
    [InlineData("E20o", GoroseiMode.Warcury)]
    [InlineData("130o", GoroseiMode.Saturn)]
    [InlineData("230o", GoroseiMode.Nasjuro)]
    public void MapMarkerRawcodeSelectsGorosei(string text, GoroseiMode expected)
    {
        Assert.True(RawcodeCodec.TryParse(text, out var rawcode));

        Assert.Equal(expected, GoroseiMemoryDetector.FromRawcode(rawcode));
    }

    [Fact]
    public void PositiveDetectionOverridesPreviousManualSelection()
    {
        Assert.Equal(GoroseiMode.Warcury,
            GoroseiMemoryDetector.Resolve(GoroseiMode.Nasjuro,
                GoroseiMode.Warcury));
        Assert.Equal(GoroseiMode.Nasjuro,
            GoroseiMemoryDetector.Resolve(GoroseiMode.Nasjuro,
                GoroseiMode.None));
    }
}
