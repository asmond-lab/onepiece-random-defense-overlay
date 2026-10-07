using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void CaptureGoalConsistency(string output)
    {
        const string lawId = "rawcode:690H";
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var settings = new AppSettings
        {
            Mode = PlayMode.Normal, GoalUnitId = "yamato_transcendent",
            AutoScanEnabled = true, TelemetryEnabled = false, ClearDataAutoRefresh = false
        };
        var main = new MainWindow(FixtureContext(settings));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var recognizer = new CoachFrameRecognizer();

        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var rows = new List<object>();
        try
        {
            main.Show();
            main.Left = SystemParameters.VirtualScreenLeft - main.Width - 20;
            foreach (var round in new[] { 0, 20, 30 })
            {
                recognizer.Next = new RecognitionResult
                {
                    State = RecognitionState.Ready,
                    Entries = [new InventoryEntry { UnitId = lawId }, new InventoryEntry { UnitId = "rawcode:S20h" }],
                    MapSignals = MapSignals.Empty,
                    Diagnostics = new RecognitionDiagnostics
                    {
                        ObservedObjects = 2, MappedObjects = 2, ForeignObjects = 8,
                        MapState = new MapStateSample(round, 0, "신")
                    }
                };
                var result = WaitCoach(main,
                    () => _ = ScanFixtureAsync(main, recognizer.Next),
                    (_, frame) => frame.IsCurrent && frame.Round == round && frame.Inventory.ContainsKey(lawId));
                if (result.Frame.GoalId != lawId)
                    throw new InvalidOperationException($"Owned Law was replaced by {result.Frame.GoalId ?? "no goal"} at round {round}.");
                var readiness = result.Frame.Recommendations.FirstOrDefault()?.CombatReadiness;
                if (readiness is not { DamageType: ReadinessDamageType.Magic, RequiredArmorReduction: 0, RequiredMagicArmorSources: 1 })
                    throw new InvalidOperationException("The owned Law plan did not carry magic readiness.");
                var expected = RecommendationPresentation.ReadinessLine(CombatReadinessCalculator.Calculate(
                    catalog, catalog.Unit(lawId), recognizer.Next.Entries, "신"));
                var displayed = ((TextBlock)overlay.Stats.FindName("ReadinessSummaryText")).Text;
                if (displayed != expected)
                    throw new InvalidOperationException($"Stats readiness differs from owned Law: {displayed}");
                rows.Add(new { Round = round, result.Frame.GoalId, readiness.DamageType, DisplayedReadiness = displayed });
            }
            main.UpdateLayout();
            SaveCoachWindow(main, Path.Combine(output, "main-law.png"));
            overlay.Stats.Show();
            overlay.Stats.Left = SystemParameters.VirtualScreenLeft - overlay.Stats.Width - 20;
            overlay.Stats.UpdateLayout();
            SaveCoachWindow(overlay.Stats, Path.Combine(output, "stats-law.png"));
            WriteEvidenceText(Path.Combine(output, "goal-consistency.json"), JsonSerializer.Serialize(new
            {
                Kind = "production-scan-and-WPF-with-controlled-owned-Law",
                Limitation = "Synthetic recognition, no live Warcraft writes or application restarts.", Rows = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"GOAL_CONSISTENCY PASS scenarios={rows.Count}");
        }
        finally
        {
            main.Close();
            overlay.Stats.CloseForApplication();
            overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
