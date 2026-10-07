using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecognitionPolicyTests
{
    [Fact]
    public void ConsumedWaitingBoundaryDoesNotResetAgainUntilAnotherMatch()
    {
        var waiting = new RecognitionResult { State = RecognitionState.Waiting, ConfirmsSessionBoundary = true };
        Assert.False(RecognitionPolicy.ShouldResetMatch(waiting, 1, false));
        Assert.True(RecognitionPolicy.ShouldResetMatch(waiting, 2, false));
        for (var scans = 2; scans <= 30; scans++)
            Assert.False(RecognitionPolicy.ShouldResetMatch(waiting, scans, true));
        // A newly observed Ready match clears the boundary latch in ScanCoreAsync.
        Assert.True(RecognitionPolicy.ShouldResetMatch(waiting, 2, false));
        Assert.True(RecognitionPolicy.ShouldClearAutomaticInventory(waiting, 30));
    }
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
    public void ViviTransformedRawcodePreservesTransformedIdentity()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var map = new RawcodeUnitMap(catalog);
        Assert.True(RawcodeCodec.TryParse("W50h", out var rawcode));

        var mapped = map.Map(new Dictionary<uint, int> { [rawcode] = 1 });

        var entry = Assert.Single(mapped.Entries);
        Assert.Equal("rawcode:W50h", entry.UnitId);
        Assert.Equal("변화된", catalog.Unit(entry.UnitId).Tier);
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

    [Fact]
    public void ConfirmedReadyBoundaryResetsOldMatchBeforeApplyingNewInventory()
    {
        var nextMatch = new RecognitionResult
        {
            State = RecognitionState.Ready,
            ConfirmsSessionBoundary = true
        };
        var sameMatch = new RecognitionResult
        {
            State = RecognitionState.Ready,
            ConfirmsSessionBoundary = false
        };

        Assert.True(RecognitionPolicy.ShouldResetBeforeReadyInventory(nextMatch));
        Assert.False(RecognitionPolicy.ShouldResetBeforeReadyInventory(sameMatch));
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
