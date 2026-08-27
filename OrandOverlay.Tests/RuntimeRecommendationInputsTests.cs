using System.Collections.Immutable;
using System.Reflection;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RuntimeRecommendationInputsTests
{
    [Fact]
    public void MixedKnownAndUnknownFieldsPreserveIndependentConfidenceAndScenarios()
    {
        var tracker = new RuntimeRecommendationInputsTracker(Profile());
        var snapshot = tracker.Capture(Frame(7, 11,
            Observation("resources", "lumber", 42, 10000),
            Observation("helper", "helper-mana", null, 8000)));

        Assert.True(snapshot.IsPlannerReady);
        Assert.Equal(42, snapshot.Field("lumber").Value.Value);
        Assert.Equal(10000, snapshot.Field("lumber").ConfidenceBp);
        Assert.False(snapshot.Field("helper-mana").Value.IsKnown);
        Assert.Equal(8000, snapshot.Field("helper-mana").ConfidenceBp);
        Assert.Equal(
            ["Unknown", "Known signed 32-bit nonnegative value"],
            snapshot.Field("lumber").FiniteScenarios.ToArray());
    }

    [Fact]
    public void InvalidOptionalValueMakesOnlyItsOwnFieldUnknown()
    {
        var tracker = new RuntimeRecommendationInputsTracker(Profile());
        var snapshot = tracker.Capture(Frame(7, 11,
            Observation("resources", "lumber", -1, 10000),
            Observation("rerolls-gamble-bounty-boss-item", "rerolls", 3, 10000)));

        Assert.True(snapshot.IsPlannerReady);
        Assert.False(snapshot.Field("lumber").Value.IsKnown);
        Assert.Equal(8000, snapshot.Field("lumber").ConfidenceBp);
        Assert.Equal(3, snapshot.Field("rerolls").Value.Value);
    }

    [Fact]
    public void ConflictingValuesInOneAtomicFrameCannotBecomeKnown()
    {
        var tracker = new RuntimeRecommendationInputsTracker(Profile());
        var snapshot = tracker.Capture(Frame(7, 11,
            Observation("resources", "lumber", 41, 10000),
            Observation("resources", "lumber", 42, 10000)));

        Assert.True(snapshot.IsPlannerReady);
        Assert.False(snapshot.Field("lumber").Value.IsKnown);
        Assert.Equal(8000, snapshot.Field("lumber").ConfidenceBp);
    }

    [Fact]
    public void ResourceValueAboveSigned32BitRangeRemainsUnknown()
    {
        var tracker = new RuntimeRecommendationInputsTracker(Profile());
        var snapshot = tracker.Capture(Frame(7, 11,
            Observation("resources", "lumber", (long)int.MaxValue + 1, 10000)));

        Assert.False(snapshot.Field("lumber").Value.IsKnown);
        Assert.Equal(8000, snapshot.Field("lumber").ConfidenceBp);
    }

    [Fact]
    public void StaleOrInvalidSourceCannotReplaceCurrentSnapshot()
    {
        var tracker = new RuntimeRecommendationInputsTracker(Profile());
        tracker.Capture(Frame(7, 11, Observation("resources", "lumber", 42, 10000)));

        var stale = tracker.Capture(Frame(7, 10,
            Observation("resources", "lumber", 99, 10000)));
        var wrongMap = tracker.Capture(Frame(7, 12,
            Observation("resources", "lumber", 99, 10000), identity: UnknownIdentity()));

        Assert.False(stale.IsPlannerReady);
        Assert.Equal(RuntimeRecommendationSnapshotState.Stale, stale.State);
        Assert.False(wrongMap.IsPlannerReady);
        Assert.Equal(RuntimeRecommendationSnapshotState.InvalidSource, wrongMap.State);
        var transientAfterInvalid = tracker.Capture(Frame(7, 13,
            state: RecognitionState.TransientReadError));
        Assert.False(transientAfterInvalid.IsPlannerReady);
    }

    [Fact]
    public void TransientReadFreezesLastGoodAndConfirmedResetClearsIt()
    {
        var tracker = new RuntimeRecommendationInputsTracker(Profile());
        var good = tracker.Capture(Frame(7, 11,
            Observation("resources", "lumber", 42, 10000)));

        var transient = tracker.Capture(Frame(7, 12, state: RecognitionState.TransientReadError));
        tracker.ConfirmReset(7);
        var afterReset = tracker.Capture(Frame(7, 13, state: RecognitionState.TransientReadError));

        Assert.Same(good, transient);
        Assert.False(afterReset.IsPlannerReady);
        Assert.Equal(RuntimeRecommendationSnapshotState.TransientWithoutLastGood, afterReset.State);
    }

    [Fact]
    public void NewMatchGenerationCannotRetainPriorMatchSnapshot()
    {
        var tracker = new RuntimeRecommendationInputsTracker(Profile());
        tracker.Capture(Frame(7, 11, Observation("resources", "lumber", 42, 10000)));

        var snapshot = tracker.Capture(Frame(8, 1, state: RecognitionState.TransientReadError));

        Assert.False(snapshot.IsPlannerReady);
        Assert.Equal(8, snapshot.MatchGeneration);
    }

    [Fact]
    public void ReadinessRequiresCurrentIdentityButNoOptionalField()
    {
        var tracker = new RuntimeRecommendationInputsTracker(Profile());

        var ready = tracker.Capture(Frame(7, 11));
        var notReady = new RuntimeRecommendationInputsTracker(Profile()).Capture(
            Frame(7, 11, identity: UnknownIdentity()));

        Assert.True(ready.IsPlannerReady);
        Assert.All(ready.Fields, field => Assert.False(field.Value.IsKnown));
        Assert.False(notReady.IsPlannerReady);
    }

    [Fact]
    public void SnapshotContractContainsNoActualNavigationSelectionField()
    {
        var names = typeof(NavigationStateSnapshot).GetProperties(
            BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name);

        Assert.DoesNotContain(names, name =>
            name.Contains("Selected", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Actual", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Choice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("CurrentTopCount", names);

        var snapshot = new RuntimeRecommendationInputsTracker(Profile()).Capture(Frame(7, 11,
            Observation("navigation-selection", "actual-choice", 3, 10000)));
        Assert.DoesNotContain(snapshot.Values, value => value.Name == "actual-choice");
    }

    [Theory]
    [InlineData(20, false, false, NavigationRecommendationMode.ProvisionalPreview)]
    [InlineData(21, false, false, NavigationRecommendationMode.ActionableRerank)]
    [InlineData(22, true, false, NavigationRecommendationMode.OverlayRecommendationLocked)]
    [InlineData(23, false, true, NavigationRecommendationMode.ManualOverrideLocked)]
    [InlineData(24, false, false, NavigationRecommendationMode.SourceForcedExpectation)]
    public void RecommendationModeNeverDependsOnObservedMapChoice(
        int round, bool overlayLocked, bool manualOverride, NavigationRecommendationMode expected)
    {
        var mode = NavigationRecommendationPolicy.ForRound(
            round, overlayLocked, manualOverride);

        Assert.Equal(expected, mode);
        Assert.False(NavigationRecommendationPolicy.ClaimsRuntimeConfirmation(mode));
    }

    private static RuntimeRecommendationInputFrame Frame(
        long matchGeneration,
        long revision,
        RuntimeRecommendationObservation observation = default,
        RuntimeRecommendationObservation observation2 = default,
        RecognitionState state = RecognitionState.Ready,
        RuntimeMapIdentityResult? identity = null)
    {
        var observations = ImmutableArray.Create(observation, observation2);
        return new RuntimeRecommendationInputFrame(matchGeneration, revision, state,
            identity ?? ProvenIdentity(), observations);
    }

    private static RuntimeRecommendationObservation Observation(
        string signal, string field, long? value, int confidence) =>
        new(signal, field, value, confidence);

    private static RuntimeSignalFeasibilityProfile Profile() =>
        RuntimeSignalFeasibilityProfileLoader.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Data"));

    private static RuntimeMapIdentityResult ProvenIdentity() => new(
        RuntimeMapIdentityState.Proven, RuntimeMapIdentityFailure.None, "proven",
        DateTimeOffset.UtcNow, RuntimeMapIdentityProvider.PathField, "D:/map.w3x",
        1, new string('a', 64), 1, 1, 1, 1);

    private static RuntimeMapIdentityResult UnknownIdentity() => new(
        RuntimeMapIdentityState.Unknown, RuntimeMapIdentityFailure.ArchiveHashMismatch,
        "wrong map", null, RuntimeMapIdentityProvider.PathField, "", 0, "", 1, 1, 0, 0);
}
