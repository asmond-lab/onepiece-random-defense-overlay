using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletTrait2320Tests
{
    private static BulletUpgradeObservationContext Context() => new(BulletMapVersion.V2320,
        BulletMechanics.Script2320, 1, 123, "session", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    private static BulletUpgradeAbilityObservation Observation(BulletUpgradeObservationContext context) => new(
        context.MapVersion, context.MapScriptSha256, BulletUpgradeObservationSource.VerifiedCompleteAbilityList,
        context.Owner, context.EngineHandle, "h081", context.SessionId, context.NowUtc, true, 3, 3, 3);
    [Fact]
    public void ActualSelectedObjectFieldsAre50NotTooltip48AndFormulasStillClamp()
    {
        var context = Context(); var baseline = Observation(context);
        Assert.Equal(.4, baseline.ObjectValue(BulletUpgradeAbility.Attack, context));
        Assert.Equal(.4, baseline.ObjectValue(BulletUpgradeAbility.Speed, context));
        Assert.Equal(-40, baseline.ObjectValue(BulletUpgradeAbility.Armor, context));
        foreach (var ability in Enum.GetValues<BulletUpgradeAbility>())
        {
            var selected = ability switch { BulletUpgradeAbility.Attack => baseline with { AttackLevel = 4 },
                BulletUpgradeAbility.Speed => baseline with { SpeedLevel = 4 }, _ => baseline with { ArmorLevel = 4 } };
            Assert.Equal(BulletTraitState.ConfirmedSelected, selected.Trait(ability, context));
            Assert.Equal(ability == BulletUpgradeAbility.Armor ? -50 : .5, selected.ObjectValue(ability, context));
            Assert.Equal(BulletMechanics.Evaluate(BulletFormula.StrikeInitial, baseline, context),
                BulletMechanics.Evaluate(BulletFormula.StrikeInitial, selected, context));
        }
        Assert.Equal(48, BulletUpgradeAbilityObservation.SelectedTraitTooltipClaim);
    }
    [Fact]
    public void IdentitySourceSessionFreshnessAndInvalidLevelsFailClosed()
    {
        var context = Context(); var good = Observation(context);
        foreach (var bad in new[] { good with { MapVersion = BulletMapVersion.V2314 }, good with { MapScriptSha256 = "wrong" },
            good with { Owner = 2 }, good with { EngineHandle = 124 }, good with { Rawcode = "h018" },
            good with { SessionId = "other" }, good with { Complete = false }, good with { AttackLevel = -1 },
            good with { ArmorLevel = 5 }, good with { AttackLevel = 4, SpeedLevel = 4 },
            good with { Source = BulletUpgradeObservationSource.Simulation }, good with { Source = BulletUpgradeObservationSource.OfflineFixture },
            good with { ObservedAtUtc = context.NowUtc.AddSeconds(-6) }, good with { ObservedAtUtc = context.NowUtc.AddSeconds(1) } })
        {
            Assert.False(bad.IsValid(context));
            Assert.Null(BulletMechanics.Evaluate(BulletFormula.StrikeInitial, bad, context));
            Assert.Equal(BulletTraitState.Unknown, bad.Trait(BulletUpgradeAbility.Armor, context));
        }
        Assert.False(good.IsValid(context with { MaximumAge = TimeSpan.Zero }));
        Assert.Null((good with { ArmorLevel = null }).ObjectValue(BulletUpgradeAbility.Armor, context));
    }
    [Fact]
    public void RuntimeAdviceSeparatesBaselineConfirmedAndUnknownWithoutInferringTraitFromTier()
    {
        var context = Context(); var observation = Observation(context);
        var runtime = new BulletGuideRuntimeState(true, 3, 3, 3, "test") { MapVersion = BulletMapVersion.V2320 };
        Assert.Equal(40, runtime.BaselineArmorReduction);
        Assert.Null(runtime.BulletArmorReduction);
        Assert.Equal(BulletTraitState.Unknown, runtime.Trait(BulletUpgradeAbility.Armor));
        Assert.Null(runtime.BulletArmorReduction);
        var baseline = runtime.WithUpgradeAbilities(observation, context);
        Assert.Equal(BulletTraitState.Baseline, baseline.Trait(BulletUpgradeAbility.Armor));
        Assert.Equal(40, baseline.BulletArmorReduction);
        var selected = runtime.WithUpgradeAbilities(observation with { ArmorLevel = 4 }, context);
        Assert.Equal(BulletTraitState.ConfirmedSelected, selected.Trait(BulletUpgradeAbility.Armor));
        Assert.Equal(50, selected.BulletArmorReduction);
        Assert.Equal(3, selected.ArmorTier);
        Assert.DoesNotContain("A0SJ", BulletGuideAdvice.UpgradeEffects(selected));
        Assert.DoesNotContain("A0WU", BulletGuideAdvice.UpgradeEffects(selected));
        Assert.Null((selected with { IsCurrent = false }).BulletArmorReduction);
        Assert.Null(runtime.WithUpgradeAbilities(observation with { Source = BulletUpgradeObservationSource.Simulation }, context).BulletArmorReduction);
    }
    [Fact]
    public void NewMapAndClientCannotActivateLegacyReader()
    {
        var reader = new BulletGuideRuntimeReader((_, _) => throw new Exception("must not read"),
            () => throw new Exception("must not discover"));
        Assert.False(reader.Read("3.0", BulletMechanics.Script2320, 1, true).IsCurrent);
        Assert.False(reader.Read("2.0.4.23745", BulletMechanics.Script2320, 1, true).IsCurrent);
    }
}
