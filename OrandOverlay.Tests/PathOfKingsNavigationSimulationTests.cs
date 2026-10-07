using System.Collections.Immutable;
using System.Numerics;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class PathOfKingsNavigationSimulationTests
{
    private static readonly NavigationMechanicsProfile Profile =
        NavigationMechanicsProfileLoader.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void MartialAndAllTwoTopPathOptionsAreHardIneligible()
    {
        var simulation = new PathOfKingsNavigationSimulation(Profile);

        Assert.Equal(PathOfKingsDisposition.HardIneligible,
            simulation.SimulateMartial(Input(plannedTopCount: 1)).Disposition);
        Assert.Equal(PathOfKingsDisposition.HardIneligible,
            simulation.SimulateMartial(Input(plannedTopCount: 2)).Disposition);
        Assert.Equal(PathOfKingsDisposition.HardIneligible,
            simulation.SimulateBounty(Input(plannedTopCount: 2)).Disposition);
        Assert.Equal(PathOfKingsDisposition.HardIneligible,
            simulation.SimulateRoyal(Input(plannedTopCount: 2)).Disposition);
    }

    [Fact]
    public void CategoryBasicsPreserveExactSlowBossLumberAndOneTopLimit()
    {
        var result = new PathOfKingsNavigationSimulation(Profile)
            .SimulateBounty(Input(remainingBosses: 3));

        Assert.Equal(new Rational(7, 100), result.Category.LineMovementReduction);
        Assert.Equal(2, result.Category.LumberPerBoss);
        Assert.Equal(6, result.Category.TotalBossLumber);
        Assert.Equal(1, result.Category.TopUnitLimit);
    }

    [Fact]
    public void BountyRunsExactDryStreakDpAndPreservesChestPool()
    {
        var result = new PathOfKingsNavigationSimulation(Profile).SimulateBounty(
            Input(futureKills: 2, chestDeltas:
            [
                new("lumber", new SignedRouteDelta(100)),
                new("random-wisp", new SignedRouteDelta(200)),
                new("gold", new SignedRouteDelta(300))
            ]));

        Assert.Equal(PathOfKingsDisposition.Eligible, result.Disposition);
        Assert.Equal(new Rational(1, 1), result.Bounty!.ProbabilityMass);
        Assert.Equal(new Rational(12499, 12500000), result.Bounty.ExpectedChestCount);
        Assert.Equal([new Rational(1, 5), new Rational(1, 5), new Rational(3, 5)],
            result.Bounty.ChestOutcomes.Select(value => value.Probability));
        Assert.Equal([0, 1, 2], result.Bounty.TerminalStates.Select(value => value.DryStreak));
        Assert.Equal(new PathSignedRational(37497, 156250),
            result.Bounty.ExpectedChestRouteDelta);
    }

    [Fact]
    public void Bounty4096PassesFinalScoreInputAnd4097FailsClosed()
    {
        var simulation = new PathOfKingsNavigationSimulation(Profile);

        var atLimit = simulation.SimulateBounty(Input(futureKills: 4096));
        var overLimit = simulation.SimulateBounty(Input(futureKills: 4097));

        Assert.Equal(PathOfKingsDisposition.Eligible, atLimit.Disposition);
        Assert.Equal(4096, atLimit.FinalScoreInputs.Single().ExecutedKills);
        Assert.Equal(new Rational(1, 1), atLimit.Bounty!.ProbabilityMass);
        Assert.Equal(PathOfKingsDisposition.ArithmeticLimitExceeded, overLimit.Disposition);
        Assert.Empty(overLimit.FinalScoreInputs);
    }

    [Fact]
    public void TreasureMapBountyFailsClosedInsteadOfGuessingChestDistribution()
    {
        var result = new PathOfKingsNavigationSimulation(Profile).SimulateBounty(
            Input(treasureMapActive: true));

        Assert.Equal(PathOfKingsDisposition.UnknownInput, result.Disposition);
        Assert.Empty(result.FinalScoreInputs);
    }

    [Fact]
    public void RoyalUsesTransformedCooldownForAttacksAndBaseCooldownForExactProc()
    {
        var fixture = Profile.Fixture("royal-piecewise-proc-branches");
        var input = Input(royalScenarios: fixture.SemanticInput.RoyalAttackScenarios);

        var result = new PathOfKingsNavigationSimulation(Profile).SimulateRoyal(input);

        Assert.Equal(PathOfKingsDisposition.Eligible, result.Disposition);
        Assert.Equal(fixture.SemanticExpected.RoyalBranches,
            result.RoyalScenarios.Select(value => value.Mechanics));
        Assert.Equal([0, 1, 0, 1], result.FinalScoreInputs
            .Select(value => value.TargetsWithin400));
        Assert.All(result.RoyalScenarios, value =>
            Assert.Equal(value.Mechanics.ProcBaseWeaponCooldown.Value,
                value.FinalScoreInput.BaseWeaponCooldown));
        Assert.Equal([12, 7, 3, 2], result.RoyalScenarios
            .Select(value => value.Mechanics.AttackCountDelta));
        Assert.Equal([
            NavigationRoyalPiecewiseBranch.CooldownProductMinimum,
            NavigationRoyalPiecewiseBranch.CooldownProductMinimum,
            NavigationRoyalPiecewiseBranch.CooldownProductMinimum,
            NavigationRoyalPiecewiseBranch.SlowCooldownMinimum],
            result.RoyalScenarios.Select(value => value.Mechanics.Branch));
    }

    private static PathOfKingsSimulationInput Input(
        int plannedTopCount = 1,
        int remainingBosses = 0,
        int futureKills = 0,
        bool treasureMapActive = false,
        ImmutableArray<PathChestRouteDelta> chestDeltas = default,
        ImmutableArray<NavigationRoyalAttackInput> royalScenarios = default) => new(
            plannedTopCount, remainingBosses, futureKills, 0, treasureMapActive,
            chestDeltas, royalScenarios);
}
