using Xunit;
using System.Collections.Immutable;
using System.Numerics;

namespace OrandOverlay.Tests;

public sealed class NavigationIntervalScorerMathTests
{
    [Theory]
    [InlineData(10_000, 0, 0, 6_000)]
    [InlineData(0, 10_000, 0, 2_500)]
    [InlineData(0, 0, 10_000, 1_500)]
    [InlineData(5_001, 5_001, 5_001, 5_001)]
    public void StateUtilityUsesNormativeBuildCoreCombatWeights(
        int buildBp,
        int coreBp,
        int combatBp,
        int expected)
    {
        Assert.Equal(expected,
            NavigationIntervalScorerMath.StateUtilityBp(buildBp, coreBp, combatBp));
    }

    [Fact]
    public void ExactDistributionProducesNormativeMomentsAndRoundedComponents()
    {
        var result = ScoreScenario(5_000,
            (new Rational(1, 2), 4_000),
            (new Rational(1, 2), 7_000));

        Assert.Equal(new NavigationSignedRational(500, 1), result.ExpectedUplift);
        Assert.Equal(new Rational(2_250_000, 1), result.Variance);
        Assert.Equal(1_500, result.StandardDeviation);
        Assert.Equal(2_000, result.Quantile95);
        Assert.Equal(4_000, result.FloorBp);
        Assert.Equal(5_250, result.ValueBp);
        Assert.Equal(3_000, result.UpperBp);
        Assert.Equal(5_500, result.CaptureConflictBp);
        Assert.Equal(4_913, result.NavigationScoreBp);
        Assert.Equal(new Rational(0, 1), result.RecoveryProbability);
    }

    [Fact]
    public void FloorSquareRootAndHalfRoundingStayExact()
    {
        Assert.Equal(2, NavigationIntervalScorerMath.FloorSquareRoot(
            new Rational(35, 6)));
        Assert.Equal(1, NavigationIntervalScorerMath.RoundAwayFromZero(
            new NavigationSignedRational(1, 2)));
        Assert.Equal(-1, NavigationIntervalScorerMath.RoundAwayFromZero(
            new NavigationSignedRational(-1, 2)));
    }

    [Fact]
    public void CombatOnlyAndResourceOnlyEffectsChangeOnlyTheirWeightedUtilityShare()
    {
        var royal = ScoreScenario(0,
            new NavigationScoringOutcome(new Rational(1, 1), 0, 0, 10_000));
        var maximum = ScoreScenario(0,
            new NavigationScoringOutcome(new Rational(1, 1), 0, 0, 8_000));
        var resource = ScoreScenario(0,
            new NavigationScoringOutcome(new Rational(1, 1), 0, 10_000, 0));

        Assert.Equal(new NavigationSignedRational(1_500, 1), royal.ExpectedUplift);
        Assert.Equal(new NavigationSignedRational(1_200, 1), maximum.ExpectedUplift);
        Assert.Equal(new NavigationSignedRational(2_500, 1), resource.ExpectedUplift);
    }

    [Fact]
    public void ComponentsSaturateWithoutFloatingPoint()
    {
        var gain = ScoreScenario(0,
            new NavigationScoringOutcome(new Rational(1, 1), 10_000, 10_000, 10_000));
        var loss = ScoreScenario(10_000,
            new NavigationScoringOutcome(new Rational(1, 1), 0, 0, 0));

        Assert.Equal(10_000, gain.ValueBp);
        Assert.Equal(10_000, gain.CaptureConflictBp);
        Assert.Equal(0, loss.ValueBp);
        Assert.Equal(0, loss.CaptureConflictBp);
    }

    [Fact]
    public void MalformedProbabilityMassIsRejectedAtTheScoringBoundary()
    {
        var malformed = new NavigationCoupledScenario("malformed",
        [
            new NavigationScoringOutcome(new Rational(1, 2), 5_000, 5_000, 5_000)
        ]);

        Assert.Throws<ArgumentException>(() =>
            NavigationIntervalScorerScenario.TryEvaluate(
                malformed, 5_000, 262_144, out _));
    }

    private static NavigationScenarioScore ScoreScenario(int before,
        params (Rational Probability, int Utility)[] outcomes) =>
        ScoreScenario(before, outcomes.Select(item => new NavigationScoringOutcome(
            item.Probability, item.Utility, item.Utility, item.Utility)).ToArray());

    private static NavigationScenarioScore ScoreScenario(int before,
        params NavigationScoringOutcome[] outcomes)
    {
        var scenario = new NavigationCoupledScenario("fixture", outcomes.ToImmutableArray());
        Assert.True(NavigationIntervalScorerScenario.TryEvaluate(
            scenario, before, 262_144, out var result));
        return Assert.IsType<NavigationScenarioScore>(result);
    }
}
