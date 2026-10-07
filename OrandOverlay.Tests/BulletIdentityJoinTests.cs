using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletIdentityJoinTests
{
    private static BulletAbilityObservation? Inner(BulletAbilityObservationTests.Memory f) =>
        new WarcraftBulletAbilityReader(f.Bytes, BulletAbilityObservationTests.Memory.Module, 0x4000000)
            .Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
                BulletAbilityObservationTests.Memory.Unit, BulletAbilityObservationTests.Memory.Code(f.Rawcode), f.Owner, 0);

    [Fact]
    public void MissingParentHandleCannotAuthorizeAValidInnerIdentity()
    {
        var f = new BulletAbilityObservationTests.Memory("H0C4", "A09C");
        f.U64(0x140090, 0x200000);
        var reads = 0;
        f.Change = (address, _, bytes) =>
        {
            if (address == BulletAbilityObservationTests.Memory.Unit + 0x3b8 && ++reads == 4)
                f.U64(0x140090, BulletAbilityObservationTests.Memory.Unit);
            return bytes;
        };
        Assert.Null(f.Read());
        Assert.NotNull(Inner(f));
    }

    [Theory]
    [InlineData(false, true)] [InlineData(true, false)]
    public void ManualOnlyOldCombatPreconditionRequiresExplicitFreshnessLoss(bool lostCurrent, bool displaysOldEvidence)
    {
        // Pure consumer characterization, NOT execution of the WPF Stop event.
        // The reviewed stop branch with automatic inventory empty leaves stale=false.
        var automatic = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty;
        var manual = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty.Add("rawcode:4C0H", 1);
        var oldCombat = new BulletAbilityObservationTests.Memory("H0C4", "A09C").Read()!;
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(BulletGuideUncommonSaleTests.OperatingSupportCodes(), oldCombat)
            with { Inventory = manual, IsCurrent = !lostCurrent && automatic.Count == 0 };
        Assert.Empty(automatic); Assert.NotEmpty(frame.Inventory);
        var text = BulletAbilityPresentation.Describe(frame);
        Assert.Equal(1, oldCombat.BulletAbilities!.A09CRemaining);
        Assert.Equal(displaysOldEvidence ? BulletAbilityPresentation.Describe(frame with { IsCurrent = true }) : "", text);
    }

    [Theory]
    [InlineData("present")] [InlineData("absent")] [InlineData("null")]
    public void StableCompositePreservesSameIdentityCompleteAbsenceAndUnknown(string kind)
    {
        var f = new BulletAbilityObservationTests.Memory("H0C4", kind == "absent" ? [] : ["A09C"]);
        if (kind == "null") f.U64(0x140090, 0x200000);
        var value = Assert.IsType<CombatUnitState>(f.Read());
        if (kind == "null") { Assert.Null(value.EngineHandle); Assert.Null(value.BulletAbilities); return; }
        Assert.Equal(value.EngineHandle, value.BulletAbilities!.EngineHandle);
        Assert.Equal(value.Rawcode, value.BulletAbilities.Rawcode);
        Assert.Equal(value.Owner, value.BulletAbilities.Owner);
        Assert.Equal(kind == "absent" ? 0 : 1, value.BulletAbilities.A09CRemaining);
    }

    [Theory]
    [InlineData("level")] [InlineData("object")]
    public void StandaloneInnerRejectsMutationWithoutOuterReader(string field)
    {
        var f = new BulletAbilityObservationTests.Memory("H0C4", "A09C");
        var reads = 0;
        var target = field == "level" ? BulletAbilityObservationTests.Memory.Ability(0) + 0x9c : 0x141090UL;
        f.Change = (address, _, bytes) => address == target && ++reads > 1
            ? field == "level" ? BitConverter.GetBytes(3U) : BitConverter.GetBytes(0x200000UL) : bytes;
        Assert.Null(Inner(f));
        Assert.True(reads > 1);
    }

    private static BulletAbilityObservationTests.Memory PositiveTimer()
    {
        var f = new BulletAbilityObservationTests.Memory("H0C4", "A09C");
        var ability = BulletAbilityObservationTests.Memory.Ability(0);
        f.U32(ability + 0x38, 0x200);
        f.U64(ability + 0x170, 0x403000);
        f.U64(0x403028, BulletAbilityObservationTests.Memory.Module + 0x3eabe0);
        f.U64(ability + 0x180, 0x190000);
        f.Put(0x190008, BitConverter.GetBytes(12.5f));
        f.U64(0x190010, 0x191000);
        f.Put(0x191070, BitConverter.GetBytes(10f));
        return f;
    }

    [Fact]
    public void PositiveTimerReachesRealPlannerWithoutInventingBaseFiveSeconds()
    {
        var f = PositiveTimer();
        Assert.Equal(2.5f, Inner(f)!.A09CCooldown);
        var unit = Assert.IsType<CombatUnitState>(f.Read());
        Assert.Equal(2.5f, unit.BulletAbilities!.A09CCooldown);
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(BulletGuideUncommonSaleTests.OperatingSupportCodes(), unit);
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Equal(1, unit.BulletAbilities.A09CRemaining);
        Assert.Null(unit.BulletAbilities.ButtonEnabled);
        Assert.Null(unit.BulletAbilities.UseReceipt);
        Assert.Contains(BulletAbilityPresentation.Describe(frame), decision.OperationGuide);
        Assert.Equal(BulletGuideAdvice.Operation(frame), decision.OperationGuide);
    }

    [Theory]
    [InlineData("getter")] [InlineData("flags")] [InlineData("record")]
    public void FirstValidThenChangedOrFailedCooldownKeepsConservativeWholeObservationUnknown(string boundary)
    {
        var f = PositiveTimer();
        var reads = 0;
        var target = boundary switch { "getter" => 0x402648UL, "flags" => BulletAbilityObservationTests.Memory.Ability(0) + 0x38,
            _ => BulletAbilityObservationTests.Memory.Ability(0) + 0x180 };
        var firstCount = boundary == "getter" ? 1 : 2;
        f.Change = (address, _, bytes) => address == target && ++reads > firstCount
            ? boundary == "flags" ? BitConverter.GetBytes(0U) : BitConverter.GetBytes(0UL) : bytes;
        var unit = Assert.IsType<CombatUnitState>(f.Read());
        Assert.True(reads > firstCount);
        Assert.Null(unit.BulletAbilities);
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(BulletGuideUncommonSaleTests.OperatingSupportCodes(), unit);
        var text = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame).OperationGuide;
        Assert.Equal(BulletGuideAdvice.Operation(frame), text);
        Assert.NotEqual(BulletAbilityPresentation.Describe(frame with { CombatObservations = [PositiveTimer().Read()!] }),
            BulletAbilityPresentation.Describe(frame));
    }

    [Theory]
    [InlineData("self")] [InlineData("rawcode")] [InlineData("owner")] [InlineData("armor")]
    public void CompositeRevalidatesOuterAfterInnerCompletes(string field)
    {
        var f = new BulletAbilityObservationTests.Memory("H0C4", "A09C");
        var heads = 0;
        f.Change = (address, _, bytes) =>
        {
            // Outer snapshots consume four head reads, inner snapshots four more.
            if (address == BulletAbilityObservationTests.Memory.Unit + 0x558 && ++heads == 8)
            {
                switch (field)
                {
                    case "self": f.U64(BulletAbilityObservationTests.Memory.Unit + 0x18, 10UL << 32); f.U32(0x140024, 10); break;
                    case "rawcode": f.U32(BulletAbilityObservationTests.Memory.Unit + 0x178, BulletAbilityObservationTests.Memory.Code("h08A")); break;
                    case "owner": f.Put(BulletAbilityObservationTests.Memory.Unit + 0x1c0, [1]); break;
                    case "armor": f.Put(BulletAbilityObservationTests.Memory.Unit + 0x2e8, BitConverter.GetBytes(12f)); break;
                }
            }
            return bytes;
        };
        var result = f.Read();
        Assert.True(heads >= 8);
        Assert.Null(result);
    }

    [Fact]
    public void CompositeRejectsReplacementAfterTwoOuterSnapshotsBeforeInnerRead()
    {
        var f = new BulletAbilityObservationTests.Memory("H0C4", "A09C");
        var finalPositionReads = 0;
        var replaced = false;
        f.Change = (address, _, bytes) =>
        {
            // Each outer snapshot reads position at entry and at its final fence.
            // Mutate AFTER returning the fourth value: both completed outers are H1.
            if (address == BulletAbilityObservationTests.Memory.Unit + 0x3b8 && ++finalPositionReads == 4)
            {
                f.U64(BulletAbilityObservationTests.Memory.Unit + 0x18, 10UL << 32);
                f.U32(0x140024, 10); // same table slot/agent -> same address, new generation H2
                f.U32(BulletAbilityObservationTests.Memory.Ability(0) + 0x9c, 3);
                replaced = true;
            }
            return bytes;
        };
        var composite = f.Read();
        Assert.True(replaced);
        var inner = new WarcraftBulletAbilityReader(f.Bytes, BulletAbilityObservationTests.Memory.Module, 0x4000000)
            .Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
                BulletAbilityObservationTests.Memory.Unit, BulletAbilityObservationTests.Memory.Code("H0C4"), 0, 0);
        Assert.NotNull(inner);
        Assert.Equal(10UL << 32, inner.EngineHandle);
        Assert.Equal(4, inner.A09CRemaining);
        Assert.Null(composite);
    }
}
