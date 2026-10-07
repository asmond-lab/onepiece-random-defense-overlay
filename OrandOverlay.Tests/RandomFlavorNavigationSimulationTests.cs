using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RandomFlavorNavigationSimulationTests
{
    [Fact]
    public void EveryNonRandomChildHasExactOneTwelfthMass()
    {
        var result = Simulate(RandomFlavor.Blue, actualTopCount: 0);

        Assert.Equal(RandomFlavorEligibility.Eligible, result.Eligibility);
        Assert.Equal(12, result.Outcomes.Length);
        Assert.Equal(new Rational(1, 1), result.ProbabilityMass);
        Assert.All(result.Outcomes,
            outcome => Assert.Equal(new Rational(1, 12), outcome.Probability));
        Assert.Equal(Enum.GetValues<RandomFlavorChildOption>(),
            result.Outcomes.Select(outcome => outcome.Option));
    }

    [Theory]
    [InlineData(RandomFlavor.Blue, RandomFlavorBonusKind.RandomWisp)]
    [InlineData(RandomFlavor.Green, RandomFlavorBonusKind.Lumber)]
    [InlineData(RandomFlavor.Yellow, RandomFlavorBonusKind.Absalom)]
    public void SuccessfulDispatchClearsVyAndAppliesFlavorBonusExactlyOnce(
        RandomFlavor flavor, RandomFlavorBonusKind expectedBonus)
    {
        var children = Children();
        var result = RandomFlavorNavigationSimulator.Simulate(
            flavor, new RandomFlavorRecommendationInput(0, false, children));

        Assert.All(result.Outcomes, outcome =>
        {
            var supplied = children.Single(child => child.Option == outcome.Option);
            Assert.True(outcome.DispatchSucceeded);
            Assert.False(outcome.VyPending);
            Assert.True(outcome.LyLocked);
            Assert.False(outcome.RerollAttempted);
            Assert.Same(supplied.Effect, outcome.AppliedChildEffect);
            Assert.Equal(new RandomFlavorBonus(expectedBonus, 1), outcome.Bonus);
        });
    }

    [Fact]
    public void OneTopLeavesFailedMartialAsLockedZeroRewardWithoutReroll()
    {
        var result = Simulate(RandomFlavor.Green, actualTopCount: 1);
        var martial = result.Outcomes.Single(outcome =>
            outcome.Option == RandomFlavorChildOption.MartialLaw);

        Assert.False(martial.DispatchSucceeded);
        Assert.True(martial.VyPending);
        Assert.True(martial.LyLocked);
        Assert.False(martial.RerollAttempted);
        Assert.Null(martial.AppliedChildEffect);
        Assert.Null(martial.Bonus);
        Assert.Equal(0, martial.RewardUnits);
        Assert.Equal(11, result.Outcomes.Count(outcome => outcome.DispatchSucceeded));
    }

    [Fact]
    public void TwoTopsLeaveEveryPathBranchAsLockedZeroRewardWithoutReroll()
    {
        var result = Simulate(RandomFlavor.Yellow, actualTopCount: 2);
        var failed = result.Outcomes.Where(outcome => !outcome.DispatchSucceeded).ToArray();

        Assert.Equal([
            RandomFlavorChildOption.MartialLaw,
            RandomFlavorChildOption.BountyHunter,
            RandomFlavorChildOption.RoyalLoader
        ], failed.Select(outcome => outcome.Option));
        Assert.All(failed, outcome =>
        {
            Assert.True(outcome.VyPending);
            Assert.True(outcome.LyLocked);
            Assert.False(outcome.RerollAttempted);
            Assert.Null(outcome.AppliedChildEffect);
            Assert.Null(outcome.Bonus);
            Assert.Equal(0, outcome.RewardUnits);
        });
    }

    [Theory]
    [InlineData(RandomFlavor.Blue)]
    [InlineData(RandomFlavor.Green)]
    [InlineData(RandomFlavor.Yellow)]
    public void ZeroTopSelectedPlanMakesFlavorHardIneligible(RandomFlavor flavor)
    {
        var result = RandomFlavorNavigationSimulator.Simulate(
            flavor, new RandomFlavorRecommendationInput(0, true, Children()));

        Assert.Equal(RandomFlavorEligibility.HardIncompatibleWithSelectedTop,
            result.Eligibility);
        Assert.Empty(result.Outcomes);
        Assert.Equal(new Rational(0, 1), result.ProbabilityMass);
    }

    [Fact]
    public void ChildInputIsClosedToTheTwelveNonRandomOptions()
    {
        Assert.Equal(12, Enum.GetValues<RandomFlavorChildOption>().Length);
        Assert.DoesNotContain(Enum.GetNames<RandomFlavorChildOption>(),
            name => name.Contains("Random", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => new RandomFlavorRecommendationInput(
            0, false, Children()[..11]));
        Assert.Throws<ArgumentException>(() => new RandomFlavorRecommendationInput(
            0, false, Children().SetItem(11, Children()[0])));
    }

    [Fact]
    public void ActualTopCountCannotBeNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RandomFlavorRecommendationInput(-1, false, Children()));
    }

    private static RandomFlavorSimulationResult Simulate(
        RandomFlavor flavor, int actualTopCount) =>
        RandomFlavorNavigationSimulator.Simulate(flavor,
            new RandomFlavorRecommendationInput(actualTopCount, false, Children()));

    private static ImmutableArray<RandomFlavorChildSimulation> Children() =>
        Enum.GetValues<RandomFlavorChildOption>()
            .Select((option, index) => new RandomFlavorChildSimulation(
                option, new RandomFlavorChildEffect($"effect-{option}", index + 1)))
            .ToImmutableArray();
}
