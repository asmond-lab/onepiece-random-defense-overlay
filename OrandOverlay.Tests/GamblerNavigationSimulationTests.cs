using System.Collections.Immutable;
using System.Numerics;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GamblerNavigationSimulationTests
{
    [Theory]
    [InlineData(false, "casino-normal", 3)]
    [InlineData(true, "casino-isekai", 4)]
    public void CasinoUsesPinnedExactSequentialMass(bool isekai, string poolId, int states)
    {
        var profile = Load();
        var result = GamblerNavigationSimulation.Run(profile,
            Request("Gambler.Casino", high: 1, isekai: isekai));

        AssertSafeUnitMass(result, 1, states);
        var expected = profile.Pool(poolId).Members.ToDictionary(
            member => member.Id, member => member.Probability.Value);
        var pinned = isekai
            ? new[] { new Rational(3, 10), new Rational(7, 250),
                new Rational(21, 3125), new Rational(2079, 3125) }
            : [new Rational(3, 10), new Rational(7, 250),
                new Rational(84, 125)];
        Assert.Equal(pinned, expected.Values);
        Assert.Equal(expected, RewardMasses(result, expected.Keys));
        Assert.All(result.Outcomes, outcome =>
            Assert.Equal(1, outcome.State.RerollsAvailable));
        Assert.Equal([GamblerActionTier.Low, GamblerActionTier.Middle],
            result.DisabledTiers.ToArray());
    }

    [Fact]
    public void RiskUsesExactHighPoolAndPinnedWorldHazard()
    {
        var profile = Load();
        var high = GamblerNavigationSimulation.Run(profile,
            Request("Gambler.RiskHedge", high: 1));

        AssertSafeUnitMass(high, 1, 4);
        Assert.Equal([new Rational(3, 10), new Rational(7, 250),
                new Rational(42, 125), new Rational(42, 125)],
            profile.Pool("risk-high-gamble").Members.Select(member =>
                member.Probability.Value).ToArray());
        Assert.Equal(profile.Pool("risk-high-gamble").Members.ToDictionary(
            member => member.Id, member => member.Probability.Value),
            RewardMasses(high, profile.Pool("risk-high-gamble").Members
                .Select(member => member.Id)));
        Assert.All(high.Outcomes, outcome => Assert.Equal(2, outcome.State.RerollsAvailable));
        Assert.Equal(2, high.Outcomes.Single(outcome =>
            outcome.State.Reward("failure") == 1).State.Reward("consolation_token"));
        Assert.Equal(1, high.Outcomes.Single(outcome =>
            outcome.State.Reward("failure") == 1).State.Reward("lumber"));

        Assert.Equal([22, 33, 44, 55, 66, 77, 88, 99, 100],
            Enumerable.Range(0, 9).Select(failures =>
                (int)(GamblerNavigationSimulation.WorldSuccessProbability(
                    profile, failures).Numerator * 100 /
                    GamblerNavigationSimulation.WorldSuccessProbability(
                        profile, failures).Denominator)).ToArray());
    }

    [Fact]
    public void ContinuousUsesExactPoolAndExclusiveRepeatingMilestones()
    {
        var profile = Load();
        var result = GamblerNavigationSimulation.Run(profile,
            Request("Gambler.ContinuousBetting", middle: 1));

        AssertSafeUnitMass(result, 1, 3);
        Assert.Equal([new Rational(3, 10), new Rational(315, 10000),
                new Rational(6685, 10000)],
            profile.Pool("continuous-middle-gamble").Members.Select(member =>
                member.Probability.Value).ToArray());
        Assert.Equal(profile.Pool("continuous-middle-gamble").Members.ToDictionary(
            member => member.Id, member => member.Probability.Value),
            RewardMasses(result, profile.Pool("continuous-middle-gamble").Members
                .Select(member => member.Id)));
        Assert.Equal([GamblerActionTier.High, GamblerActionTier.RareReroll],
            result.DisabledTiers.ToArray());
        Assert.Equal(new[] { ("lumber", 1), ("random_wisp", 1) },
            Rewards(GamblerNavigationSimulation.ContinuousMilestone(profile, 6)));
        Assert.Equal(new[] { ("ship", 1) },
            Rewards(GamblerNavigationSimulation.ContinuousMilestone(profile, 12)));
        Assert.Equal(new[] { ("ship", 1) },
            Rewards(GamblerNavigationSimulation.ContinuousMilestone(profile, 24)));
    }

    [Fact]
    public void IdenticalRouteOutcomesMergeAfterEveryActionAndRemainDeterministicAtLimit()
    {
        var profile = Load();
        var pools = profile.Pool("casino-normal").Members.ToImmutableDictionary(
            member => member.Id,
            member => new GamblerRoutePool([
                new GamblerWeightedRouteOutcome("same", new Rational(1, 1),
                    GamblerOutcomeDelta.Empty)]), StringComparer.Ordinal);
        var request = Request("Gambler.Casino", high: 64, routePools: pools);

        var first = GamblerNavigationSimulation.Run(profile, request);
        var second = GamblerNavigationSimulation.Run(profile, request);

        AssertSafeUnitMass(first, 64, 1);
        Assert.Equal(first.ArithmeticDisposition, second.ArithmeticDisposition);
        Assert.Equal(first.WaveDisposition, second.WaveDisposition);
        Assert.Equal(first.TotalMass, second.TotalMass);
        Assert.Equal(first.Outcomes.Select(outcome =>
                (outcome.State.CanonicalKey, outcome.Mass)),
            second.Outcomes.Select(outcome =>
                (outcome.State.CanonicalKey, outcome.Mass)));
        Assert.Equal(64, first.Outcomes.Single().State.GambleActions);
    }

    [Fact]
    public void DisabledBudgetsAreNeverExecutedOrLeakedIntoMass()
    {
        var profile = Load();
        var casino = GamblerNavigationSimulation.Run(profile,
            Request("Gambler.Casino", low: 64, middle: 64));
        var continuous = GamblerNavigationSimulation.Run(profile,
            Request("Gambler.ContinuousBetting", high: 64));

        AssertSafeUnitMass(casino, 0, 1);
        AssertSafeUnitMass(continuous, 0, 1);
    }

    [Fact]
    public void LimitsFailClosedWithoutTruncationOrSampling()
    {
        var profile = Load();
        AssertLimit(GamblerNavigationSimulation.Run(profile,
            Request("Gambler.Casino", high: 65)), NavigationLimitKind.GambleActions, 65);

        var stateOverflow = new GamblerRoutePool(Enumerable.Range(0, 65_537)
            .Select(index => new GamblerWeightedRouteOutcome(index.ToString(),
                new Rational(1, 65_537), new GamblerOutcomeDelta([
                    new GamblerRewardQuantity($"route-{index}", 1)])))
            .ToImmutableArray());
        AssertLimit(GamblerNavigationSimulation.Run(profile,
            Request("Gambler.Casino", high: 1,
                routePools: ImmutableDictionary<string, GamblerRoutePool>.Empty
                    .Add("random-rare", stateOverflow))),
            NavigationLimitKind.AggregatedStates, 65_537);

        var denominator = BigInteger.One << 262_144;
        var rationalOverflow = new GamblerRoutePool([
            new GamblerWeightedRouteOutcome("tiny", new Rational(1, denominator),
                GamblerOutcomeDelta.Empty),
            new GamblerWeightedRouteOutcome("rest", new Rational(denominator - 1, denominator),
                GamblerOutcomeDelta.Empty)]);
        AssertLimit(GamblerNavigationSimulation.Run(profile,
            Request("Gambler.Casino", high: 1,
                routePools: ImmutableDictionary<string, GamblerRoutePool>.Empty
                    .Add("random-rare", rationalOverflow))),
            NavigationLimitKind.RationalBits, 262_145);
    }

    private static GamblerSimulationRequest Request(string optionId, int low = 0,
        int middle = 0, int high = 0, int world = 0, bool isekai = false,
        ImmutableDictionary<string, GamblerRoutePool>? routePools = null) =>
        new(optionId, new GamblerObservedActionBudget(low, middle, high, world),
            isekai, GamblerPlannerOutcomeState.Empty,
            routePools ?? ImmutableDictionary<string, GamblerRoutePool>.Empty);

    private static Dictionary<string, Rational> RewardMasses(GamblerSimulationResult result,
        IEnumerable<string> rewardIds)
    {
        var expected = rewardIds.ToHashSet(StringComparer.Ordinal);
        return
        result.Outcomes.SelectMany(outcome => outcome.State.Rewards
                .Where(reward => reward.Count == 1 && expected.Contains(reward.Id))
                .Select(reward => (reward.Id, outcome.Mass)))
            .GroupBy(value => value.Id).ToDictionary(group => group.Key,
                group => group.Aggregate(new Rational(0, 1),
                    (sum, value) => sum + value.Mass));
    }

    private static (string Id, int Count)[] Rewards(GamblerOutcomeDelta delta) =>
        delta.Rewards.Select(reward => (reward.Id, reward.Count)).ToArray();

    private static void AssertSafeUnitMass(GamblerSimulationResult result,
        int actions, int states)
    {
        Assert.Equal(ArithmeticDisposition.Allowed, result.ArithmeticDisposition);
        Assert.Equal(WaveDisposition.SafeRecommendation, result.WaveDisposition);
        Assert.Equal(new Rational(1, 1), result.TotalMass);
        Assert.Equal(actions, result.ExecutedActions);
        Assert.Equal(states, result.Outcomes.Length);
    }

    private static void AssertLimit(GamblerSimulationResult result,
        NavigationLimitKind kind, int requested)
    {
        Assert.Equal(ArithmeticDisposition.ArithmeticLimitExceeded,
            result.ArithmeticDisposition);
        Assert.Equal(WaveDisposition.NoSafeRecommendation, result.WaveDisposition);
        Assert.Equal(kind, result.ExceededLimit);
        Assert.Equal(requested, result.Requested);
        Assert.Empty(result.Outcomes);
    }

    private static NavigationMechanicsProfile Load() =>
        NavigationMechanicsProfileLoader.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Data"));
}
