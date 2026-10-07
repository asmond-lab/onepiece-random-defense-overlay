using System.Collections.Immutable;
using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletObservationFixTests
{
    public static IEnumerable<object[]> GoroseiCases()
    {
        foreach (var mode in Enum.GetValues<PlayMode>())
        foreach (var saved in new[] { GoroseiMode.Warcury, GoroseiMode.Nasjuro })
        foreach (var status in new[] { GoroseiMarkerStatus.Unknown, GoroseiMarkerStatus.Conflict, GoroseiMarkerStatus.SelectedIdentity })
            yield return [mode, saved, status];
    }

    [Theory]
    [MemberData(nameof(GoroseiCases))]
    public void EveryModeUsesCurrentDetailedEffectsThroughActualConsumers(PlayMode mode,
        GoroseiMode saved, GoroseiMarkerStatus status)
    {
        var marker = new GoroseiMarkerSnapshot(status, saved, "fixture", []);
        var session = new GoroseiObservationSession(); session.Reset(1); session.Accept(1, 1, marker, true);
        var effective = MainWindow.ResolveObservationGorosei(mode, saved, session.Current);
        var strategy = new GoalStrategyProfile(1, 1, ArmorReductionTarget: 100, MagicArmorReductionTarget: 30);
        Assert.Equal(strategy, GoalStrategyCalculator.ApplyGorosei(strategy, effective));
        Assert.Equal(GoroseiMode.None, effective);
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var goal = catalog.Unit(BulletGuidePolicy.GoalId);
        InventoryEntry[] inventory = [new() { UnitId = goal.Id, Count = 1 }, new() { UnitId = "rawcode:X00h", Count = 1 }];
        var request = new RecommendationPipelineRequest {
            Mode = mode, Engine = new RecommendationEngine(catalog), Goal = goal, Inventory = inventory,
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = "Unselected",
            Gorosei = effective, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 40 };
        var actual = RecommendationPipeline.ComputeCandidates(request);
        var expected = RecommendationPipeline.ComputeCandidates(request with { Engine = new RecommendationEngine(catalog), Gorosei = GoroseiMode.None });
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected), System.Text.Json.JsonSerializer.Serialize(actual));
        var source = new AdaptivePlanningInputSource {
            MatchGeneration = 1, RecognitionRevision = 1, Round = 40, Phase = PlannerPhase.Committed,
            Inventory = inventory, Units = catalog.AllUnits.ToDictionary(x => x.Id), GoalUnitId = goal.Id,
            NavigationOptionId = "Unselected", GoroseiMode = effective, ManualLatches = ManualLatches.None };
        var factory = new AdaptivePlanningCoordinatorInputFactory(Path.Combine(AppContext.BaseDirectory, "Data"));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(factory.Create(source with { GoroseiMode = GoroseiMode.None })),
            System.Text.Json.JsonSerializer.Serialize(factory.Create(source)));
        var frame = NasjuroWispAdvicePolicyTests.Frame() with { Gorosei = session.Current };
        Assert.Equal(GoroseiMode.None, frame.Gorosei.EffectMode);
        Assert.NotEqual(NasjuroApplicability.Applicable, NasjuroWispAdvicePolicy.Evaluate(frame).Applicability);
    }

    [Theory]
    [InlineData(PlayMode.Beginner)]
    [InlineData(PlayMode.Normal)]
    [InlineData(PlayMode.Manual)]
    public void ExplicitOfflineScenarioSurvivesWithoutBecomingNativeEvidence(PlayMode mode)
    {
        var session = new GoroseiObservationSession(); session.Reset(1);
        var saved = GoroseiMode.Nasjuro;
        var explicitSelection = GoroseiMode.Warcury;
        Assert.Equal(explicitSelection, MainWindow.ResolveObservationGorosei(mode, saved,
            session.Current, explicitSelection, liveAutomatic: false));
        Assert.Equal(GoroseiMode.None, MainWindow.ResolveObservationGorosei(mode, saved,
            session.Current, explicitSelection, liveAutomatic: true));
        Assert.Equal(GoroseiMode.None, MainWindow.ResolveObservationGorosei(mode, saved,
            session.Current, liveAutomatic: false));
        Assert.Equal(GoroseiMode.None, session.Current.EffectMode);
        Assert.False(session.Current.IsCurrent);
        Assert.Equal(GoroseiMode.None, session.LastKnown);
    }

    [Theory]
    [InlineData(NativeNavigationStatus.Selected, false)]
    [InlineData(NativeNavigationStatus.Selected, true)]
    [InlineData(NativeNavigationStatus.Conflict, true)]
    [InlineData(NativeNavigationStatus.Unknown, true)]
    [InlineData(NativeNavigationStatus.Unknown, false)]
    public void HeaderAndSummaryConsumeTheSameNativeAndManualProvenance(NativeNavigationStatus status, bool confirmed)
    {
        var manual = new NavigationSessionState(); if (confirmed) manual.Confirm(ManualId);
        var native = new NativeNavigationSnapshot(status, status == NativeNavigationStatus.Selected ? NativeId : null, "fixture native status");
        Assert.Equal("불릿 · " + NativeNavigationPresentation.Describe(native, manual.ConfirmedOptionId),
            MainWindow.ObservationNavigationHeader("불릿", native, manual));
    }

    [Fact]
    public void RecoveryUsesFreshReaderResultAndRejectsOldSessionAndRevision()
    {
        var memory = new BulletNativeGlobalsTests.GlobalsMemory();
        memory.Array("JP", 9, 2); memory.Array("SP", 9, 0); memory.Array("cE", 13, 1); memory.Array("Ly", 13, 1);
        var incoming = new WarcraftNavigationReader().Read(memory.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, memory.Nodes["JP"]);
        Assert.Equal(NativeId, incoming.OptionId);
        var lost = MainWindow.NavigationAfterObservationLoss(MapSignals.Empty with { NativeNavigation = incoming });
        Assert.Equal(NativeNavigationStatus.Unknown, lost.NativeNavigation.Status);
        Assert.False(MainWindow.IsCurrentRecognition(1, 2, 1, 1)); // stopped scanner
        Assert.False(MainWindow.IsCurrentRecognition(2, 2, 1, 2)); // previous match
        Assert.False(MainWindow.IsCurrentRecognition(2, 2, 2, 2, false)); // replaced recognizer
        Assert.True(MainWindow.IsCurrentRecognition(2, 2, 2, 2)); // fresh Ready, including new generation
        var recovered = MapSignals.Empty with { NativeNavigation = incoming };
        Assert.Equal(NativeId, recovered.NativeNavigation.Resolve(ManualId));
        Assert.Equal("불릿 · " + NativeNavigationPresentation.Describe(incoming, null),
            MainWindow.ObservationNavigationHeader("불릿", recovered.NativeNavigation, new()));
        var gorosei = new GoroseiObservationSession(); gorosei.Reset(2);
        var identity = new GoroseiMarkerSnapshot(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Nasjuro, "identity", []);
        gorosei.Accept(2, 5, identity, true);
        gorosei.Accept(1, 99, GoroseiMarkerSnapshot.Unknown, true);
        gorosei.Accept(2, 4, GoroseiMarkerSnapshot.Unknown, true);
        Assert.Same(identity, gorosei.Current.Marker);
        foreach (var mode in new[] { PlayMode.Guide, PlayMode.Beginner, PlayMode.Normal, PlayMode.Manual })
            Assert.Equal(GoroseiMode.None, MainWindow.ResolveObservationGorosei(mode, GoroseiMode.Warcury, gorosei.Current));
        gorosei.Reset(3); gorosei.Accept(2, 99, identity, true);
        Assert.Equal(GoroseiMode.None, gorosei.Current.EffectMode);
        Assert.Equal(GoroseiMode.None, gorosei.LastKnown);
    }

    [Fact]
    public void CompiledProductionHandlersCallTheExecutedConsumers()
    {
        // IL wiring evidence only, not a launched WPF event/window.
        var scan = Calls("ScanCoreAsync");
        Assert.Equal(2, scan.Count(n => n == "NavigationAfterObservationLoss"));
        // Audited ScanCoreAsync: result fence, cancellation fence + invalidation,
        // then general-exception fence + invalidation. Loss still has exactly two consumers.
        const System.Reflection.BindingFlags consumerFlags = System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        System.Reflection.MethodBase Consumer(string name) => typeof(MainWindow).GetMethod(name, consumerFlags)!;
        var current = Consumer("IsCurrentRecognition");
        var loss = Consumer("NavigationAfterObservationLoss");
        var invalidate = Consumer("InvalidateDiagnosticInventoryObservation");
        var observe = Consumer("ObserveApplicationUpdateSafety");
        var audited = new[] { current, loss, invalidate, observe };
        Assert.Equal(new[] { current, observe, loss, current, invalidate, current, invalidate, loss },
            ResolvedCalls("ScanCoreAsync").Where(call => audited.Contains(call)).ToArray());
        Assert.Single(Calls("AutoScan_OnChanged"), n => n == "ApplyScanStopObservation");
        Assert.Single(Calls("StopControlledObservation"), n => n == "ApplyScanStopObservation");
        Assert.Contains("Apply", Calls("ApplyScanStopObservation"));
        Assert.Contains("NavigationAfterObservationLoss", Calls("ApplyScanStopObservation"));
        var refresh = Calls("RefreshAll");
        Assert.Contains("ResolveObservationGorosei", refresh);
        Assert.Contains("BuildAdaptivePlanningInput", refresh);
        Assert.Contains("ObservationNavigationHeader", refresh);
        Assert.Contains("RenderBeginnerCoach", refresh);
    }

    private static List<string> Calls(string name) => ResolvedCalls(name).Select(call => call.Name).ToList();

    private static List<System.Reflection.MethodBase> ResolvedCalls(string name)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var method = typeof(MainWindow).GetMethod(name, flags)!;
        var asyncType = method.GetCustomAttributes(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false)
            .Cast<System.Runtime.CompilerServices.AsyncStateMachineAttribute>().FirstOrDefault()?.StateMachineType;
        if (asyncType is not null) method = asyncType.GetMethod("MoveNext", flags)!;
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        var ops = typeof(System.Reflection.Emit.OpCodes).GetFields().Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode))
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
        var calls = new List<System.Reflection.MethodBase>();
        for (var i = 0; i < bytes.Length;)
        {
            ushort code = bytes[i++]; if (code == 0xfe) code = (ushort)(0xfe00 | bytes[i++]);
            var operand = ops[code].OperandType;
            if (operand == System.Reflection.Emit.OperandType.InlineMethod)
                calls.Add(method.Module.ResolveMethod(BitConverter.ToInt32(bytes, i))!);
            i += operand switch {
                System.Reflection.Emit.OperandType.InlineNone => 0,
                System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or System.Reflection.Emit.OperandType.ShortInlineVar => 1,
                System.Reflection.Emit.OperandType.InlineVar => 2,
                System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
                System.Reflection.Emit.OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, i),
                _ => 4 };
        }
        return calls;
    }

    private const string NativeId = "PathOfKings.BountyHunter";
    private const string ManualId = "AlliedForces.EmergencyCall";

    [Theory]
    [InlineData(NativeNavigationStatus.Selected, false)]
    [InlineData(NativeNavigationStatus.Selected, true)]
    [InlineData(NativeNavigationStatus.Conflict, false)]
    [InlineData(NativeNavigationStatus.Conflict, true)]
    public void ObservationLossDropsNativeButPreservesIndependentManualConfirmation(
        NativeNavigationStatus status, bool manual)
    {
        var session = new NavigationSessionState();
        if (manual) session.Confirm(ManualId);
        var signals = MapSignals.Empty with { NativeNavigation = new(status,
            status == NativeNavigationStatus.Selected ? NativeId : null, "current native") };
        // Same pure MainWindow transition is consumed by exception and AutoScan-off handlers.
        signals = MainWindow.NavigationAfterObservationLoss(signals);
        Assert.Equal(NativeNavigationStatus.Unknown, signals.NativeNavigation.Status);
        Assert.Equal(manual ? ManualId : null, signals.NativeNavigation.Resolve(session.ConfirmedOptionId));
        Assert.Equal(manual ? ManualId : null, session.ConfirmedOptionId);
        var frame = new CoachFrame { MatchGeneration = 1, Revision = 1, Round = 24,
            CompletedStoryStage = 5, Inventory = ImmutableDictionary<string, int>.Empty,
            NativeNavigation = signals.NativeNavigation,
            ConfirmedNavigation = signals.NativeNavigation.Resolve(session.ConfirmedOptionId), IsCurrent = false };
        Assert.Equal(signals.NativeNavigation, frame.NativeNavigation);
        var description = NativeNavigationPresentation.Describe(frame.NativeNavigation, session.ConfirmedOptionId);
        Assert.Equal(NativeNavigationPresentation.Describe(NativeNavigationSnapshot.Unknown, manual ? ManualId : null), description);
        Assert.NotEqual(NativeNavigationPresentation.Describe(new(NativeNavigationStatus.Selected, NativeId, "fixture"), null), description);
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Equal(CoachActionKind.Recognition, decision.Kind);
    }
}
