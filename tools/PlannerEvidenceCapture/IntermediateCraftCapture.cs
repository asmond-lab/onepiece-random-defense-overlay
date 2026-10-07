using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void CaptureIntermediateCraft(string output, PlayMode mode = PlayMode.Normal)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var main = new MainWindow(FixtureContext(new AppSettings
        {
            Mode = mode, GoalUnitId = "rawcode:690H", AutoScanEnabled = true,
            TelemetryEnabled = false, ClearDataAutoRefresh = false
        }));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var commitment = (IntermediateCraftCommitment)typeof(MainWindow).GetField("_craftCommitment", flags)!.GetValue(main)!;
        var recognizer = new CoachFrameRecognizer();

        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var rows = new List<object>();
        try
        {
            main.Show();
            main.Left = SystemParameters.VirtualScreenLeft - main.Width - 20;
            var before = catalog.Unit("rawcode:X90h").Recipe.Select(pair =>
                new InventoryEntry { UnitId = pair.Key, Count = pair.Value })
                .Append(new InventoryEntry { UnitId = "rawcode:690H" }).ToArray();
            Observe(before);
            commitment.Reset();
            // Reconstruct a previously offered, executable Nekomamushi recipe using real
            // recipe allocation and current-combat validation, not a fabricated craft permission.
            var engine = new RecommendationEngine(catalog);
            var recipe = engine.Recascade([new Recommendation
            {
                Route = new RouteDefinition { Id = "craft:rawcode:Z90h", GoalUnitId = "rawcode:Z90h", Name = "Nekomamushi" }
            }], before, null);
            recipe[0].CurrentCraft = new CurrentCraftPolicy(catalog.Unit, catalog.Unit("rawcode:690H"),
                before.ToDictionary(entry => entry.UnitId, entry => entry.Count),
                GoalStrategyCalculator.StrategyProfileFor(catalog.Unit("rawcode:690H"))!.Value,
                20, 13, new RecipeCompletionCalculator(catalog.Unit)).Assess(catalog.Unit("rawcode:Z90h"));
            var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
            var drake = new AutoCombinePlanner(catalog, hotkeys).Plan(recipe, before)
                .Single(step => step.TargetUnitId == "rawcode:X90h");
            var offered = WaitCoach(main, () => typeof(MainWindow).GetMethod("RenderBeginnerCoach", flags)!
                    .Invoke(main, [recipe, new[] { drake }, Array.Empty<RareRerollAdvice>(),
                        Array.Empty<SpecialDismantleAdvice>(), Array.Empty<GreenBloodAdvice>(), false,
                        Array.Empty<EmergencySummonAdvice>()]), (_, _) => true);
            if (offered.Decision.Kind != CoachActionKind.Craft || commitment.TargetUnitId != "rawcode:Z90h")
                throw new InvalidOperationException("The rendered actionable recipe was not committed.");
            InventoryEntry[] after = [Entry("rawcode:690H"), Entry("rawcode:X90h"),
                Entry("rawcode:S00h"), Entry("rawcode:210h"), Entry("rawcode:Q00h")];
            var progressed = Observe(after);
            if (progressed.Frame.Recommendations[0].Route.GoalUnitId != "rawcode:Z90h")
                throw new InvalidOperationException("Crafted Drake investment was abandoned after a fresh production scan.");
            if (progressed.Frame.CommittedCraftUnitId != "rawcode:Z90h" ||
                progressed.Decision.PreservedMaterialCounts.GetValueOrDefault("rawcode:X90h") != 1)
                throw new InvalidOperationException("Committed Nekomamushi does not preserve the observed Drake.");
            VerifyPreservation(progressed, "drake-preserved");
            var repeated = WaitCoach(main, () => typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(main, [null]),
                (_, frame) => frame.Revision > progressed.Frame.Revision);
            if (repeated.Frame.Recommendations[0].Route.GoalUnitId != "rawcode:Z90h")
                throw new InvalidOperationException("An unchanged refresh abandoned the intermediate commitment.");
            var heldFrame = repeated.Frame with
            {
                Story = new StoryRewardSequenceDecision(RecommendationSequenceStage.StoryReward,
                    StorySequenceAction.WaitForStoryReward, "", "", "", "", "", null, null, 0, false)
            };
            var held = new BeginnerCoachPlanner(catalog).Decide(heldFrame);
            if (!held.CraftDeferredForReward || held.CraftRecipe is not null ||
                held.Kind != CoachActionKind.Story || held.PreservedMaterialCounts.GetValueOrDefault("rawcode:X90h") != 1)
                throw new InvalidOperationException("Reward hold lost Drake preservation or authorized a craft.");
            ((BeginnerCoachView)main.FindName("MainCoachView")).Render(held, heldFrame, null);
            overlay.RenderCoach(true, held, heldFrame, null);
            VerifyPreservation((held, heldFrame), "reward-hold");
            Observe([Entry("rawcode:690H"), Entry("rawcode:Z90h")]);
            if (commitment.TargetUnitId == "rawcode:Z90h")
                throw new InvalidOperationException("Completed intermediate recipe remained pinned.");
            WriteEvidenceText(Path.Combine(output, "intermediate-craft.json"), JsonSerializer.Serialize(new
            {
                Kind = "real-WPF-offered-recipe-and-production-scan-sequence",
                Limitation = "Controlled prior recipe and synthetic recognition; no Warcraft input.", Rows = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("INTERMEDIATE_CRAFT PASS offered=Drake target=Nekomamushi repeated=retained completed=released");
        }
        finally
        {
            main.Close();
            overlay.Stats.CloseForApplication();
            overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        (CoachDecision Decision, CoachFrame Frame) Observe(InventoryEntry[] inventory)
        {
            recognizer.Next = new RecognitionResult
            {
                State = RecognitionState.Ready, Entries = inventory.ToList(), MapSignals = MapSignals.Empty,
                Diagnostics = new RecognitionDiagnostics
                { ObservedObjects = inventory.Length, MappedObjects = inventory.Length, ForeignObjects = 8,
                    MapState = new MapStateSample(20, 0, "신") }
            };
            var result = WaitCoach(main, () => _ = ScanFixtureAsync(main, recognizer.Next),
                (_, frame) => frame.IsCurrent && frame.Inventory.Count == inventory.Length);
            rows.Add(new { result.Frame.GoalId, Target = result.Frame.Recommendations.FirstOrDefault()?.Route.GoalUnitId,
                result.Decision.Kind, result.Decision.TargetUnitId });
            return result;
        }
        static InventoryEntry Entry(string id) => new() { UnitId = id };

        void VerifyPreservation((CoachDecision Decision, CoachFrame Frame) result, string name)
        {
            foreach (var surface in new[] { (BeginnerCoachView)main.FindName("MainCoachView"), overlay.BeginnerView })
            {
                var text = (System.Windows.Controls.TextBlock)surface.FindName("PreserveText");
                if (text.Visibility != Visibility.Visible || text.Text != result.Decision.PreservedMaterials ||
                    System.Windows.Automation.AutomationProperties.GetName(text) != text.Text)
                    throw new InvalidOperationException("Preserved materials differ between the decision and WPF surface.");
                if (result.Decision.CraftDeferredForReward &&
                    ((System.Windows.Controls.TextBlock)surface.FindName("ConfirmationText")).Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Reward resumption condition is hidden.");
            }
            rows.Add(new { Name = name, result.Frame.CommittedCraftUnitId, result.Decision.PreservedMaterialCounts,
                result.Decision.PreservedMaterials, result.Decision.CraftDeferredForReward });
            CaptureDifficultyWindows(main, overlay, result, output, name);
        }
    }
}
