using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletAbilityObservationTests
{
    [Fact]
    public void StableRecipientMarkersReachPlannerWithoutReceiptOrActiveCredit()
    {
        var f = new Memory("h02Z", "A134", "A13C", "A912");
        var unit = Assert.IsType<CombatUnitState>(f.Read());
        var property = typeof(CombatUnitState).GetProperty("BulletAbilities");
        Assert.NotNull(property);
        var value = property.GetValue(unit);
        Assert.NotNull(value);
        Assert.Equal("AppliedMarkers", value.GetType().GetProperty("GreenBloodMarkers")!.GetValue(value)!.ToString());
        Assert.Null(value.GetType().GetProperty("UseReceipt")!.GetValue(value));
        Assert.Null(value.GetType().GetProperty("ActiveEffect")!.GetValue(value));
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(
            BulletGuideUncommonSaleTests.OperatingSupportCodes(), unit);
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Contains(BulletAbilityPresentation.Describe(frame), decision.OperationGuide);
        Assert.Equal(BulletGuideAdvice.Operation(frame), decision.OperationGuide);
    }

    [Theory]
    [InlineData("h0A3", "A13C", GreenBloodMarkerState.IntrinsicSeraphim)]
    [InlineData("h0A1", "A13C", GreenBloodMarkerState.IntrinsicSeraphim)]
    [InlineData("h0A0", "A13C", GreenBloodMarkerState.IntrinsicSeraphim)]
    [InlineData("h09Y", "A13C", GreenBloodMarkerState.IntrinsicSeraphim)]
    [InlineData("h02Z", "A13C", GreenBloodMarkerState.Partial)]
    [InlineData("h02Z", "A134", GreenBloodMarkerState.Partial)]
    [InlineData("h02Z", "A912", GreenBloodMarkerState.Absent)]
    public void SingleMarkerNeverBecomesReceipt(string raw, string ability, GreenBloodMarkerState expected)
    {
        var value = Assert.IsType<BulletAbilityObservation>(new Memory(raw, ability).Read()!.BulletAbilities);
        Assert.Equal(expected, value.GreenBloodMarkers);
        Assert.Null(value.UseReceipt); Assert.Null(value.ActiveEffect); Assert.Null(value.ButtonEnabled);
    }

    [Fact]
    public void InventoryOnlyGreenBloodIsAConfirmationNotExactConsumption()
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var frame = BulletGuideUncommonSaleTests.Frame() with {
            Inventory = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty
                .Add("rawcode:180h", 1).Add("rawcode:Z20h", 1), CombatObservations = [] };
        frame = frame with { GreenBloodAvailable = true,
            Inventory = frame.Inventory.Add("item_greenblood", 1),
            GreenBlood = new GreenBloodAdvisor(catalog).EvaluateBulletGuide(
                frame.Inventory.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value })
                    .Append(new InventoryEntry { UnitId = "item_greenblood", Count = 1 }),
                13, GoroseiMode.Nasjuro, false, "악몽") };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Item, decision.Kind);
        Assert.Equal("greenblood:rawcode:Z20h", decision.Id);
        Assert.Equal("rawcode:Z20h", decision.TargetUnitId);
        Assert.Empty(frame.CombatObservations);
        Assert.Equal(1, frame.Inventory["item_greenblood"]);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void CarrierRemainingLevelReachesActualPlanner(int remaining)
    {
        var f = new Memory("H0C4", "A09C", "A13A");
        f.U32(Memory.Ability(0) + 0x9c, (uint)remaining - 1);
        var unit = Assert.IsType<CombatUnitState>(f.Read());
        var observation = Assert.IsType<BulletAbilityObservation>(unit.BulletAbilities);
        var property = typeof(BulletAbilityObservation).GetProperty("A09CRemaining");
        Assert.NotNull(property);
        Assert.Equal(remaining, property.GetValue(observation));
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(
            BulletGuideUncommonSaleTests.OperatingSupportCodes(), unit);
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Null(observation.ButtonEnabled);
        Assert.Null(observation.UseReceipt);
        Assert.Equal(unit.EngineHandle, observation.EngineHandle);
        Assert.Equal(unit.Owner, observation.Owner);
        Assert.Contains(BulletAbilityPresentation.Describe(frame), decision.OperationGuide);
        Assert.Equal(BulletGuideAdvice.Operation(frame), decision.OperationGuide);
    }

    [Theory]
    [InlineData(0, "h00Y", "A0BA", true)]
    [InlineData(7, "h00Y", "A0BA", true)]
    [InlineData(0, "h01C", "A912", true)]
    [InlineData(7, "h015", "A912", true)]
    [InlineData(0, "h00Y", "A912", false)]
    [InlineData(7, "h00Y", "A912", false)]
    public void StableTargetUsesMapOwnerMarkerAndExceptions(int owner, string raw, string ability, bool eligible)
    {
        var f = new Memory(raw, ability) { Owner = (byte)owner };
        f.Put(Memory.Unit + 0x1c0, [(byte)owner]);
        var state = Assert.IsType<CombatUnitState>(f.Read());
        Assert.Equal(eligible, state.BulletAbilities?.A09CTargetEligible);
    }

    [Theory]
    [InlineData("self")] [InlineData("owner")] [InlineData("rawcode")]
    [InlineData("incomplete")] [InlineData("cycle")] [InlineData("duplicate")]
    [InlineData("changedLevel")] [InlineData("changedAbilityIdentity")]
    public void InvalidIdentityOrIncompleteListNeverYieldsAbilityEvidence(string invalid)
    {
        var f = new Memory("H0C4", "A09C", "A13C");
        switch (invalid)
        {
            case "self": f.U64(0x140090, 0x200000); break;
            case "owner": f.Put(Memory.Unit + 0x1c0, [1]); break;
            case "rawcode": f.U32(Memory.Unit + 0x178, Memory.Code("h08A")); break;
            case "incomplete": f.U64(Memory.Ability(1) + 0x58, (9UL << 32) | 200); break;
            case "cycle": f.U64(Memory.Ability(1) + 0x58, (9UL << 32) | 1); break;
            case "duplicate": f.U32(Memory.Ability(1) + 0x70, Memory.Code("A09C")); break;
            case "changedLevel":
                var reads = 0;
                f.Change = (a, _, b) => a == Memory.Ability(0) + 0x9c && ++reads > 1 ? BitConverter.GetBytes(3U) : b;
                break;
            case "changedAbilityIdentity":
                var visits = 0;
                f.Change = (a, _, b) => a == 0x141090 && ++visits > 1 ? BitConverter.GetBytes(0x200000UL) : b;
                break;
        }
        Assert.Null(f.Read()?.BulletAbilities);
    }

    [Fact]
    public void RemainingTracksBonusConsumptionAndRemovalNotNavigationSelection()
    {
        var f = new Memory("H0C4", "A09C", "A13A");
        foreach (var count in new[] { 3, 4, 3, 2, 1 })
        {
            f.U32(Memory.Ability(0) + 0x9c, (uint)count - 1);
            Assert.Equal(count, f.Read()!.BulletAbilities!.A09CRemaining);
        }
        f.U64(Memory.Unit + 0x558, (9UL << 32) | 2);
        Assert.Equal(0, f.Read()!.BulletAbilities!.A09CRemaining);
        Assert.True(f.Read()!.BulletAbilities!.A13APresent);
        f.U64(Memory.Unit + 0x558, ulong.MaxValue);
        Assert.False(f.Read()!.BulletAbilities!.A13APresent);
    }

    [Theory]
    [InlineData(4U)] [InlineData(5U)] [InlineData(uint.MaxValue)]
    public void OutOfContractLevelIsUnknownNeverClamped(uint stored)
    {
        var f = new Memory("H0C4", "A09C"); f.U32(Memory.Ability(0) + 0x9c, stored);
        var observation = Assert.IsType<BulletAbilityObservation>(f.Read()!.BulletAbilities);
        Assert.Null(observation.A09CRemaining); Assert.Null(observation.A09CCooldown);
    }

    [Fact]
    public void CooldownGetterFailurePreservesCountButNeverInventsFiveSeconds()
    {
        var f = new Memory("H0C4", "A09C");
        Assert.Equal(0f, f.Read()!.BulletAbilities!.A09CCooldown);
        f.U64(0x402648, 0);
        var observation = f.Read()!.BulletAbilities!;
        Assert.Equal(1, observation.A09CRemaining); Assert.Null(observation.A09CCooldown);
    }

    [Fact]
    public void EligibleTargetIsDisplayedAsObservedNotEnabledOrCreditedInventory()
    {
        var unit = new Memory("h00Y", "A0BA").Read()!;
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(
            BulletGuideUncommonSaleTests.OperatingSupportCodes(), unit);
        var before = frame.Inventory;
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Equal("h00Y", unit.Rawcode);
        Assert.True(unit.BulletAbilities!.A09CTargetEligible);
        Assert.Null(unit.BulletAbilities.ButtonEnabled);
        Assert.Null(unit.BulletAbilities.UseReceipt);
        Assert.Contains(BulletAbilityPresentation.Describe(frame), decision.OperationGuide);
        Assert.Same(before, frame.Inventory);
        Assert.NotEqual(CoachActionKind.Item, decision.Kind);
    }

    [Theory]
    [InlineData(RecognitionState.Ready, false, true)]
    [InlineData(RecognitionState.Ready, true, false)]
    [InlineData(RecognitionState.Waiting, false, false)]
    [InlineData(RecognitionState.TransientReadError, false, false)]
    public void RecognitionAcceptanceAndFingerprintCarryActualReaderOutput(RecognitionState state, bool boundary, bool accepted)
    {
        var memory = new Memory("H0C4", "A09C");
        var first = memory.Read()!;
        var result = new RecognitionResult { State = state, ConfirmsSessionBoundary = boundary,
            CombatObservations = [first], Status = "fixture" };
        var observations = MainWindow.AcceptCombatObservations(result);
        Assert.Equal(accepted ? 1 : 0, observations.Length);
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(BulletGuideUncommonSaleTests.OperatingSupportCodes())
            with { CombatObservations = observations };
        var text = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame).OperationGuide;
        if (accepted)
        {
            Assert.Same(first, Assert.Single(observations));
            Assert.Equal(1, observations[0].BulletAbilities!.A09CRemaining);
        }
        else Assert.Empty(observations);
        Assert.Equal(BulletGuideAdvice.Operation(frame), text);
        var a = new System.Text.StringBuilder(); MainWindow.AppendCombatObservationFingerprint(a, observations);
        memory.U32(Memory.Ability(0) + 0x9c, 3);
        var b = new System.Text.StringBuilder(); MainWindow.AppendCombatObservationFingerprint(b, [memory.Read()!]);
        Assert.NotEqual(a.ToString(), b.ToString());
        Assert.Contains("A09CRemaining = 4", b.ToString());
    }

    [Theory]
    [InlineData("stale")] [InlineData("paused")] [InlineData("clear")]
    [InlineData("fail")] [InlineData("difficulty")]
    public void LostFreshnessNeverDisplaysCurrentAbilities(string boundary)
    {
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(
            BulletGuideUncommonSaleTests.OperatingSupportCodes(), new Memory("H0C4", "A09C").Read());
        frame = boundary switch { "stale" => frame with { IsCurrent = false },
            "paused" => frame with { Paused = true }, "difficulty" => frame with { Difficulty = "unknown" },
            _ => frame with { Outcome = boundary } };
        Assert.Empty(BulletAbilityPresentation.Describe(frame));
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.DoesNotContain(BulletAbilityPresentation.Describe(frame with {
            IsCurrent = true, Paused = false, Outcome = "", Difficulty = "악몽" }), decision.OperationGuide);
    }

    internal sealed class Memory
    {
        internal const ulong Unit = 0x100000, Module = 0x400000, Vtable = 0x401000;
        private readonly Dictionary<ulong, byte> bytes = [];
        internal Func<ulong, int, byte[], byte[]>? Change;
        internal string Rawcode;
        internal byte Owner;
        internal Memory(string code, params string[] abilities)
        {
            Rawcode = code;
            U64(Unit, Vtable);
            U64(Vtable + 0x178, Module + 0x1163ad0);
            U64(Vtable + 0x278, Module + 0x1163a00);
            U64(Vtable + 0x280, Module + 0x1163a20);
            U32(Unit + 0x178, Code(code)); Put(Unit + 0x1c0, [0]);
            U64(Unit + 0x258, ulong.MaxValue); U64(Unit + 0x3b8, ulong.MaxValue);
            U64(Unit + 0x500, ulong.MaxValue);
            U64(Module + 0x2b808c0, 0x120000);
            U32(0x120030, (uint)abilities.Length + 1); U64(0x120018, 0x130000);
            Entry(0, Unit); U64(Unit + 0x18, 9UL << 32);
            U64(Unit + 0x558, abilities.Length == 0 ? ulong.MaxValue : (9UL << 32) | 1);
            for (var i = 0; i < abilities.Length; i++)
            {
                var ability = Ability(i); Entry(i + 1, ability);
                U64(ability, 0x402000); U64(0x402648, Module + 0x6515d0);
                U32(ability + 0x70, Code(abilities[i])); U32(ability + 0x9c, 0);
                U64(ability + 0x58, i == abilities.Length - 1 ? ulong.MaxValue : (9UL << 32) | (uint)(i + 2));
            }
        }
        internal static uint Code(string value) => value.Aggregate(0U, (n, c) => (n << 8) | c);
        internal static ulong Ability(int index) => 0x180000UL + (ulong)index * 0x1000;
        private void Entry(int index, ulong obj)
        {
            var entry = 0x140000UL + (ulong)index * 0x1000;
            U32(0x130000UL + (ulong)index * 16, 0xfffffffe);
            U64(0x130008UL + (ulong)index * 16, entry);
            U32(entry + 0x18, 0x2b61676c); U32(entry + 0x24, 9);
            U64(entry + 0x30, 0); U64(entry + 0x90, obj);
        }
        internal void Put(ulong address, byte[] value)
        { for (var i = 0; i < value.Length; i++) bytes[address + (ulong)i] = value[i]; }
        internal void U32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        internal void U64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        internal byte[] Bytes(ulong address, int length)
        {
            var result = Enumerable.Range(0, length).Select(i => bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
            return Change?.Invoke(address, length, result) ?? result;
        }
        internal CombatUnitState? Read() => new WarcraftCombatReader(Bytes, Module, 0x4000000)
            .Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, Unit, Code(Rawcode), Owner, 0, 1);
    }
}
