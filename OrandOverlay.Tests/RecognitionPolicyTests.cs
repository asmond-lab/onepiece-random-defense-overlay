using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecognitionPolicyTests
{
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
