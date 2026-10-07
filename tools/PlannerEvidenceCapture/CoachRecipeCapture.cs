using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void CaptureCoachRecipes(string output)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var main = new MainWindow(FixtureContext(new AppSettings
            { Mode = PlayMode.Normal, TelemetryEnabled = false, ClearDataAutoRefresh = false }));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog", flags)!.GetValue(main)!;
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        var goal = catalog.Unit("rawcode:130h");
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys);
        var planner = new BeginnerCoachPlanner(catalog);
        var lower = goal.Recipe.Keys.Select(catalog.Unit).Where(unit => unit.Tier != "자원")
            .SelectMany(unit => unit.Recipe).GroupBy(pair => pair.Key)
            .Select(group => new InventoryEntry { UnitId = group.Key, Count = group.Sum(pair => pair.Value) }).ToArray();
        var direct = goal.Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원")
            .Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var hold = Frame(lower);
        var ready = Frame(direct);
        ready = ready with { CraftSteps = new AutoCombinePlanner(catalog, hotkeys).Plan(ready.Recommendations, direct) };
        var rows = new List<object>();
        var captures = new List<string>();
        try
        {
            main.Show();
            main.Left = SystemParameters.VirtualScreenLeft - main.Width - 20;
            WaitCoach(main, () => typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(main, [null]), (_, frame) => frame.Round == 0);
            foreach (var mode in new[] { PlayMode.Normal, PlayMode.Beginner })
            {
                if (mode == PlayMode.Beginner)
                    WaitCoach(main, () => ((ComboBox)main.FindName("PlayModeCombo")).SelectedItem =
                        PlayModes.Options.Single(option => option.Mode == mode), (_, frame) => frame.Mode == mode);
                foreach (var (name, frame, expected) in new[]
                {
                    ("four-step-hold", hold, CoachActionKind.Waiting),
                    ("gather", Frame(lower.Take(lower.Length - 1).ToArray()), CoachActionKind.Gather),
                    ("reward-wait", hold with { Story = Story(StorySequenceAction.WaitForStoryReward) }, CoachActionKind.Story),
                    ("reward-use", ready with { Story = Story(StorySequenceAction.SpendStoryWisps),
                        RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e016", 1) }, CoachActionKind.Reward),
                    ("resource-check", ready with { Signals = ImmutableDictionary<string, long?>.Empty }, CoachActionKind.Economy),
                    ("craft", ready, CoachActionKind.Craft),
                    ("stale", hold with { IsCurrent = false }, CoachActionKind.Recognition),
                    ("paused", hold with { Paused = true }, CoachActionKind.Waiting)
                })
                {
                    var observation = frame with { Mode = mode, Revision = rows.Count + 1 };
                    var decision = planner.Decide(observation);
                    if (decision.Kind != expected) throw new InvalidOperationException($"{name}: {decision.Kind}, expected {expected}");
                    if (name == "four-step-hold" && decision.RecipePreview.Count != 4)
                        throw new InvalidOperationException("The four-stage catalog fixture lost recipe steps.");
                    var unit = decision.TargetUnitId is { } id ? catalog.Unit(id) : null;
                    view.Render(decision, observation, unit);
                    overlay.RenderCoach(true, decision, observation, unit);
                    overlay.Show();
                    foreach (var (window, surface, prefix) in new[]
                    {
                        ((Window)main, view, "main"), ((Window)overlay, overlay.BeginnerView, "overlay")
                    })
                    {
                        var preview = (TextBlock)surface.FindName("RecipePreviewText");
                        var recipe = (TextBlock)surface.FindName("CraftRecipeText");
                        var controls = (TextBlock)surface.FindName("ControlsText");
                        if ((preview.Visibility == Visibility.Visible) != (decision.RecipePreview.Count > 0) ||
                            (recipe.Visibility == Visibility.Visible) != (decision.CraftRecipe is not null) ||
                            controls.Text != decision.Controls)
                            throw new InvalidOperationException("Recipe/action visibility or controls do not match the decision.");
                        foreach (var block in new[] { preview, recipe, controls })
                            if (AutomationProperties.GetName(block) != block.Text || AutomationProperties.GetItemStatus(block) != block.Text)
                                throw new InvalidOperationException("Accessible recipe content differs from visible content.");
                        if (decision.CraftDeferredForReward || name == "four-step-hold")
                            if (((TextBlock)surface.FindName("ConfirmationText")).Visibility != Visibility.Visible)
                                throw new InvalidOperationException("Compact mode hid the hold/resumption condition.");
                        Capture(window, surface, $"{prefix}-{mode}-{name}");
                    }
                    rows.Add(new { Name = name, Mode = mode, decision });
                }
            }
            WriteEvidenceText(Path.Combine(output, "coach-recipes.json"), JsonSerializer.Serialize(new
            {
                Surface = "real MainWindow and OverlayWindow shared WPF views; no runtime startup",
                Limitation = "Controlled catalog/engine frames, not the exact live inventory; layout scaling, not hardware DPI.",
                Rows = rows, Captures = captures
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"COACH_RECIPES PASS states={rows.Count} surfaces=2 scales=3 screenshots={captures.Count}");
        }
        finally
        {
            main.Close(); overlay.Stats.CloseForApplication(); overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        CoachFrame Frame(InventoryEntry[] inventory) => new()
        {
            Mode = PlayMode.Normal, MatchGeneration = 1, Revision = 1, Round = 9,
            CompletedStoryStage = 7, Difficulty = "신", IsCurrent = true,
            Inventory = inventory.ToImmutableDictionary(item => item.UnitId, item => item.Count),
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 3),
            Recommendations = [engine.RecommendNearestCrafts(goal.Id, inventory, difficulty: "신", round: 9, completedStoryStage: 7)
                .Single(item => item.Route.GoalUnitId == goal.Id)]
        };

        static StoryRewardSequenceDecision Story(StorySequenceAction action) => new(
            RecommendationSequenceStage.StoryReward, action, "스토리 8단계 진행", "보상 결과 확인", "", "", "", null, null, 0, false);

        void Capture(Window window, BeginnerCoachView surface, string name)
        {
            var width = window.Width;
            var height = window.Height;
            var root = (FrameworkElement)window.Content;
            var original = root.LayoutTransform;
            var scroll = (ScrollViewer)surface.FindName("CoachScroll");
            try
            {
                foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                {
                    var transform = new TransformGroup();
                    transform.Children.Add(original);
                    transform.Children.Add(new ScaleTransform(scale, scale));
                    root.LayoutTransform = transform;
                    window.Width = width * scale; window.Height = height * scale;
                    window.Left = SystemParameters.VirtualScreenLeft - window.Width - 20;
                    window.UpdateLayout();
                    if (scroll.ExtentWidth > scroll.ViewportWidth + 1 || scroll.ViewportHeight <= 0)
                        throw new InvalidOperationException("Recipe surface overflow or missing scroll viewport.");
                    // Capture every viewport, including long recipe middles, without timing waits.
                    var pages = (int)Math.Ceiling(scroll.ExtentHeight / scroll.ViewportHeight);
                    for (var page = 0; page < pages; page++)
                    {
                        scroll.ScrollToVerticalOffset(page * scroll.ViewportHeight);
                        window.UpdateLayout();
                        var path = Path.Combine(output, $"{name}-{scale * 100:0}-{page}.png");
                        SaveCoachWindow(window, path);
                        captures.Add(path);
                    }
                }
            }
            finally
            {
                root.LayoutTransform = original;
                window.Width = width; window.Height = height;
                scroll.ScrollToHome(); window.UpdateLayout();
            }
        }
    }
}
