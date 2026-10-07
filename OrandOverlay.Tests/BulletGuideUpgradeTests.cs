using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideUpgradeTests
{
    private readonly DataCatalog _catalog = new();
    public BulletGuideUpgradeTests() => _catalog.Load(loadCarryPolicy: false);

    [Theory]
    [InlineData(1, 1, "armor")]
    [InlineData(2, 1, "armor")]
    [InlineData(3, 1, "speed")]
    [InlineData(3, 2, "speed")]
    public void VerifiedTiersChooseArmorThenSpeed(int armor, int speed, string track)
    {
        var decision = BulletGuideUpgradePolicy.Decide(Frame(21) with
            { GuideRuntime = new(true, armor, speed, 1, "fixture") }, _catalog);
        Assert.Equal(CoachActionKind.Upgrade, decision!.Kind);
        Assert.Equal("guide1:upgrade:" + track, decision.Id);
        Assert.Equal("luffy_common", decision.ConsumedUnitId);
    }

    [Fact]
    public void CommittedRecipeCommonIsNotUpgradePayment()
    {
        var frame = Frame(1) with
        {
            GuideRuntime = new(true, 1, 1, 1, "fixture"),
            CommittedCraftUnitId = "luffy_common"
        };
        Assert.Null(BulletGuideUpgradePolicy.Decide(frame, _catalog));
    }

    [Fact]
    public void MissingTiersOrNoCommonNeverSpendCommon()
    {
        Assert.Null(BulletGuideUpgradePolicy.Decide(Frame(21), _catalog));
        Assert.Null(BulletGuideUpgradePolicy.Decide(Frame(0) with
            { GuideRuntime = new(true, 1, 1, 1, "fixture") }, _catalog));
    }

    [Fact]
    public void NoOwnedBulletCannotUseAnotherPlayersOrPreviousTiers()
    {
        var frame = Frame(21) with { Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy_common", 21),
            GuideRuntime = new(true, 1, 1, 1, "fixture") };
        Assert.Null(BulletGuideUpgradePolicy.Decide(frame, _catalog));
    }

    [Fact]
    public void TheRoundFiftyCommonReserveCanBeSpentForItsIntendedUpgrades()
    {
        Assert.Equal(CoachActionKind.Upgrade, BulletGuideUpgradePolicy.Decide(Frame(20) with
            { GuideRuntime = new(true, 1, 1, 1, "fixture") }, _catalog)!.Kind);
    }

    [Fact]
    public void UserManagedAttackInvestmentIsNotAutomaticallySelected()
    {
        Assert.Null(BulletGuideUpgradePolicy.Decide(Frame(21) with
            { GuideRuntime = new(true, 3, 3, 1, "fixture") }, _catalog));
    }

    [Fact]
    public void SupportSummaryUsesUnknownAndSelectedTraitContractWithoutChangingUpgradePolicy()
    {
        var context = new BulletUpgradeObservationContext(BulletMapVersion.V2320, BulletMechanics.Script2320,
            0, 123, "test", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
        var runtime = new BulletGuideRuntimeState(true, 3, 1, 1, "fixture") { MapVersion = BulletMapVersion.V2320 };
        var frame = Frame(21) with { GuideRuntime = runtime };
        Assert.Equal(BulletTraitState.Unknown, frame.GuideRuntime.Trait(BulletUpgradeAbility.Armor));
        Assert.Null(frame.GuideRuntime.BulletArmorReduction);
        var observation = new BulletUpgradeAbilityObservation(context.MapVersion, context.MapScriptSha256,
            BulletUpgradeObservationSource.VerifiedCompleteAbilityList, 0, 123, "h081", "test", context.NowUtc,
            true, 1, 1, 4);
        frame = frame with { GuideRuntime = runtime.WithUpgradeAbilities(observation, context) };
        Assert.Equal(BulletTraitState.ConfirmedSelected, frame.GuideRuntime.Trait(BulletUpgradeAbility.Armor));
        Assert.Equal(50, frame.GuideRuntime.BulletArmorReduction);
        Assert.DoesNotContain("UNIT_TYPE_UNDEAD", BulletGuideAdvice.SupportSummary(frame));
        Assert.Equal("guide1:upgrade:speed", BulletGuideUpgradePolicy.Decide(frame, _catalog)!.Id);
    }

    private CoachFrame Frame(int common) => new()
    {
        Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
        Round = 50, CompletedStoryStage = 13, IsCurrent = true, Difficulty = "악몽",
        Inventory = ImmutableDictionary<string, int>.Empty.Add(BulletGuidePolicy.GoalId, 1).Add("luffy_common", common),
        GuidePlan = new(BulletGuideStage.Operating, null, true)
        { Support = new(100, 82, 100, true, null) }
    };
}
