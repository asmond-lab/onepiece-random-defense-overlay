using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BestHelpNavigationSimulationMaximumTests
{
    [Fact]
    public void MaximumUsesSourceBusterUpgradePotionTotalAndTargetCount()
    {
        var result = Simulation().SimulateMaximum(new()
        {
            CurrentMana = 0,
            HorizonSeconds = 0,
            RemainingEnemyEffectiveHp = 60_000_000,
            SpellScenarios = BestHelpNavigationSimulationTestData.SpellScenarios(
                busterTargets: 3)
        });

        Assert.Equal(BestHelpSimulationDisposition.Confirmed, result.Disposition);
        Assert.Equal(900, result.Outcome!.AddedPotionMana);
        var buster = Assert.Single(result.Outcome.Casts,
            cast => cast.SpellId == "A0BZ");
        Assert.Equal(333, buster.ManaCost);
        Assert.Equal(30_000_000, buster.Damage);
        Assert.Equal(30_000_000, result.IncrementalDamage);
        Assert.Equal(5_000, result.DamageGainBp);
        Assert.Equal(3_500, result.CombatBp);
    }

    [Fact]
    public void MaximumSharedManaCannotDoubleFundConflictingCasts()
    {
        var result = Simulation().SimulateMaximum(new()
        {
            CurrentMana = 0,
            HorizonSeconds = 0,
            RemainingEnemyEffectiveHp = 100_000_000,
            SpellScenarios = BestHelpNavigationSimulationTestData.SpellScenarios(
                poisonLevel2Damage: 9_000_000)
        });

        Assert.Equal(900, result.Outcome!.AddedPotionMana);
        Assert.Contains(result.Outcome.Casts, cast => cast.SpellId == "A0BZ");
        Assert.DoesNotContain(result.Outcome.Casts, cast => cast.SpellId == "A07X");
        Assert.True(result.Outcome.Casts.Sum(cast => cast.ManaCost) <= 900);
    }

    [Fact]
    public void MaximumHonorsExactLevelCooldownBoundary()
    {
        var result = Simulation().SimulateMaximum(new()
        {
            CurrentMana = 666,
            HorizonSeconds = 66,
            RemainingEnemyEffectiveHp = 100_000_000,
            SpellScenarios = BestHelpNavigationSimulationTestData.SpellScenarios()
        });

        Assert.Equal([0, 66], result.Outcome!.Casts
            .Where(cast => cast.SpellId == "A0BZ")
            .Select(cast => cast.TimeSeconds));
        Assert.Single(result.Baseline!.Casts, cast => cast.SpellId == "A0BZ");
    }

    [Fact]
    public void MaximumUnknownObjectEffectIsScenarioGated()
    {
        var scenarios = BestHelpNavigationSimulationTestData.SpellScenarios()
            .Where(value => value.SpellId != "A07T").ToImmutableArray();

        var result = Simulation().SimulateMaximum(new()
        {
            CurrentMana = 1000,
            HorizonSeconds = 10,
            RemainingEnemyEffectiveHp = 1,
            SpellScenarios = scenarios
        });

        Assert.Equal(BestHelpSimulationDisposition.ScenarioGated, result.Disposition);
        Assert.Contains("A07T", result.UnknownSpellIds);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public void MaximumControlDeltaContributesThirtyPercentOfCombatBp()
    {
        var result = Simulation().SimulateMaximum(new()
        {
            CurrentMana = 0,
            HorizonSeconds = 0,
            RemainingEnemyEffectiveHp = 1,
            RemainingControlDeficits = [6],
            SpellScenarios = BestHelpNavigationSimulationTestData.SpellScenarios(
                busterTargets: 0)
        });

        Assert.Equal(3, result.IncrementalControlTargetSeconds);
        Assert.Equal(5_000, result.ControlGainBp);
        Assert.Equal(1_500, result.CombatBp);
    }

    [Fact]
    public void MaximumLevelOneBaselineReceivesBestHelpManaRegeneration()
    {
        var result = Simulation().SimulateMaximum(new()
        {
            CurrentMana = 493,
            HorizonSeconds = 10,
            RemainingEnemyEffectiveHp = 100_000_000,
            SpellScenarios = BestHelpNavigationSimulationTestData.SpellScenarios()
        });

        var baselineBuster = Assert.Single(result.Baseline!.Casts,
            cast => cast.SpellId == "A0BZ");
        Assert.Equal(10, baselineBuster.TimeSeconds);
        Assert.Equal(500, baselineBuster.ManaCost);
    }

    private static BestHelpNavigationSimulation Simulation() => new(
        BestHelpNavigationSimulationTestData.Profile());
}
