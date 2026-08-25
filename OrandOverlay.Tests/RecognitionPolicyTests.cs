using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecognitionPolicyTests
{
    [Fact]
    public void PoolRebuildDuringConfirmedMatchKeepsPreviousInventory()
    {
        var state = WarcraftMemoryRecognitionService.PoolNotReadyState(
            localPlayerConfirmed: true);
        var result = new RecognitionResult { State = state };

        Assert.Equal(RecognitionState.TransientReadError, state);
        Assert.False(result.ShouldClearAutomaticInventory);
    }

    [Fact]
    public void FirstConfirmedWaitingScanKeepsLastGoodInventory()
    {
        var waiting = new RecognitionResult
        {
            State = RecognitionState.Waiting,
            ConfirmsSessionBoundary = true
        };

        Assert.False(RecognitionPolicy.ShouldClearAutomaticInventory(waiting, 1));
        Assert.True(RecognitionPolicy.ShouldUseLastGood(waiting, 1));
        Assert.True(RecognitionPolicy.ShouldClearAutomaticInventory(waiting, 2));
        Assert.False(RecognitionPolicy.ShouldUseLastGood(waiting, 2));
    }

    [Fact]
    public void SnapshotChangeKeepsVerifiedLocatorForNextTick()
    {
        var changed = new WarcraftMemoryRecognitionService.SnapshotChangedException();

        Assert.False(WarcraftMemoryRecognitionService.ShouldInvalidateLocator(changed));
        Assert.True(WarcraftMemoryRecognitionService.ShouldInvalidateLocator(
            new InvalidDataException()));
    }

    [Fact]
    public void ViviTransformedRawcodeMapsMemoryToRareIdentity()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var map = new RawcodeUnitMap(catalog);
        Assert.True(RawcodeCodec.TryParse("W50h", out var rawcode));

        var mapped = map.Map(new Dictionary<uint, int> { [rawcode] = 1 });

        var entry = Assert.Single(mapped.Entries);
        Assert.Equal("rawcode:O10h", entry.UnitId);
        Assert.Equal("희귀함", catalog.Unit(entry.UnitId).Tier);
        Assert.Equal("변화된", catalog.Unit("rawcode:W50h").Tier);
    }

    [Fact]
    public void ShouldResetMatch_WaitsForTwoConfirmedBoundaryScans()
    {
        var confirmed = new RecognitionResult
        {
            State = RecognitionState.Waiting,
            ConfirmsSessionBoundary = true
        };
        var unconfirmed = new RecognitionResult
        {
            State = RecognitionState.Waiting,
            ConfirmsSessionBoundary = false
        };
        Assert.False(RecognitionPolicy.ShouldResetMatch(confirmed, confirmedWaitingScans: 1));
        Assert.True(RecognitionPolicy.ShouldResetMatch(confirmed, confirmedWaitingScans: 2));
        Assert.False(RecognitionPolicy.ShouldResetMatch(unconfirmed, confirmedWaitingScans: 2));
    }

    [Theory]
    [InlineData(RecognitionState.TransientReadError, true)]
    [InlineData(RecognitionState.Waiting, false)]
    [InlineData(RecognitionState.Unsupported, false)]
    public void MayUseLastGoodForRecommendations_IsLimitedToReadRaces(
        RecognitionState state, bool expected)
    {
        Assert.Equal(expected,
            RecognitionPolicy.MayUseLastGoodForRecommendations(state));
    }
}
