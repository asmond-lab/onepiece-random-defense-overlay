using Xunit;

namespace OrandOverlay.Tests;

public sealed class Bullet2322TraitGuideTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static BulletUpgradeObservationContext Context() => new(BulletMapVersion.V2322,
        BulletMechanics.Script2322, 1, 123, "session", Now, TimeSpan.FromSeconds(5));
    private static BulletUpgradeAbilityObservation Abilities(BulletUpgradeObservationContext context) => new(
        context.MapVersion, context.MapScriptSha256, BulletUpgradeObservationSource.VerifiedCompleteAbilityList,
        context.Owner, context.EngineHandle, "h081", context.SessionId, context.NowUtc, true, 3, 3, 3);

    [Fact]
    public void VerifiedChoiceNeedsExactLevelThreeSelectorsAndFiveNativeEligibleBodies()
    {
        var context = Context(); var abilities = Abilities(context);
        var eligible = new BulletTrait2322Eligibility(context, 5, true, true);
        foreach (var choice in Enum.GetValues<BulletUpgradeAbility>())
        {
            Assert.True(eligible.CanChoose(abilities, choice, context));
            var selected = choice switch
            {
                BulletUpgradeAbility.Attack => abilities with { AttackLevel = 4 },
                BulletUpgradeAbility.Speed => abilities with { SpeedLevel = 4 },
                _ => abilities with { ArmorLevel = 4 }
            };
            Assert.False(eligible.CanChoose(selected, choice, context));
            Assert.Equal(BulletTraitState.ConfirmedSelected, selected.Trait(choice, context));
            Assert.Equal(choice == BulletUpgradeAbility.Armor ? -50 : .5, selected.ObjectValue(choice, context));
        }
        Assert.False((eligible with { EligibleOwnedLiveUndeadCount = 4 }).CanChoose(abilities, BulletUpgradeAbility.Armor, context));
        Assert.False((eligible with { SelectorsObserved = false }).CanChoose(abilities, BulletUpgradeAbility.Armor, context));
        Assert.False((eligible with { VerifiedNativePopulation = false }).CanChoose(abilities, BulletUpgradeAbility.Armor, context));
        Assert.False(eligible.CanChoose(abilities with { ArmorLevel = 2 }, BulletUpgradeAbility.Armor, context));
        Assert.False(eligible.CanChoose(abilities with { ArmorLevel = 4 }, BulletUpgradeAbility.Armor, context));
        Assert.False(eligible.CanChoose(abilities with { ArmorLevel = null }, BulletUpgradeAbility.Armor, context));
    }

    [Fact]
    public void PopulationAndTraitRejectOtherVersionOwnerSessionStalenessAndUnverifiedSource()
    {
        var context = Context(); var abilities = Abilities(context);
        var eligible = new BulletTrait2322Eligibility(context, 5, true, true);
        var choice = BulletUpgradeAbility.Armor;
        foreach (var changed in new[] { context with { MapVersion = BulletMapVersion.V2320 },
            context with { MapScriptSha256 = BulletMechanics.Script2320 }, context with { Owner = 2 },
            context with { EngineHandle = 124 }, context with { SessionId = "other" },
            context with { NowUtc = Now.AddSeconds(6) }, context with { MaximumAge = TimeSpan.Zero } })
            Assert.False(eligible.CanChoose(abilities, choice, changed));
        Assert.False((eligible with { Context = context with { Owner = 2 } }).CanChoose(abilities, choice, context));
        Assert.False((eligible with { Context = context with { SessionId = "other" } }).CanChoose(abilities, choice, context));
        Assert.False(eligible.CanChoose(abilities with { Source = BulletUpgradeObservationSource.OfflineFixture }, choice, context));
        Assert.False(eligible.CanChoose(abilities with { Complete = false }, choice, context));
        Assert.False((abilities with { AttackLevel = 4, SpeedLevel = 4 }).IsValid(context));
    }

    [Fact]
    public void GuideSeparatesSourceCostFromObservedSelectionAndDoesNotForecastCombat()
    {
        var context = Context() with { NowUtc = DateTimeOffset.UtcNow, MaximumAge = TimeSpan.FromDays(1) };
        var runtime = new BulletGuideRuntimeState(true, 3, 3, 3, "fixture")
            { MapVersion = BulletMapVersion.V2322 };
        Assert.Null(runtime.BulletArmorReduction);
        var advice = BulletGuideAdvice.UpgradeEffects(runtime);
        Assert.DoesNotContain("UNIT_TYPE_UNDEAD", advice);
        Assert.DoesNotContain("A0SJ", advice);
        Assert.DoesNotContain("A0WU", advice);
        Assert.False(new BulletTrait2322Eligibility(context, 5, false, false)
            .CanChoose(Abilities(context), BulletUpgradeAbility.Armor, context));
        var selected = runtime.WithUpgradeAbilities(Abilities(context) with { ArmorLevel = 4 }, context);
        Assert.Equal(50, selected.BulletArmorReduction);
        Assert.Equal(BulletTraitState.ConfirmedSelected, selected.Trait(BulletUpgradeAbility.Armor));
        Assert.DoesNotContain("A0SJ", BulletGuideAdvice.UpgradeEffects(selected));
        Assert.Null(BulletMechanics.Evaluate(BulletMapVersion.V2322, BulletFormula.StrikeInitial, 3, 3));
        Assert.Null(BulletMechanics.Evaluate(BulletFormula.StrikeInitial, Abilities(context), context));
    }

    [Fact]
    public void LegacyMemoryReaderNeverDiscoversFor2322Archive()
    {
        var reader = new BulletGuideRuntimeReader((_, _) => throw new Exception("read forbidden"),
            () => throw new Exception("discovery forbidden"));
        Assert.False(reader.Read("2.0.4.23745", BulletMechanics.Script2322, 1, true).IsCurrent);
        Assert.False(reader.Read("3.0", BulletMechanics.Script2322, 1, true).IsCurrent);
    }
}
