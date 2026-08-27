using System.Numerics;

namespace OrandOverlay;

internal static class NavigationIntervalScorerScenario
{
    private static readonly Rational One = new(1, 1);
    private static readonly Rational QuantileThreshold = new(19, 20);

    public static bool TryEvaluate(
        NavigationCoupledScenario scenario,
        int beforeUtilityBp,
        int maxRationalBits,
        out NavigationScenarioScore? score)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (string.IsNullOrWhiteSpace(scenario.Id) || scenario.Outcomes.IsDefaultOrEmpty)
            throw new ArgumentException("A coupled scenario needs an id and outcomes.", nameof(scenario));
        if (maxRationalBits <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRationalBits));

        var deltas = new List<(NavigationScoringOutcome Outcome, int Delta)>();
        foreach (var outcome in scenario.Outcomes)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            ValidateOutcome(outcome);
            if (NavigationIntervalScorerMath.MaxBitLength(outcome.Probability) >
                maxRationalBits)
            {
                score = null;
                return false;
            }
            var after = NavigationIntervalScorerMath.StateUtilityBp(
                outcome.AfterBuildBp, outcome.AfterCoreBp, outcome.AfterCombatBp);
            deltas.Add((outcome, checked(after - beforeUtilityBp)));
        }
        var commonDenominator = deltas.All(item => item.Outcome.Probability.Denominator ==
            deltas[0].Outcome.Probability.Denominator);
        var moments = commonDenominator
            ? CommonDenominatorMoments(deltas)
            : GeneralMoments(deltas);
        var mass = moments.Mass;
        var expected = moments.Expected;
        if (mass != One)
            throw new ArgumentException("Outcome probability mass must equal one.", nameof(scenario));
        var expectedSquare = NavigationIntervalScorerMath.Square(expected);
        var variance = NavigationIntervalScorerMath.Subtract(
            moments.SecondMoment, expectedSquare);
        if (!WithinLimit(maxRationalBits, mass, expected, moments.SecondMoment,
                expectedSquare, variance))
        {
            score = null;
            return false;
        }

        var standardDeviation = NavigationIntervalScorerMath.FloorSquareRoot(variance);
        var quantile95 = Quantile95(deltas, commonDenominator);
        var floor = deltas.Min(item => item.Outcome.AfterCoreBp);
        var recovery = RecoveryProbability(deltas, commonDenominator);
        var value = NavigationIntervalScorerMath.ClampBasisPoints(checked(5_000 +
            NavigationIntervalScorerMath.RoundAwayFromZero(
                NavigationIntervalScorerMath.Divide(expected, 2))));
        var upper = NavigationIntervalScorerMath.ClampBasisPoints(checked(
            standardDeviation + NavigationIntervalScorerMath.RoundAwayFromZero(
                NavigationIntervalScorerMath.Subtract(
                    NavigationIntervalScorerMath.FromInteger(quantile95), expected))));
        var captureConflict = CaptureConflict(expected, beforeUtilityBp);
        var navigationScore = checked((40 * floor + 25 * value + 15 * upper +
            10 * 10_000 + 10 * captureConflict + 50) / 100);

        score = new NavigationScenarioScore(
            scenario.Id, floor, value, upper, 10_000, captureConflict,
            navigationScore, expected, variance, standardDeviation, quantile95, recovery);
        return true;
    }

    private static int CaptureConflict(
        NavigationSignedRational expected,
        int beforeUtilityBp)
    {
        NavigationSignedRational normalized;
        if (expected.Numerator >= 0)
        {
            normalized = NavigationIntervalScorerMath.Divide(
                expected, Math.Max(1, 10_000 - beforeUtilityBp));
        }
        else
        {
            normalized = NavigationIntervalScorerMath.Divide(
                expected, Math.Max(1, beforeUtilityBp));
        }
        var adjustment = NavigationIntervalScorerMath.RoundAwayFromZero(
            NavigationIntervalScorerMath.Multiply(
                NavigationIntervalScorerMath.FromInteger(5_000), normalized));
        return NavigationIntervalScorerMath.ClampBasisPoints(checked(5_000 + adjustment));
    }

    private static (Rational Mass, NavigationSignedRational Expected,
        Rational SecondMoment) CommonDenominatorMoments(
        IEnumerable<(NavigationScoringOutcome Outcome, int Delta)> outcomes)
    {
        var materialized = outcomes.ToArray();
        var denominator = materialized[0].Outcome.Probability.Denominator;
        var mass = materialized.Aggregate(BigInteger.Zero,
            (sum, item) => sum + item.Outcome.Probability.Numerator);
        var expected = materialized.Aggregate(BigInteger.Zero,
            (sum, item) => sum + item.Outcome.Probability.Numerator * item.Delta);
        var second = materialized.Aggregate(BigInteger.Zero,
            (sum, item) => sum + item.Outcome.Probability.Numerator * item.Delta * item.Delta);
        return (new Rational(mass, denominator),
            new NavigationSignedRational(expected, denominator),
            new Rational(second, denominator));
    }

    private static (Rational Mass, NavigationSignedRational Expected,
        Rational SecondMoment) GeneralMoments(
        IEnumerable<(NavigationScoringOutcome Outcome, int Delta)> outcomes)
    {
        var mass = new Rational(0, 1);
        var expected = NavigationSignedRational.Zero;
        var second = new Rational(0, 1);
        foreach (var item in outcomes)
        {
            mass = NavigationIntervalScorerMath.Add(mass, item.Outcome.Probability);
            expected = NavigationIntervalScorerMath.Add(expected,
                NavigationIntervalScorerMath.Multiply(
                    NavigationIntervalScorerMath.FromInteger(item.Delta),
                    item.Outcome.Probability));
            second = NavigationIntervalScorerMath.Add(second,
                NavigationIntervalScorerMath.Multiply(item.Outcome.Probability,
                    new Rational((BigInteger)item.Delta * item.Delta, 1)));
        }
        return (mass, expected, second);
    }

    private static int Quantile95(
        IEnumerable<(NavigationScoringOutcome Outcome, int Delta)> outcomes,
        bool commonDenominator)
    {
        var ordered = outcomes.OrderBy(item => item.Delta).ToArray();
        if (commonDenominator)
        {
            var denominator = ordered[0].Outcome.Probability.Denominator;
            var numerator = BigInteger.Zero;
            foreach (var item in ordered)
            {
                numerator += item.Outcome.Probability.Numerator;
                if (numerator * 20 >= denominator * 19)
                    return item.Delta;
            }
            throw new InvalidOperationException("Probability mass was not complete.");
        }
        var cumulative = new Rational(0, 1);
        foreach (var item in ordered)
        {
            cumulative = NavigationIntervalScorerMath.Add(
                cumulative, item.Outcome.Probability);
            if (NavigationIntervalScorerMath.Compare(cumulative, QuantileThreshold) >= 0)
                return item.Delta;
        }
        throw new InvalidOperationException("Probability mass was not complete.");
    }

    private static Rational RecoveryProbability(
        IEnumerable<(NavigationScoringOutcome Outcome, int Delta)> outcomes,
        bool commonDenominator)
    {
        var recovered = outcomes.Where(item => item.Outcome.AfterCoreBp == 10_000).ToArray();
        if (recovered.Length == 0)
            return new Rational(0, 1);
        if (commonDenominator)
            return new Rational(recovered.Aggregate(BigInteger.Zero,
                    (sum, item) => sum + item.Outcome.Probability.Numerator),
                recovered[0].Outcome.Probability.Denominator);
        return recovered.Aggregate(new Rational(0, 1),
            (sum, item) => NavigationIntervalScorerMath.Add(
                sum, item.Outcome.Probability));
    }

    private static bool WithinLimit(int limit, params object[] values)
    {
        foreach (var value in values)
        {
            var bits = value switch
            {
                Rational rational => NavigationIntervalScorerMath.MaxBitLength(rational),
                NavigationSignedRational signed =>
                    NavigationIntervalScorerMath.MaxBitLength(signed),
                _ => throw new ArgumentOutOfRangeException(nameof(values))
            };
            if (bits > limit)
                return false;
        }
        return true;
    }

    private static void ValidateOutcome(NavigationScoringOutcome outcome)
    {
        NavigationIntervalScorerMath.ValidateBasisPoints(outcome.AfterBuildBp);
        NavigationIntervalScorerMath.ValidateBasisPoints(outcome.AfterCoreBp);
        NavigationIntervalScorerMath.ValidateBasisPoints(outcome.AfterCombatBp);
    }
}
