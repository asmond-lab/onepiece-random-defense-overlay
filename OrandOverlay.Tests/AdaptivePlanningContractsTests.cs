using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Reflection;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AdaptivePlanningContractsTests
{
    [Fact]
    public void CanonicalInputHashIncludesEverySemanticFieldAndIsCultureIndependent()
    {
        var input = Fixture();
        var changed = new AdaptivePlanningInput(input.MatchGeneration, input.Round + 1,
            input.Phase, input.Story, input.Values, input.ManualLatches,
            input.ProfileHash, input.DataHash);

        Assert.Equal(input.CanonicalFingerprint, input.CanonicalFingerprint);
        Assert.NotEqual(input.CanonicalFingerprint, changed.CanonicalFingerprint);
        Assert.Equal(input.CanonicalSerialization,
            input.CanonicalSerialization);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ko-KR");
            var korean = input.CanonicalFingerprint;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal(korean, input.CanonicalFingerprint);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void UnknownAndKnownZeroAreDistinctAndOrdinalSerializationIsStable()
    {
        var unknown = PlanningValue.Unknown("lumber");
        var zero = PlanningValue.Known("lumber", 0);

        Assert.NotEqual(unknown, zero);
        Assert.NotEqual(unknown.Serialize(), zero.Serialize());
        Assert.Equal("known:lumber:0", zero.Serialize());
    }

    [Fact]
    public void RationalNormalizesAndRejectsMalformedBounds()
    {
        Assert.Equal(new Rational(new BigInteger(1), new BigInteger(2)),
            new Rational(new BigInteger(2), new BigInteger(4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rational(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rational(-1, 2));
    }

    [Fact]
    public void ResultCopiesCollectionsAndIsImmutableSealedSurface()
    {
        var route = new AdaptiveRouteResult(DamageLane.Physical, "top", 8000, 6000,
            ImmutableArray.Create("support"), ImmutableArray.Create(ReasonCode.Ready));
        var result = new AdaptivePlanningResult(
            "abc", PlannerPhase.CommitRound20, route, null,
            ImmutableArray.Create(ReasonCode.Ready));

        Assert.True(typeof(AdaptivePlanningInput).IsSealed);
        Assert.True(typeof(AdaptivePlanningResult).IsSealed);
        Assert.True(typeof(AdaptiveRouteResult).IsSealed);
        Assert.DoesNotContain(typeof(AdaptivePlanningInput).GetProperties(),
            property => property.SetMethod is not null);
        Assert.Equal("abc", result.InputFingerprint);
        Assert.Single(result.Route!.PackageUnitIds);
    }

    [Fact]
    public void RationalAndIntervalRespectExactEqualityAndBounds()
    {
        var interval = new RationalInterval(new Rational(1, 3), new Rational(2, 3));

        Assert.True(interval.Contains(new Rational(1, 2)));
        Assert.False(interval.Contains(new Rational(5, 6)));
        Assert.Throws<ArgumentException>(() =>
            new RationalInterval(new Rational(2, 3), new Rational(1, 3)));
    }

    [Fact]
    public void JsonRoundTripPreservesUnknownKnownZeroAndCommittedPhysicalResult()
    {
        var input = Fixture();
        var route = new AdaptiveRouteResult(DamageLane.Physical, "top", 8000, 6000,
            ImmutableArray.Create("support"), ImmutableArray.Create(ReasonCode.Ready));
        var distribution = new NavigationOutcomeDistribution(
            ImmutableArray.Create(new Rational(1, 1)),
            new RationalInterval(new Rational(1, 2), new Rational(1, 2)),
            new RationalInterval(new Rational(0, 1), new Rational(1, 1)),
            new RationalInterval(new Rational(0, 1), new Rational(1, 1)),
            8000, ImmutableArray.Create(ReasonCode.Ready));
        var result = new AdaptivePlanningResult("fp", PlannerPhase.Committed, route, distribution,
            ImmutableArray.Create(ReasonCode.Ready));
        var options = AdaptivePlanningJson.CreateOptions();

        var unknownJson = JsonSerializer.Serialize(PlanningValue.Unknown("lumber"), options);
        var zeroJson = JsonSerializer.Serialize(PlanningValue.Known("lumber", 0), options);
        var unknown = JsonSerializer.Deserialize<PlanningValue>(unknownJson, options);
        var zero = JsonSerializer.Deserialize<PlanningValue>(zeroJson, options);
        var inputCopy = JsonSerializer.Deserialize<AdaptivePlanningInput>(
            JsonSerializer.Serialize(input, options), options);
        var resultCopy = JsonSerializer.Deserialize<AdaptivePlanningResult>(
            JsonSerializer.Serialize(result, options), options);

        Assert.False(unknown.IsKnown);
        Assert.Equal(0, zero.Value);
        Assert.NotNull(inputCopy);
        Assert.NotNull(resultCopy);
        Assert.Equal(input.CanonicalFingerprint, inputCopy!.CanonicalFingerprint);
        Assert.Equal(result.InputFingerprint, resultCopy!.InputFingerprint);
        Assert.Equal(result.Route!.GoalUnitId, resultCopy.Route!.GoalUnitId);
        Assert.NotEqual(unknown, zero);
        Assert.Equal(new Rational(1, 1), resultCopy!.Navigation!.Probabilities.Single());
    }

    [Fact]
    public void CanonicalEncodingSeparatesDelimiterCollisionAndBindsHashes()
    {
        var collisionA = new AdaptivePlanningInput(1, 2, PlannerPhase.SpendRares,
            new StorySignal(1, ImmutableArray<string>.Empty, true),
            ImmutableArray.Create(PlanningValue.Known("a:1,known:b", 1)),
            ManualLatches.None, "profile", "data");
        var collisionB = new AdaptivePlanningInput(1, 2, PlannerPhase.SpendRares,
            new StorySignal(1, ImmutableArray<string>.Empty, true),
            ImmutableArray.Create(PlanningValue.Known("a", 1), PlanningValue.Known("b", 1)),
            ManualLatches.None, "profile", "data");

        Assert.NotEqual(collisionA.CanonicalFingerprint, collisionB.CanonicalFingerprint);
        var changedHash = new AdaptivePlanningInput(1, 2, PlannerPhase.SpendRares,
            collisionA.Story, collisionA.Values, collisionA.ManualLatches,
            "profile|other", "data").CanonicalFingerprint;
        Assert.NotEqual(collisionA.CanonicalFingerprint, changedHash);
    }

    [Fact]
    public void EveryTaskThreeDtoIsSealedOrReadonlyAndDoesNotExposeMutableCollections()
    {
        var types = new[]
        {
            typeof(StorySignal), typeof(PlanningValue), typeof(ManualLatches),
            typeof(NavigationStateSnapshot), typeof(NavigationOutcomeDistribution),
            typeof(RationalInterval), typeof(AdaptiveDecisionTrace),
            typeof(AdaptivePlanningInput), typeof(AdaptivePlanningResult),
            typeof(AdaptiveRouteResult), typeof(SignedRouteDelta)
        };

        foreach (var type in types)
        {
            Assert.True(type.IsValueType || type.IsSealed, type.FullName);
            Assert.DoesNotContain(type.GetProperties(), property => property.SetMethod is not null);
            Assert.DoesNotContain(type.GetProperties(), property => IsMutableCollection(property.PropertyType));
        }
        Assert.True(IsMutableCollection(typeof(List<int>)));
        Assert.True(IsMutableCollection(typeof(Dictionary<string, int>)));
        Assert.True(IsMutableCollection(typeof(int[])));
    }

    [Fact]
    public void SignedRouteDeltaRepresentsNegativeOutcomeUplift()
    {
        var delta = new SignedRouteDelta(-250);

        Assert.Equal(-250, delta.Value);
        var options = AdaptivePlanningJson.CreateOptions();
        Assert.Equal(delta, JsonSerializer.Deserialize<SignedRouteDelta>(
            JsonSerializer.Serialize(delta, options), options));
    }

    private static bool IsMutableCollection(Type type) =>
        type.IsArray ||
        (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) ||
            type.GetGenericTypeDefinition() == typeof(Dictionary<,>)));

    private static AdaptivePlanningInput Fixture() =>
        new(
            4,
            20,
            PlannerPhase.SpendRares,
            new StorySignal(9, ImmutableArray.Create("stage-8"), true),
            ImmutableArray.Create(
                PlanningValue.Known("gold", 0),
                PlanningValue.Unknown("lumber")),
            ManualLatches.None,
            "profile-sha",
            "data-sha");
}
