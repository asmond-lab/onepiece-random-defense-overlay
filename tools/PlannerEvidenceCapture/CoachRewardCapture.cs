using System.Collections.Immutable;
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
    private static void CaptureCoachRewards(string output)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var main = new MainWindow(FixtureContext(new AppSettings
            { Mode = PlayMode.Beginner, TelemetryEnabled = false, ClearDataAutoRefresh = false }));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog", flags)!.GetValue(main)!;
        var planner = new BeginnerCoachPlanner(catalog);
        var goal = catalog.Unit("rawcode:F40h");
        var rows = new List<object>();
        try
        {
            main.Show();
            main.Left = SystemParameters.VirtualScreenLeft - main.Width - 20;
            WaitCoach(main, () => typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(main, [null]), (_, frame) => frame.Round == 0);
            foreach (var mode in new[] { PlayMode.Beginner, PlayMode.Normal })
            {
                if (mode == PlayMode.Normal)
                    WaitCoach(main, () => ((ComboBox)main.FindName("PlayModeCombo")).SelectedItem =
                        PlayModes.Options.Single(option => option.Mode == mode), (_, frame) => frame.Mode == mode);
                foreach (var (name, action, wisp, current, expected) in new[]
                {
                    ("wait", StorySequenceAction.WaitForStoryReward, "", true, CoachActionKind.Story),
                    ("push", StorySequenceAction.PushStoryForRareReward, "", true, CoachActionKind.Story),
                    ("special", StorySequenceAction.SpendStoryWisps, "e016", true, CoachActionKind.Reward),
                    ("uncommon", StorySequenceAction.SpendStoryWisps, "e017", true, CoachActionKind.Reward),
                    ("rare", StorySequenceAction.SpendRareWisps, "e019", true, CoachActionKind.Reward),
                    ("stale", StorySequenceAction.SpendRareWisps, "e019", false, CoachActionKind.Recognition),
                    ("resume", StorySequenceAction.CraftLegendNow, "", true, CoachActionKind.Craft)
                })
                {
                    var frame = new CoachFrame
                    {
                        Mode = mode, MatchGeneration = 1, Revision = rows.Count + 1, Round = 10,
                        CompletedStoryStage = 3, Difficulty = "신", IsCurrent = current, GoalId = goal.Id,
                        Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy_common", 1),
                        Story = new StoryRewardSequenceDecision(RecommendationSequenceStage.StoryReward,
                            action, "스토리 4단계 진행", "특별위습 2개 · 안흔위습 1개", "", "", "", null, null, 0, false),
                        RewardWisps = wisp.Length == 0 ? ImmutableDictionary<string, int>.Empty :
                            ImmutableDictionary<string, int>.Empty.Add(wisp, 2),
                        Wisps = [new EmergencySummonAdvice("navigation-only", "항법 후보", 9, "")],
                        Recommendations = [new Recommendation
                        {
                            Route = new RouteDefinition { Id = "craft:luffy", GoalUnitId = "luffy_common", Name = "루피" },
                            CurrentCraft = new CurrentCraftAssessment(true, 0, new GoalStrategyProfile(0, 0))
                        }],
                        CraftSteps = [new AutoCombineStep("luffy_common", "루피", "luffy_common", "루피", "", "Z", [])]
                    };
                    var decision = planner.Decide(frame);
                    if (decision.Kind != expected || (expected == CoachActionKind.Reward && decision.RewardWispId != wisp))
                        throw new InvalidOperationException("Reward fixture selected the wrong action/type.");
                    view.Render(decision, frame, null);
                    overlay.RenderCoach(true, decision, frame, null);
                    foreach (var surface in new[] { view, overlay.BeginnerView })
                    {
                        foreach (var (field, value) in new[] { ("ReasonText", decision.Reason), ("ConfirmationText", decision.Confirmation) })
                        {
                            var text = (TextBlock)surface.FindName(field);
                            if (text.Text != value || AutomationProperties.GetName(text) != value ||
                                decision.CraftDeferredForReward && text.Visibility != Visibility.Visible)
                                throw new InvalidOperationException("Essential hold/resumption guidance is hidden or differs from the decision.");
                        }
                        if (((TextBlock)surface.FindName("CompletedText")).Visibility != Visibility.Collapsed)
                            throw new InvalidOperationException("Future resumption is being displayed as observed completion.");
                    }
                    var key = mode + "-" + name;
                    CaptureDifficultyWindows(main, overlay, (decision, frame), output, key);
                    rows.Add(new { Name = key, frame.Mode, decision });
                }
            }
            WriteEvidenceText(Path.Combine(output, "coach-rewards.json"), JsonSerializer.Serialize(new
            {
                Kind = "real-main-and-overlay-shared-WPF-views",
                Limitation = "Controlled coach frames, real mode switch and views; not live Warcraft recognition or hardware DPI.",
                Scales = new[] { 100, 125, 150 }, Rows = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"COACH_REWARDS PASS states={rows.Count} surfaces=2 scales=3 screenshots={rows.Count * 12}");
        }
        finally
        {
            main.Close(); overlay.Stats.CloseForApplication(); overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
