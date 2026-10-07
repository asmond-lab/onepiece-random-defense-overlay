using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using OrandOverlay;

namespace PlannerEvidenceCapture;

#if STORY_REWARD
internal static class StoryRewardEntry
{
    [STAThread]
    private static int Main(string[] args) => Program.RunStoryReward(args);
}
#endif

internal static partial class Program
{
    internal static int RunStoryReward(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]) ||
            Path.GetFileName(Path.GetDirectoryName(args[0])) != ".story-reward-artifacts")
            throw new ArgumentException("A fresh direct child of .story-reward-artifacts is required.");
        using var scope = CaptureOutputScope.Create(Path.GetDirectoryName(args[0])!, args[0]);
        Output = scope;
        CaptureInputContract.ValidateBundledInputs(Path.Combine(AppContext.BaseDirectory, "Data"));
        CaptureInputContract.InitializeBundledAllowlist();
        var app = new App { Execution = FixtureContext(new AppSettings { TelemetryEnabled = false }) };
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        typeof(Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, null);
        try { return CaptureStoryReward(args[0]); }
        finally { app.Shutdown(); }
    }

    private static int CaptureStoryReward(string output)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var main = new MainWindow(FixtureContext(new AppSettings
        { Mode = PlayMode.Guide, GuideNumber = 1, TelemetryEnabled = false, ClearDataAutoRefresh = false, AutoScanEnabled = true }));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog", flags)!.GetValue(main)!;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var rows = new List<object>();
        try
        {
            main.Show(); main.Left = SystemParameters.VirtualScreenLeft - main.Width - 40;
            overlay.Show(); overlay.Left = SystemParameters.VirtualScreenLeft - overlay.Width - 40;
            var recorded = RandipickCleanupFixture.Build(catalog).Single(item => item.Name == "legend-chain-0");
            var hand = recorded.Inventory.ToImmutableDictionary();
            var before = hand.SetItem("luffy_common", hand["luffy_common"] - 1);
            var basis = recorded with { Round = 9, Story = 3, Inventory = before, Boundary = true };
            Apply(basis with { Name = "first-rare-hold" }, CoachActionKind.Story);
            Apply(basis with { Name = "fourth-reward-held", Story = 4, Boundary = false,
                Wisps = ImmutableDictionary<string, int>.Empty.Add("e016", 1) }, CoachActionKind.Reward);
            Apply(basis with { Name = "reward-decreased-hand-pending", Story = 4, Boundary = false }, CoachActionKind.Story);
            var unrelated = Apply(basis with { Name = "unrelated-common-still-pending", Story = 4,
                Inventory = hand, Boundary = false }, CoachActionKind.Story);
            if (!unrelated.Frame.GuidePlan!.AwaitingRewardHand || unrelated.Frame.GuidePlan.FirstLegendRewardHandObserved)
                throw new InvalidOperationException("Unrelated common gain was treated as a special-wisp result.");
            if (TopGradePolicy.BaseTier(catalog.Unit("rawcode:C10h").Tier) != "특별함")
                throw new InvalidOperationException("The fixture output must belong to the special-wisp pool.");
            hand = hand.SetItem("rawcode:C10h", hand["rawcode:C10h"] + 1);
            basis = basis with { Story = 4, Inventory = hand, Boundary = false };
            string[] expected = ["F00h", "V00h", "V00h", "220h", "E20h", "J20h", "B30h"];
            foreach (var step in expected.Select((code, index) => (code, index)))
            {
                var accepted = Apply(basis with { Name = "reward-chain-" + step.index }, CoachActionKind.Craft);
                if (accepted.Decision.TargetUnitId != "rawcode:" + step.code ||
                    !accepted.Frame.GuidePlan!.FirstLegendRewardHandObserved)
                    throw new InvalidOperationException("Rewarded seven-craft path changed.");
                var after = BulletGuideCraftSafety.ProjectAfterCraft(catalog, accepted.Decision.TargetUnitId, basis.Inventory)!;
                basis = basis with { Inventory = after.Where(pair => pair.Value > 0).ToImmutableDictionary() };
            }
            var complete = Apply(basis with { Name = "reward-chain-complete", Lumber = 13 }, null);
            if (complete.Frame.GuidePlan!.Stage != BulletGuideStage.SecondLegend ||
                complete.Frame.Inventory.GetValueOrDefault("rawcode:B30h") != 1)
                throw new InvalidOperationException("Observed first legend did not advance the guide.");
            Apply(recorded with { Name = "late-attach-round10", Round = 10, Story = 0, Boundary = true }, CoachActionKind.Craft);
            Apply(recorded with { Name = "late-attach-zero-not-receipt", Round = 9, Story = 4, Boundary = true }, CoachActionKind.Story);
            foreach (var id in new[] { "e0IX", "e01A" })
            {
                var unknown = recorded with { Name = id + "-owned", Round = 9, Story = 4, Boundary = true,
                    Wisps = ImmutableDictionary<string, int>.Empty.Add(id, 1) };
                Apply(unknown, CoachActionKind.Reward);
                unknown = unknown with { Name = id + "-spent-unknown", Boundary = false,
                    Wisps = ImmutableDictionary<string, int>.Empty };
                var spent = Apply(unknown, CoachActionKind.Story);
                var deadline = Apply(unknown with { Name = id + "-deadline", Round = 10 }, CoachActionKind.Craft);
                if (spent.Frame.GuidePlan!.AwaitingRewardHand || spent.Frame.GuidePlan.FirstLegendRewardHandObserved ||
                    deadline.Frame.GuidePlan!.AwaitingRewardHand || deadline.Frame.GuidePlan.FirstLegendRewardHandObserved)
                    throw new InvalidOperationException("Unsupported output fabricated checkpoint evidence or permanent pending debt.");
            }
            var early = recorded with { Name = "output-first-offer", Round = 7, Story = 4, Boundary = true,
                Wisps = ImmutableDictionary<string, int>.Empty.Add("e017", 2) };
            Apply(early, CoachActionKind.Reward);
            var earlyHand = early.Inventory.ToImmutableDictionary()
                .SetItem("rawcode:A00h", early.Inventory.GetValueOrDefault("rawcode:A00h") + 1)
                .SetItem("rawcode:N00h", early.Inventory.GetValueOrDefault("rawcode:N00h") + 1);
            early = early with { Name = "output-first-units-present", Inventory = earlyHand, Boundary = false };
            Apply(early, CoachActionKind.Reward);
            var released = Apply(early with { Name = "output-first-released",
                Wisps = ImmutableDictionary<string, int>.Empty }, CoachActionKind.Craft);
            if (released.Frame.GuidePlan!.AwaitingRewardHand || !released.Frame.GuidePlan.FirstLegendRewardHandObserved ||
                released.Frame.Inventory.GetValueOrDefault("rawcode:A00h") != earlyHand["rawcode:A00h"] ||
                released.Frame.Inventory.GetValueOrDefault("rawcode:N00h") != earlyHand["rawcode:N00h"])
                throw new InvalidOperationException("Previously observed reward units did not release the reward hold.");
            WriteEvidenceText(Path.Combine(output, "story-reward.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"STORY_REWARD PASS rows={rows.Count} crafts=7; synthetic accepted WPF, no game access");
            return 0;
        }
        finally
        {
            main.Close(); overlay.Stats.CloseForApplication(); overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        (CoachDecision Decision, CoachFrame Frame) Apply(FastUniqueUiCase item, CoachActionKind? expectedKind)
        {
            var generation = ((AdaptivePlanningCompositionRoot)typeof(MainWindow).GetField("_adaptivePlanning", flags)!.GetValue(main)!)
                .MatchGeneration + (item.Boundary ? 1 : 0);
            var revision = (long)typeof(MainWindow).GetField("_recognitionRevision", flags)!.GetValue(main)! + 1;
            var request = FastUniqueUiFixture.Request(item, generation, revision);
            var result = WaitCoach(main, () => _ = ScanFixtureAsync(main, request), (_, frame) =>
                BulletGuideRowProjection.MatchesObservation(frame, item.Round, generation, revision,
                    item.Inventory, request.Diagnostics.GoroseiMarker));
            if (expectedKind is { } kind && result.Decision.Kind != kind)
                throw new InvalidOperationException($"{item.Name}: expected {kind}, got {result.Decision.Kind}");
            rows.Add(new { item.Name, result.Decision, result.Frame.GuidePlan, result.Frame.RewardWisps,
                result.Frame.CompletedStoryStage, result.Frame.Round, result.Frame.Inventory });
            if (item.Name is "first-rare-hold" or "unrelated-common-still-pending" or "reward-chain-0" or "reward-chain-complete"
                or "output-first-units-present" or "output-first-released")
            {
                main.UpdateLayout(); overlay.UpdateLayout();
                SaveCoachWindow(main, Path.Combine(output, "main-" + item.Name + ".png"));
                SaveCoachWindow(overlay, Path.Combine(output, "overlay-" + item.Name + ".png"));
            }
            return result;
        }
    }
}
