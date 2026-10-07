using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CoachSignalAdapterTests
{
    [Fact]
    public void CurrentVerifiedZeroResourceIsNotUnknown()
    {
        var snapshot = Snapshot(10000);
        var values = CoachSignalAdapter.Read(snapshot, 2, 7, true);
        Assert.Equal(0, values["gold"]);
        Assert.Null(values["lumber"]);
    }

    [Theory]
    [InlineData(1, 7, true, 10000)]
    [InlineData(2, 6, true, 10000)]
    [InlineData(2, 7, false, 10000)]
    [InlineData(2, 7, true, 8000)]
    public void OldOrOnlySourceBoundResourcesRemainUnknown(long generation, long revision,
        bool current, int confidence)
    {
        var values = CoachSignalAdapter.Read(Snapshot(confidence), generation, revision, current);
        Assert.All(values.Values, value => Assert.Null(value));
    }

    private static NavigationStateSnapshot Snapshot(int confidence) => new(
        2, 7, RuntimeRecommendationSnapshotState.Current, true,
        [new RuntimeRecommendationField(PlanningValue.Known("gold", 0), confidence, [])]);
}
