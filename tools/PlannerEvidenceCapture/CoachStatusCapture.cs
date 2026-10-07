using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void CaptureCoachStatus(string output)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var main = new MainWindow(FixtureContext(new AppSettings
        {
            Mode = PlayMode.Beginner, GoalUnitId = "rawcode:690H", AutoScanEnabled = true,
            TelemetryEnabled = false, ClearDataAutoRefresh = false
        }));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        var recognizer = new CoachFrameRecognizer();

        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog", flags)!.GetValue(main)!;
        var source = catalog.AllUnits.Where(unit => !TopGradePolicy.IsTopGrade(unit.Tier) &&
                GoalStrategyCalculator.StrategyMetricsFor(unit).MagicArmorReduction > 0)
            .OrderBy(unit => unit.Id, StringComparer.Ordinal).First();
        var rows = new List<object>();
        try
        {
            main.Show();
            main.Left = SystemParameters.VirtualScreenLeft - main.Width - 20;
            foreach (var mode in new[] { PlayMode.Beginner, PlayMode.Normal })
            {
                if (mode == PlayMode.Normal)
                    WaitCoach(main, () => ((ComboBox)main.FindName("PlayModeCombo")).SelectedItem =
                        PlayModes.Options.Single(option => option.Mode == mode), (_, frame) => frame.Mode == mode);
                foreach (var (name, goalId, sources) in new[]
                {
                    ("law-zero", "rawcode:690H", 0), ("law-one", "rawcode:690H", 1),
                    ("physical", "yamato_transcendent", 0)
                })
                {
                    // Different owned tops represent separate matches; only Law's source addition is continuous.
                    if (name != "law-one")
                        typeof(MainWindow).GetMethod("ResetMatchSession", flags)!.Invoke(main, null);
                    var entries = new List<InventoryEntry> { new() { UnitId = goalId } };
                    if (sources > 0) entries.Add(new InventoryEntry { UnitId = source.Id });
                    recognizer.Next = new RecognitionResult
                    {
                        State = RecognitionState.Ready, Entries = entries, MapSignals = MapSignals.Empty,
                        Diagnostics = new RecognitionDiagnostics
                        { ObservedObjects = entries.Count, MappedObjects = entries.Count, ForeignObjects = 8,
                            MapState = new MapStateSample(20, 0, "신") }
                    };
                    var result = WaitCoach(main,
                        () => _ = ScanFixtureAsync(main, recognizer.Next),
                        (_, frame) => frame.IsCurrent && frame.Inventory.ContainsKey(goalId) && frame.Inventory.Count == entries.Count);
                    var readiness = result.Frame.Recommendations.FirstOrDefault()?.CombatReadiness;
                    var expected = CombatReadinessCalculator.Calculate(catalog, catalog.Unit(goalId), entries, "신");
                    if (result.Frame.GoalId != goalId || readiness != expected ||
                        sources != expected.CurrentMagicArmorSources ||
                        expected.DamageType != (name == "physical" ? ReadinessDamageType.Physical : ReadinessDamageType.Magic))
                        throw new InvalidOperationException("Production coach readiness differs from the owned goal/inventory.");
                    foreach (var surface in new[] { view, overlay.BeginnerView })
                    {
                        var text = (TextBlock)surface.FindName("ReadinessSummary");
                        if (text.Visibility != Visibility.Visible ||
                            text.Text.Split('\n')[0] != RecommendationPresentation.ReadinessLine(expected) ||
                            AutomationProperties.GetName(text) != text.Text)
                            throw new InvalidOperationException("Coach readiness omits damage-specific values or differs from accessible text.");
                    }
                    CaptureDifficultyWindows(main, overlay, result, output, mode + "-" + name);
                    rows.Add(new { Mode = mode, Name = name, result.Frame.GoalId, Readiness = expected });
                    foreach (var hidden in new[] { result.Frame with { Difficulty = "unknown" },
                                 result.Frame with { GoalId = null }, result.Frame with { Outcome = "clear" } })
                    {
                        var decision = new BeginnerCoachPlanner(catalog).Decide(hidden);
                        view.Render(decision, hidden, null);
                        overlay.RenderCoach(true, decision, hidden, null);
                        foreach (var surface in new[] { view, overlay.BeginnerView })
                            if (((TextBlock)surface.FindName("ReadinessSummary")).Visibility != Visibility.Collapsed)
                                throw new InvalidOperationException("Unknown goal/difficulty or finished game exposes readiness.");
                    }
                }
            }
            WriteEvidenceText(Path.Combine(output, "coach-status.json"), JsonSerializer.Serialize(new
            {
                Kind = "production-scan-real-WPF-main-overlay", Rows = rows,
                Limitation = "Synthetic recognition; layout scales 100/125/150, not hardware DPI or live Warcraft."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"COACH_STATUS PASS readiness={rows.Count} hidden={rows.Count * 3} surfaces=2 scales=3");
        }
        finally
        {
            main.Close(); overlay.Stats.CloseForApplication(); overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        foreach (var mode in new[] { PlayMode.Beginner, PlayMode.Normal })
        {
            var sequenceOutput = Path.Combine(output, mode + "-sequence");
            Output.EnsureDirectory(sequenceOutput);
            CaptureIntermediateCraft(sequenceOutput, mode);
        }
    }
}
