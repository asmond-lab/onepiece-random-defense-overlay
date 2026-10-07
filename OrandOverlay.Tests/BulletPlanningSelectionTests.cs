using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletPlanningSelectionTests
{
    internal static GoroseiObservation Native(GoroseiMode mode)
    {
        var m = new BulletGoroseiNativeTests.MarkerMemory();
        uint raw = mode == GoroseiMode.Warcury ? 0x6f303245u : 0x6f303332u;
        m.U32(BulletGoroseiNativeTests.MarkerMemory.Unit + 0x178, raw);
        var marker = m.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
            [(BulletGoroseiNativeTests.MarkerMemory.Unit, raw)]);
        Assert.Equal(GoroseiMarkerStatus.SelectedIdentity, marker.Status);
        Assert.Equal(mode, marker.Mode);
        var session = new GoroseiObservationSession(); session.Reset(1);
        session.AcceptRecognition(1, 1, new RecognitionResult { State = RecognitionState.Ready,
            Diagnostics = new() { GoroseiMarker = marker, MapState = new(2, 0, "악몽") } },
            OverlayExecutionContext.Fixture(new()));
        return session.Current;
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(49)] [InlineData(50)]
    public void NativeWarcuryPlans120ThroughActualGuideInputAtEveryRound(int round)
    {
        var current = Native(GoroseiMode.Warcury);
        var selection = MainWindow.ResolveGuidePlanningSelection(current, 1, 1);
        var frame = NasjuroWispAdvicePolicyTests.Frame(GoroseiMode.Warcury) with { Round = round, Gorosei = current };
        frame = frame with { GuidePlan = new BulletGuidePolicy(NasjuroWispAdvicePolicyTests.Catalog()).Plan(
            round, 10, frame.Inventory, "악몽", BulletGuidePolicy.NavigationId, selection.Mode) };
        Assert.Equal(120, frame.GuidePlan.Support!.ArmorTarget);
        Assert.Equal(0, frame.GuidePlan.Support.ArmorPotential);
        Assert.Equal(GoroseiMode.None, current.EffectMode);
        var decision = new BeginnerCoachPlanner(NasjuroWispAdvicePolicyTests.Catalog()).Decide(frame);
        Assert.Equal(GoroseiMode.Warcury, frame.PlanningGorosei.Mode);
        Assert.Equal("CurrentSelectedIdentity", frame.PlanningGorosei.Source);
        Assert.Equal(BulletGuideAdvice.Operation(frame), decision.OperationGuide);
    }

    [Theory]
    [InlineData("UserPlan")] [InlineData("SavedUserPlan")]
    public void ManualPlanWithUnknownNativeCanPlanButNeverActivatesCombat(string source)
    {
        var frame = NasjuroWispAdvicePolicyTests.Frame() with { Round = 2,
            Gorosei = GoroseiObservation.Unknown,
            UserGoroseiPlan = new(GoroseiMode.Nasjuro, source) };
        var decision = new BeginnerCoachPlanner(NasjuroWispAdvicePolicyTests.Catalog()).Decide(frame);
        Assert.Contains("8:2", decision.OperationGuide);
        Assert.Equal(source, frame.PlanningGorosei.Source);
        Assert.Contains(frame.PlanningGorosei.Describe(), decision.OperationGuide);
        Assert.Equal(GoroseiMode.None, MainWindow.ResolveObservationGorosei(PlayMode.Guide,
            GoroseiMode.Nasjuro, frame.Gorosei));
    }

    [Fact]
    public void ManualPlanDoesNotMakeUnversionedCombatBodyCurrent()
    {
        var f = NasjuroWispAdvicePolicyTests.Frame() with { MatchGeneration = 0, RecognitionRevision = 0,
            Gorosei = GoroseiObservation.Unknown, UserGoroseiPlan = new(GoroseiMode.Nasjuro, "UserPlan") };
        Assert.Equal(GoroseiMode.Nasjuro, f.PlanningGorosei.Mode);
        Assert.DoesNotContain("8:2", new BeginnerCoachPlanner(NasjuroWispAdvicePolicyTests.Catalog()).Decide(f).OperationGuide);
    }

    [Theory]
    [InlineData("unknown")] [InlineData("conflict")] [InlineData("staleRevision")] [InlineData("staleGeneration")]
    public void UnconfirmedSelectionCannotBorrowNativeLastKnown(string fault)
    {
        var native = Native(GoroseiMode.Nasjuro);
        var f = NasjuroWispAdvicePolicyTests.Frame() with { Round = 2, Gorosei = native };
        f = fault switch {
            "unknown" => f with { Gorosei = GoroseiObservation.Unknown },
            "conflict" => f with { Gorosei = native with { IsCurrent = false, Marker = native.Marker! with {
                Status = GoroseiMarkerStatus.Conflict, Detail = "native 선택 충돌", Mode = GoroseiMode.None } } },
            "staleRevision" => f with { RecognitionRevision = 2 },
            _ => f with { MatchGeneration = 2 } };
        Assert.False(f.PlanningGorosei.IsKnown);
        Assert.DoesNotContain("8:2", new BeginnerCoachPlanner(NasjuroWispAdvicePolicyTests.Catalog()).Decide(f).OperationGuide);
        Assert.Null(BulletGuideRayleighPolicy.Decide(f));
    }

    [Theory]
    [InlineData("stale")] [InlineData("pause")] [InlineData("clear")] [InlineData("fail")]
    [InlineData("difficulty")] [InlineData("inventory")] [InlineData("dead")] [InlineData("shipUnknown")] [InlineData("shipMissing")]
    public void EarlyUserPlanKeepsAllActionAndBodyAndShipGuards(string fault)
    {
        var f = NasjuroWispAdvicePolicyTests.Frame() with { Round = 2, Gorosei = GoroseiObservation.Unknown,
            UserGoroseiPlan = new(GoroseiMode.Nasjuro, "UserPlan") };
        f = fault switch {
            "stale" => f with { IsCurrent = false }, "pause" => f with { Paused = true },
            "clear" or "fail" => f with { Outcome = fault }, "difficulty" => f with { Difficulty = "unknown" },
            "inventory" => f with { Inventory = f.Inventory.Remove("rawcode:3A0h") },
            "dead" => f with { CombatObservations = [f.CombatObservations[0] with { Life = 0 }] },
            "shipUnknown" => f with { SelectedGoalIds = ["rawcode:????"] },
            _ => f with { SelectedGoalIds = ["rawcode:850h"] } };
        var d = new BeginnerCoachPlanner(NasjuroWispAdvicePolicyTests.Catalog()).Decide(f);
        Assert.DoesNotContain("8:2", d.OperationGuide);
        Assert.Equal(GoroseiMode.Nasjuro, f.PlanningGorosei.Mode);
        Assert.Equal(GoroseiMode.None, f.CurrentGoroseiEffect);
    }

    [Fact]
    public void NewMatchResetsNativeButPreservesExplicitBuildPreferenceWithoutPromotingIt()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            "{\"GoroseiMode\":\"Nasjuro\",\"BulletPlanningGoroseiMode\":\"Warcury\"}")!;
        var preference = new GoroseiPlanningSelection(GoroseiEffects.Parse(settings.BulletPlanningGoroseiMode), "SavedUserPlan");
        var session = new GoroseiObservationSession(); session.Reset(1);
        session.Accept(1, 1, Native(GoroseiMode.Nasjuro).Marker!, true);
        Assert.Equal(GoroseiMode.Nasjuro, MainWindow.ResolveGuidePlanningSelection(session.Current, 1, 1, preference).Mode);
        session.Reset(2);
        Assert.Equal(GoroseiMode.None, session.LastKnown);
        var plan = MainWindow.ResolveGuidePlanningSelection(session.Current, 2, 0, preference);
        Assert.Equal(preference, plan);
        Assert.Equal(GoroseiMode.None, session.Current.EffectMode);
        session.Accept(1, 99, Native(GoroseiMode.Nasjuro).Marker!, true);
        Assert.Equal(preference, MainWindow.ResolveGuidePlanningSelection(session.Current, 2, 0, preference));
        session.Accept(2, 1, Native(GoroseiMode.Nasjuro).Marker!, true);
        Assert.Equal(GoroseiMode.Nasjuro, MainWindow.ResolveGuidePlanningSelection(session.Current, 2, 1, preference).Mode);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"GoroseiMode\":\"Nasjuro\"}")!;
        Assert.Equal("None", legacy.BulletPlanningGoroseiMode); // Legacy values were also auto-written; do not invent provenance.
    }

    [Fact]
    public void ManualConflictFallbackIsLabeledAndNeverOverwritesNativeEvidence()
    {
        var marker = Native(GoroseiMode.Nasjuro).Marker! with { Status = GoroseiMarkerStatus.Conflict,
            Mode = GoroseiMode.None, Detail = "native 선택 충돌" };
        var s = new GoroseiObservationSession(); s.Reset(1); s.Accept(1, 1, marker, true);
        var f = NasjuroWispAdvicePolicyTests.Frame() with { Round = 2, Gorosei = s.Current,
            UserGoroseiPlan = new(GoroseiMode.Nasjuro, "UserPlan") };
        var d = new BeginnerCoachPlanner(NasjuroWispAdvicePolicyTests.Catalog()).Decide(f);
        Assert.Contains("8:2", d.OperationGuide);
        Assert.Equal("UserPlan", f.PlanningGorosei.Source);
        Assert.Equal(GoroseiMarkerStatus.Conflict, f.Gorosei.Marker!.Status);
        Assert.Contains(f.PlanningGorosei.Describe(), d.OperationGuide);
        Assert.Same(marker, f.Gorosei.Marker); Assert.Equal(GoroseiMode.None, f.CurrentGoroseiEffect);
        foreach (var mode in new[] { PlayMode.Beginner, PlayMode.Normal, PlayMode.Guide })
            Assert.Equal(GoroseiMode.None, MainWindow.ResolveObservationGorosei(mode, GoroseiMode.Nasjuro, f.Gorosei,
                GoroseiMode.Nasjuro, liveAutomatic: true));
    }

    [Fact]
    public void Round49To50KeepsPlanAndObservedSupportButChangesCurrentEffectDisplay()
    {
        var c = OverlayExecutionContext.SyntheticGoroseiFixture(new());
        var i = c.CreateSyntheticGorosei(GoroseiMode.Warcury);
        var s = new GoroseiObservationSession(); s.Reset(1);
        var catalog = NasjuroWispAdvicePolicyTests.Catalog();
        foreach (var round in new[] { 49, 50 })
        {
            s.AcceptRecognition(1, round, SyntheticGoroseiEffectTests.Result(i, round), c);
            var f = NasjuroWispAdvicePolicyTests.Frame() with { Round = round, RecognitionRevision = round, Gorosei = s.Current };
            var selection = MainWindow.ResolveGuidePlanningSelection(s.Current, 1, round);
            f = f with { GuidePlan = new BulletGuidePolicy(catalog).Plan(round, 10, f.Inventory, "악몽", null, selection.Mode) };
            Assert.Equal(120, f.GuidePlan.Support!.ArmorTarget);
            Assert.Equal(0, f.GuidePlan.Support.ArmorPotential);
            Assert.Equal(round == 49 ? GoroseiMode.None : GoroseiMode.Warcury, f.CurrentGoroseiEffect);
            var d = new BeginnerCoachPlanner(catalog).Decide(f);
            Assert.Equal(GoroseiMode.Warcury, f.PlanningGorosei.Mode);
            Assert.Equal(BulletGuideAdvice.Operation(f), d.OperationGuide);
            Assert.Contains(f.PlanningGorosei.Describe(), d.OperationGuide);
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void EarlyNativeSelectionPlansWoodWithoutCurrentEffect(int round)
    {
        var frame = NasjuroWispAdvicePolicyTests.Frame() with {
            Round = round, Gorosei = Native(GoroseiMode.Nasjuro), GoalId = BulletGuidePolicy.GoalId };
        frame = frame with { ShipReservations = ShipReservationPolicy.Evaluate(frame, NasjuroWispAdvicePolicyTests.Catalog()) };
        var decision = new BeginnerCoachPlanner(NasjuroWispAdvicePolicyTests.Catalog()).Decide(frame);
        Assert.Contains("8:2", decision.OperationGuide);
        Assert.Equal(NasjuroApplicability.Applicable, NasjuroWispAdvicePolicy.Evaluate(frame).Applicability);
        Assert.Equal(GoroseiMode.None, frame.Gorosei.EffectMode);
        Assert.False(frame.Gorosei.EffectsActiveVerified);
        Assert.Equal(BulletGuideAdvice.Operation(frame), decision.OperationGuide);
        Assert.Null(decision.TargetUnitId);
        Assert.Null(decision.RewardWispId);
    }
}
