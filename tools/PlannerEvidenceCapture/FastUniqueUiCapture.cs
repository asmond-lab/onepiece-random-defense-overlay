using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OrandOverlay;
namespace PlannerEvidenceCapture;

// Compiled only in the separately bound focused binary. The ordinary capture entry is unchanged.
#if FAST_UNIQUE_UI
internal static class FastUniqueUiEntry
{
    [STAThread]
    private static int Main(string[] args) => Program.RunFastUniqueUi(args);
}
#endif

internal static partial class Program
{
    internal static int RunFastUniqueUi(string[] args)
    {
        FastUniqueUiArguments.Validate(args);
        var output = args[1];
        using var scope = CaptureOutputScope.Create(Path.GetTempPath(), output);
        Output = scope;
        CaptureInputContract.ValidateBundledInputs(Path.Combine(AppContext.BaseDirectory, "Data"));
        CaptureInputContract.InitializeBundledAllowlist();
        var app = new App { Execution = FixtureContext(new AppSettings { TelemetryEnabled = false }) };
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        typeof(Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, null);
        try { return CaptureFastUniqueUi(output, args[3]); }
        finally { app.Shutdown(); }
    }

    private static int CaptureFastUniqueUi(string output, string binding)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var main = new MainWindow(FixtureContext(new AppSettings
            { Mode = PlayMode.Guide, GuideNumber = 1, TelemetryEnabled = false }));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog", flags)!.GetValue(main)!;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        var rows = new List<object>();
        var violations = new List<string>();
        try
        {
            main.Show(); main.Left = SystemParameters.VirtualScreenLeft - main.Width - 20;
            overlay.Show(); overlay.Left = SystemParameters.VirtualScreenLeft - overlay.Width - 20;
            foreach (var original in FastUniqueUiFixture.Build(catalog))
            {
                var item = original;
                var generation = ((AdaptivePlanningCompositionRoot)typeof(MainWindow)
                    .GetField("_adaptivePlanning", flags)!.GetValue(main)!).MatchGeneration + (item.Boundary ? 1 : 0);
                var revision = (long)typeof(MainWindow).GetField("_recognitionRevision", flags)!.GetValue(main)! + 1;
                var request = FastUniqueUiFixture.Request(item, generation, revision);
                var inventory = request.Entries.ToImmutableDictionary(e => e.UnitId, e => e.Count);
                var result = WaitCoach(main, () => _ = ScanFixtureAsync(main, request), (_, frame) =>
                    BulletGuideRowProjection.MatchesObservation(frame, item.Round, generation, revision,
                        inventory, request.Diagnostics.GoroseiMarker));
                var frame = result.Frame; var decision = result.Decision;
                var rendered = new List<object>();
                foreach (var surface in new[] { view, overlay.BeginnerView })
                {
                    surface.UpdateLayout();
                    var texts = new[] { "ActionText", "ControlsText", "ReasonText", "UnknownText" }
                        .ToDictionary(name => name, name => ((TextBlock)surface.FindName(name)).Text);
                    if (texts["ActionText"] != decision.Title || texts["ControlsText"] != decision.Controls ||
                        texts["ReasonText"] != decision.Reason || texts["UnknownText"] !=
                            (string.IsNullOrEmpty(decision.UnknownSignals) ? "" : "게임에서 확인:\n" + decision.UnknownSignals))
                        throw new InvalidOperationException("Accepted decision and rendered surface disagree.");
                    rendered.Add(texts);
                    ((ScrollViewer)surface.FindName("CoachScroll")).ScrollToTop();
                }
                if (item.Name == "deadline-8" && frame.GuidePlan?.TargetUnitId != "rawcode:M30h")
                    violations.Add(item.Name + ": hidden opening not restored after deadline");
                if (item.Name == "rare-consumed-7" && frame.GuidePlan?.FastUnique != FastUniqueState.RarePreviouslyObserved)
                    violations.Add(item.Name + ": consumed rare observation history lost");
                if (item.Name == "new-session-7" && frame.GuidePlan?.Stage != BulletGuideStage.FastUniqueRare)
                    violations.Add(item.Name + ": new Ready boundary retained previous rare history");
                if (item.Name.StartsWith("first-rare-") &&
                    (frame.GuidePlan?.Stage != (item.Round == 0 ? BulletGuideStage.RoundUnknown : BulletGuideStage.FastUniqueRare) ||
                     decision.Kind != (item.Round == 0 ? CoachActionKind.Recognition : CoachActionKind.Craft)))
                    violations.Add(item.Name + ": unknown-round hold or first-rare craft mismatch");
                if (item.Name.EndsWith("-received") || item.Name.EndsWith("-repeat"))
                {
                    var reward = item.Name.Split('-')[0];
                    if (decision.Kind != CoachActionKind.Reward || decision.RewardWispId != reward)
                        violations.Add(item.Name + ": observed reward hidden by producer/consumer");
                }
                if (item.Name.EndsWith("-ready-rare") && decision.Kind != CoachActionKind.Craft)
                    violations.Add(item.Name + ": ready rare did not precede reward");
                if (item.Name.EndsWith("-spent") || item.Name.EndsWith("-unreceived"))
                    if (decision.Kind == CoachActionKind.Reward) violations.Add(item.Name + ": unowned reward actionable");
                if (item.Name is "selection-1" or "selection-3" or "selection-after-one")
                {
                    if (decision.RewardWispId == "e018" || decision.SelectionBatch is not null ||
                        frame.RewardWisps.GetValueOrDefault("e018") != item.Wisps.GetValueOrDefault("e018"))
                        violations.Add(item.Name + ": nonurgent selection was not conserved");
                }
                if (item.Name.StartsWith("actual-"))
                    if (frame.GuidePlan?.PlannedNavigation != BulletGuidePolicy.NavigationId ||
                        frame.NativeNavigation.Status != item.Navigation.Status ||
                        frame.ConfirmedNavigation != item.Navigation.OptionId)
                        violations.Add(item.Name + ": planned and actual navigation conflated");
                if (item.Name == "bullet-zero-to-one" &&
                    (decision.Kind != CoachActionKind.Craft || decision.TargetUnitId != BulletGuidePolicy.GoalId))
                    violations.Add(item.Name + ": fresh supported Bullet craft missing");
                if (item.Name == "bullet-owned-no-recraft" && frame.CraftSteps.Any(s => s.TargetUnitId == BulletGuidePolicy.GoalId))
                    violations.Add(item.Name + ": fresh producer repeated owned Bullet");
                if (frame.GuidePlan?.FastUnique == FastUniqueState.CompletedVerified)
                    throw new InvalidOperationException("Synthetic hand cannot produce a native success receipt.");
                var capture = !item.Name.StartsWith("first-rare-") || item.Round is 0 or 7;
                var pngs = capture ? new[] { $"main-{item.Name}.png", $"overlay-{item.Name}.png" } : [];
                if (capture)
                {
                    main.UpdateLayout(); overlay.UpdateLayout();
                    SaveCoachWindow(main, Path.Combine(output, pngs[0]));
                    SaveCoachWindow(overlay, Path.Combine(output, pngs[1]));
                }
                rows.Add(new { item.Name, Kind = "synthetic-recognition-production-accepted-frame-real-WPF",
                    Request = new { item.Round, item.Boundary, request.Status, Inventory = inventory, item.Wisps,
                        item.Navigation, item.Lumber, ExpectedGeneration = generation, ExpectedRevision = revision },
                    Projection = BulletGuideRowProjection.Observe(item.Name, decision, frame),
                    frame.Story, frame.NativeNavigation, frame.RewardWisps, frame.Signals,
                    Rendered = rendered, Pngs = pngs });
                // Unique per-row checkpoint survives later failure without rewriting prior evidence.
                WriteEvidenceText(Path.Combine(output, $"row-{item.Name}.json"), JsonSerializer.Serialize(rows[^1]));
            }
            WriteEvidenceText(Path.Combine(output, "fast-unique-ui.json"), JsonSerializer.Serialize(new
            {
                Kind = "synthetic-focused-WPF-not-live-game", BuildBinding = binding,
                Limitation = "No native receipt, game input or production runtime service. Headless model is separate evidence.",
                Rows = rows, Violations = violations
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"FOCUSED_UI rows={rows.Count} violations={violations.Count}");
            return violations.Count == 0 ? 0 : 2;
        }
        finally
        {
            main.Close(); overlay.Stats.CloseForApplication(); overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
}
