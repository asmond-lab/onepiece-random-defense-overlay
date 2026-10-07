using System.Collections.Immutable;
using System.Numerics;

namespace OrandOverlay;

internal static class PathOfKingsBountyDp
{
    public static PathBountyResult Run(NavigationMechanicsProfile profile, int kills,
        int initialDryStreak, ImmutableArray<PathChestOutcome> chestOutcomes)
    {
        var bounty = profile.Bounty;
        var certain = DivideCeiling(bounty.RollDenominator -
            bounty.InitialChanceNumerator, bounty.DryStreakIncrement);
        if (initialDryStreak > certain)
            throw new ArgumentOutOfRangeException(nameof(initialDryStreak));
        var weights = new BigInteger[initialDryStreak + 1];
        weights[initialDryStreak] = BigInteger.One;
        var chestMoment = BigInteger.Zero;
        var denominator = BigInteger.One;

        for (var kill = 0; kill < kills; kill++)
        {
            var length = Math.Min(weights.Length + 1, certain + 1);
            var nextWeights = new BigInteger[length];
            var successWeight = BigInteger.Zero;
            for (var dryStreak = 0; dryStreak < weights.Length; dryStreak++)
            {
                if (weights[dryStreak].IsZero) continue;
                var success = Math.Min(bounty.RollDenominator,
                    checked(bounty.InitialChanceNumerator +
                        bounty.DryStreakIncrement * dryStreak));
                var failure = bounty.RollDenominator - success;
                successWeight += weights[dryStreak] * success;
                if (failure == 0) continue;
                nextWeights[dryStreak + 1] += weights[dryStreak] * failure;
            }
            nextWeights[bounty.ResetDryStreak] = successWeight;
            weights = nextWeights;
            chestMoment = chestMoment * bounty.RollDenominator + successWeight;
            denominator *= bounty.RollDenominator;
        }

        var massNumerator = weights.Aggregate(BigInteger.Zero, (sum, value) => sum + value);
        var mass = new Rational(massNumerator, denominator);
        if (mass != new Rational(1, 1))
            throw new InvalidDataException("Bounty probability mass was not preserved");
        var expectedChests = new Rational(chestMoment, denominator);
        var expectedSingleChestDelta = chestOutcomes.Aggregate(PathSignedRational.Zero,
            (sum, value) => sum + new PathSignedRational(value.RouteDelta.Value, 1) *
                value.Probability);
        var terminal = weights.Select((value, dryStreak) =>
                (value, dryStreak)).Where(pair => !pair.value.IsZero)
            .Select(pair => new PathBountyTerminalState(pair.dryStreak,
                new Rational(pair.value, denominator)))
            .ToImmutableArray();
        return new PathBountyResult(kills, mass, expectedChests, terminal,
            chestOutcomes, expectedSingleChestDelta * expectedChests);
    }

    private static int DivideCeiling(int numerator, int denominator) =>
        checked((numerator + denominator - 1) / denominator);
}
