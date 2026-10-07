using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Collections.Immutable;
using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletAbilityWiringTests
{
    [Fact]
    public void CompiledReaderRecognitionAcceptanceFramePlannerBothViewsAndFingerprintAreConnected()
    {
        // Read compiled IL only. This does not instantiate WPF or run a native process.
        Assert.Contains("WarcraftBulletAbilityReader.Read", Calls(typeof(WarcraftCombatReader), "Read"));
        Assert.Contains("WarcraftMemoryRecognitionService.RecognizeCore",
            Calls(typeof(WarcraftMemoryRecognitionService), "Recognize"));
        var recognition = Calls(typeof(WarcraftMemoryRecognitionService), "RecognizeCore");
        Assert.Contains("WarcraftCombatReader.Read", recognition);
        Assert.Contains("RecognitionResult.set_CombatObservations", recognition);
        var scan = Calls(typeof(MainWindow), "ScanCoreAsync");
        Assert.Contains("MainWindow.IsCurrentRecognition", scan);
        Assert.Contains("MainWindow.CaptureCoachObservation", scan);
        Assert.Contains("MainWindow.AcceptCombatObservations", Calls(typeof(MainWindow), "CaptureCoachObservation"));
        var render = Calls(typeof(MainWindow), "RenderBeginnerCoach");
        Assert.Contains("CoachFrame.set_CombatObservations", render);
        Assert.Contains("BeginnerCoachSession.Update", render);
        Assert.Contains("BeginnerCoachView.Render", render);
        Assert.Contains("OverlayWindow.RenderCoach", render);
        Assert.Contains("BeginnerCoachPlanner.Decide", Calls(typeof(BeginnerCoachSession), "Update"));
        Assert.Contains("MainWindow.AppendCombatObservationFingerprint", Calls(typeof(MainWindow), "BuildScanSignature"));
        Assert.Contains("CoachDecision.get_UnknownSignals", Calls(typeof(BeginnerCoachView), "Render"));
    }

    [Theory]
    [InlineData("A134", "A13C", "AppliedMarkers")]
    [InlineData("A912", "A13C", "Partial")]
    [InlineData("A912", "A07N", "Absent")]
    public void ActualNativeRecipientChangesConsumerEvidenceNotUseReceipt(string a, string b, string state)
    {
        var unit = new BulletAbilityObservationTests.Memory("h02Z", a, b).Read()!;
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:180h", 1)
            .Add("rawcode:Z20h", 1).Add("item_greenblood", 1);
        var frame = BulletGuideUncommonSaleTests.Frame() with {
            Inventory = inventory, GreenBloodAvailable = true,
            CombatObservations = MainWindow.AcceptCombatObservations(new RecognitionResult {
                State = RecognitionState.Ready, Status = "fixture", CombatObservations = [unit] }),
            GreenBlood = new GreenBloodAdvisor(catalog).EvaluateBulletGuide(
                inventory.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }),
                13, GoroseiMode.Nasjuro, false, "악몽") };
        var decision = new BeginnerCoachSession(catalog).Update(frame);
        Assert.Equal(CoachActionKind.Item, decision.Kind);
        Assert.Equal(state, unit.BulletAbilities!.GreenBloodMarkers.ToString());
        Assert.Equal("greenblood:rawcode:Z20h", decision.Id);
        Assert.Equal("rawcode:Z20h", decision.TargetUnitId);
        Assert.Equal(new BeginnerCoachPlanner(catalog).Decide(frame).Reason, decision.Reason);
        Assert.Same(unit, Assert.Single(frame.CombatObservations));
        Assert.Null(unit.BulletAbilities!.UseReceipt); Assert.Null(unit.BulletAbilities.ActiveEffect);
        Assert.Same(inventory, frame.Inventory);
    }

    [Theory]
    [InlineData("map")] [InlineData("version")] [InlineData("foreign")] [InlineData("localUnknown")]
    public void UnpinnedOrForeignReadCannotBecomeNativeObservation(string invalid)
    {
        var f = new BulletAbilityObservationTests.Memory("H0C4", "A09C");
        var reader = new WarcraftBulletAbilityReader(f.Bytes, BulletAbilityObservationTests.Memory.Module, 0x4000000);
        Assert.Null(reader.Read(invalid == "version" ? "unknown" : "2.0.4.23745",
            invalid == "map" ? "unknown" : RouteQuestCatalog.MapScriptSha256,
            BulletAbilityObservationTests.Memory.Unit, BulletAbilityObservationTests.Memory.Code("H0C4"),
            invalid == "foreign" ? (byte)1 : (byte)0, invalid == "localUnknown" ? (byte)4 : (byte)0));
    }

    [Fact]
    public void IndependentCarrierDoesNotRequireHelperManaAndDoesNotAcceptOtherRawcode()
    {
        var f = new BulletAbilityObservationTests.Memory("H0C4", "A09C");
        var reader = new WarcraftBulletAbilityReader(f.Bytes, BulletAbilityObservationTests.Memory.Module, 0x4000000);
        var value = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
            BulletAbilityObservationTests.Memory.Unit, BulletAbilityObservationTests.Memory.Code("H0C4"), 0, 0);
        Assert.Equal(1, value!.A09CRemaining);
        f.U32(BulletAbilityObservationTests.Memory.Unit + 0x178, BulletAbilityObservationTests.Memory.Code("h08A"));
        value = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
            BulletAbilityObservationTests.Memory.Unit, BulletAbilityObservationTests.Memory.Code("h08A"), 0, 0);
        Assert.Null(value!.A09CRemaining);
    }

    [Fact]
    public void ReboundAbilityEntryWithSameObjectAndValuesIsNotStable()
    {
        var f = new BulletAbilityObservationTests.Memory("H0C4", "A09C");
        f.U32(0x150024, 9); f.U64(0x150030, 0); f.U64(0x150090, BulletAbilityObservationTests.Memory.Ability(0));
        var reads = 0;
        f.Change = (address, _, bytes) => address == 0x130018 && ++reads > 2
            ? BitConverter.GetBytes(0x150000UL) : bytes;
        var reader = new WarcraftBulletAbilityReader(f.Bytes, BulletAbilityObservationTests.Memory.Module, 0x4000000);
        Assert.Null(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
            BulletAbilityObservationTests.Memory.Unit, BulletAbilityObservationTests.Memory.Code("H0C4"), 0, 0));
    }

    private static List<string> Calls(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var method = type.GetMethod(name, flags)!;
        var machine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        if (machine is not null) method = machine.GetMethod("MoveNext", flags)!;
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        var ops = typeof(OpCodes).GetFields().Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
        var calls = new List<string>();
        for (var i = 0; i < bytes.Length;)
        {
            ushort code = bytes[i++]; if (code == 0xfe) code = (ushort)(0xfe00 | bytes[i++]);
            var operand = ops[code].OperandType;
            if (operand == OperandType.InlineMethod)
            {
                var target = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, i))!;
                calls.Add(target.DeclaringType!.Name + "." + target.Name);
            }
            i += operand switch {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2, OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, i), _ => 4 };
        }
        return calls;
    }
}
