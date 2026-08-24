using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecommendationRefreshTests
{
    [Fact]
    public void FlowStepUsesOnlyRemainingCount()
    {
        var step = new RecipeCraftStep
        {
            UnitId = "rawcode:S20h",
            Name = "조로",
            RequiredCount = 3,
            OwnedCount = 1
        };

        Assert.Equal(2, RecommendationPresentation.FlowRemainingCount(step));
    }

    [Fact]
    public void SupersededRefreshResultCannotReplaceLatestBoard()
    {
        var versions = new LatestRefreshVersion();
        var first = versions.Next();
        var latest = versions.Next();

        Assert.False(versions.IsCurrent(first));
        Assert.True(versions.IsCurrent(latest));
    }
}
