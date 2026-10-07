using Xunit;

namespace OrandOverlay.Tests;

public sealed class ForegroundGameWatcherTests
{
    [Theory]
    [InlineData("Warcraft III", true)]
    [InlineData("warcraft iii", true)]
    [InlineData("War3", true)]
    [InlineData("OrandOverlay", false)]
    [InlineData("chrome", false)]
    [InlineData(null, false)]
    public void OnlyWarcraftProcessesCountAsGame(string? name, bool expected) =>
        Assert.Equal(expected, ForegroundGameWatcher.IsWarcraftProcess(name));

    [Fact]
    public void WithoutStartOverlaysAreNotSuppressed() =>
        Assert.True(ForegroundGameWatcher.IsGameForeground);
}
