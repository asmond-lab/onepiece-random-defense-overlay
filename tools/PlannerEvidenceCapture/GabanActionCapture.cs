using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void CaptureGabanActions(string output)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("rawcode:F40h");
        var killer = catalog.Unit("rawcode:540h");
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(
            AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var materials = killer.Recipe.Select(pair =>
            new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var snapshots = new[]
        {
            (Name: "pre-boss", Round: 29, Inventory: materials),
            (Name: "boss", Round: 30, Inventory: materials),
            (Name: "after-killer", Round: 30, Inventory:
                new[] { new InventoryEntry { UnitId = killer.Id } }),
            (Name: "gaban-materials", Round: 40, Inventory: goal.Recipe.Select(pair =>
                new InventoryEntry { UnitId = pair.Key, Count = pair.Value })
                .Append(new InventoryEntry { UnitId = killer.Id }).ToArray()),
            (Name: "after-gaban", Round: 55, Inventory:
                new[] { new InventoryEntry { UnitId = goal.Id }, new InventoryEntry { UnitId = killer.Id } })
        }.ToList();
        var resumeInventory = snapshots.Single(item => item.Name == "gaban-materials").Inventory.ToList();
        var resumeEngine = new RecommendationEngine(catalog);
        for (var step = 0; step < 8; step++)
        {
            var next = resumeEngine.RecommendNearestCrafts(goal.Id, resumeInventory,
                difficulty: "신", round: 40, completedStoryStage: 13)[0];
            if (next.Route.GoalUnitId == goal.Id) break;
            resumeInventory.Add(new InventoryEntry { UnitId = next.Route.GoalUnitId });
        }
        snapshots.Add((Name: "gaban-resume", Round: 40, Inventory: resumeInventory.ToArray()));
        var rows = new List<object>();
        foreach (var snapshot in snapshots)
        {
            var engine = new RecommendationEngine(catalog);
            var finalBuild = engine.RecommendNearestCrafts(goal.Id, snapshot.Inventory,
                difficulty: "신");
            var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
            {
                Engine = engine, Goal = goal, Inventory = snapshot.Inventory,
                InitialSurface = RecommendationSurface.TopAndNavigation,
                NavigationMode = "PathOfKings.BountyHunter", Gorosei = GoroseiMode.None,
                BuildVariant = BuildVariants.AutoId, Difficulty = "신",
                Round = snapshot.Round, CompletedStoryStage = 13
            });
            var result = RecommendationPipeline.Finalize(candidates, catalog, goal,
                snapshot.Inventory, new FirstRareTargetPolicy(), snapshot.Round, 13, "신");
            var recommendations = engine.Recascade(result.Recommendations, snapshot.Inventory, null);
            var plan = new AutoCombinePlanner(catalog, hotkeys).Plan(
                recommendations.Take(1).ToArray(), snapshot.Inventory);
            if (snapshot.Name is "pre-boss" or "boss" &&
                (recommendations[0].Route.GoalUnitId != killer.Id ||
                 plan.FirstOrDefault()?.TargetUnitId != killer.Id))
                throw new InvalidOperationException("Ready Killer must be the immediate Gaban survival action.");
            if (snapshot.Name == "after-killer" &&
                recommendations.Any(item => item.Route.GoalUnitId == killer.Id))
                throw new InvalidOperationException("Completed Killer was recommended again.");
            if (snapshot.Name == "after-gaban" &&
                recommendations.Any(item => item.Route.GoalUnitId == goal.Id))
                throw new InvalidOperationException("Completed Gaban was recommended again.");
            if (snapshot.Name == "gaban-materials" && recommendations[0].Route.GoalUnitId == goal.Id)
                throw new InvalidOperationException("Gaban conversion needs a named replacement first.");
            if (snapshot.Name == "gaban-resume" && plan.FirstOrDefault()?.TargetUnitId != goal.Id)
                throw new InvalidOperationException("Gaban must resume after replacement support arrives.");

            var window = new OverlayWindow { Topmost = false, ShowActivated = false, Opacity = 0 };
            try
            {
                window.Render(goal.Name, recommendations,
                    new InventoryStatsCalculator(catalog).Calculate(snapshot.Inventory),
                    [], [], false, plan, $"검증용 고정 패 · 신 {snapshot.Round}라",
                    inventory: snapshot.Inventory, phaseHint: result.Urgency.Reason);
                window.Show();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                window.Left = SystemParameters.VirtualScreenLeft - 1024;
                window.Opacity = 1;
                ApplyScale(window, 1);
                var scroll = (ScrollViewer)window.FindName("BoardScrollViewer");
                foreach (var bottom in new[] { false, true })
                {
                    if (bottom) scroll.ScrollToEnd();
                    else scroll.ScrollToHome();
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                    var path = Path.Combine(output, $"gaban-{snapshot.Name}-{(bottom ? "bottom" : "top")}.png");
                    CapturePng(window, path, 1);
                    ValidateCapture(path, 540, 740);
                }
                var title = Descendants(window).OfType<TextBlock>().Single(element =>
                    AutomationProperties.GetAutomationId(element) == "current-craft-title");
                if (plan.Count > 0 && title.Text != plan[0].TargetName)
                    throw new InvalidOperationException($"Displayed action mismatch: {title.Text} / {plan[0].TargetName}");
                rows.Add(new
                {
                    snapshot.Name, snapshot.Round,
                    Inventory = snapshot.Inventory.Select(item => new { item.UnitId, item.Count }),
                    FinalBuildOrder = finalBuild.Select(item => item.Route.GoalUnitId),
                    ActionOrder = recommendations.Select(item => item.Route.GoalUnitId),
                    Steps = plan.Select(item => item.TargetUnitId),
                    DisplayedAction = title.Text,
                    Warnings = recommendations.SelectMany(item => item.Warnings)
                });
                Console.WriteLine($"GABAN_ACTION {snapshot.Name} final={finalBuild[0].Route.GoalUnitId} " +
                                  $"next={recommendations[0].Route.GoalUnitId} displayed={title.Text}");
            }
            finally
            {
                window.Stats.CloseForApplication();
                window.CloseForApplication();
            }
        }
        WriteEvidenceText(Path.Combine(output, "gaban-actions.json"), JsonSerializer.Serialize(new
        {
            Kind = "deterministic-inventory-and-WPF-check",
            Limitation = "Not a Warcraft combat replay or a measured clear rate. Snapshots are controlled fixtures.",
            Rows = rows
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"GABAN_ACTION_QA PASS snapshots={snapshots.Count} captures={snapshots.Count * 2}");
    }
}
