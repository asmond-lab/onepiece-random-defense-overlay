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
    private static void CaptureCoachDifficulties(string output)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var settings = new AppSettings { Mode = PlayMode.Beginner, TelemetryEnabled = false, ClearDataAutoRefresh = false };
        var main = new MainWindow(FixtureContext(settings));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        var stats = (ClearBuildStats)typeof(MainWindow).GetField("_clearStats", flags)!.GetValue(main)!;
        var recognizer = new CoachFrameRecognizer();

        var rows = new List<object>();
        try
        {
            main.Show();
            main.Left = SystemParameters.VirtualScreenLeft - main.Width - 20;
            var start = WaitCoach(main, () => Refresh(), (_, frame) => frame.Round == 0);
            Record("start-unknown", start);
            // Only difficulty changes: the production scan signature must schedule a fresh render.
            foreach (var (name, difficulty) in new[] { ("unknown", "unknown"), ("easy", "쉬움"),
                         ("normal", "보통"), ("hard", "어려움"), ("hell", "지옥"),
                         ("god", "신"), ("nightmare", "악몽"), ("god-correction", "신") })
            {
                var result = Observe(difficulty);
                var policy = (BeginnerGoalPolicy)typeof(MainWindow).GetField("_beginnerGoals", flags)!.GetValue(main)!;
                var expectedSamples = difficulty == "unknown" ? 0 : stats.ForDifficulty(difficulty).TotalGodPlusSamples;
                if (policy.Difficulty != difficulty || policy.Statistics.TotalGodPlusSamples != expectedSamples ||
                    result.Frame.ClearRound != (difficulty == "unknown" ? null : (int?)MatchOutcomeDetector.ClearRound(difficulty)))
                    throw new InvalidOperationException("Observed difficulty was not propagated to policy/statistics/milestone.");
                if (expectedSamples == 0 && (result.Frame.GoalId is not null || policy.EligibleGoals.Count != 0))
                    throw new InvalidOperationException("Missing exact-difficulty evidence fell back to another difficulty.");
                if (result.Frame.GoalId is { } goal && !policy.EligibleGoals.Any(unit => unit.Id == goal))
                    throw new InvalidOperationException("Committed goal is not eligible for the observed difficulty.");
                Record(name, result);
            }
            // A missing read in the same match retains the last confirmed difficulty.
            var retained = Observe("unknown", expectedDifficulty: "신", round: 21);
            var navigation = retained.Decision.NavigationOptionId;
            if (navigation is null) throw new InvalidOperationException("Navigation confirmation fixture missing.");
            WaitCoach(main, () => ((Button)view.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
                (_, frame) => frame.ConfirmedNavigation == navigation);
            var normalMode = WaitCoach(main, () => ((ComboBox)main.FindName("PlayModeCombo")).SelectedItem =
                PlayModes.Options.Single(option => option.Mode == PlayMode.Normal), (_, frame) => frame.Mode == PlayMode.Normal);
            Record("normal-mode-god", normalMode);
            WaitCoach(main, () => ((ComboBox)main.FindName("PlayModeCombo")).SelectedItem =
                PlayModes.Options.Single(option => option.Mode == PlayMode.Beginner), (_, frame) => frame.Mode == PlayMode.Beginner);
            foreach (var difficulty in new[] { "신", "악몽" })
            {
                var story = Observe(difficulty, round: 34, story: 12);
                if ((story.Decision.MilestoneRound == 35) != (difficulty == "악몽"))
                    throw new InvalidOperationException("Nightmare story milestone leaked across difficulties.");
                Record(difficulty == "신" ? "god-story" : "nightmare-story", story);
            }
            typeof(MainWindow).GetMethod("ResetMatchSession", flags)!.Invoke(main, null);
            var reset = Observe("unknown", round: 34);
            if (reset.Frame.GoalId is not null || reset.Frame.HasKnownDifficulty)
                throw new InvalidOperationException("New match retained a previous difficulty/goal.");
            Record("reset-unknown", reset);
            WriteEvidenceText(Path.Combine(output, "coach-difficulties.json"), JsonSerializer.Serialize(new
            {
                Kind = "production-scan-and-WPF-with-controlled-observations",
                Limitation = "Real WPF, bundled/local samples, synthetic recognition; not a live Warcraft match or hardware DPI test.",
                Rows = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"COACH_DIFFICULTIES PASS scenarios={rows.Count}");
        }
        finally
        {
            main.Close();
            overlay.Stats.CloseForApplication();
            overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        void Refresh() => typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(main, [null]);

        (CoachDecision Decision, CoachFrame Frame) Observe(string difficulty, string? expectedDifficulty = null,
            int round = 20, int story = 13)
        {
            recognizer.Next = new RecognitionResult
            {
                State = RecognitionState.Ready,
                Entries = [new InventoryEntry { UnitId = "rawcode:S20h", Count = 1 },
                    new InventoryEntry { UnitId = "rawcode:330h", Count = 1 },
                    new InventoryEntry { UnitId = "luffy_common", Count = 1 }],
                MapSignals = new MapSignals(story + 1, null, story, ImmutableDictionary<string, int>.Empty),
                Diagnostics = new RecognitionDiagnostics
                {
                    ObservedObjects = 3, MappedObjects = 3, ForeignObjects = 8,
                    MapState = new MapStateSample(round, 0, difficulty)
                }
            };
            return WaitCoach(main, () => _ = ScanFixtureAsync(main, recognizer.Next),
                (_, frame) => frame.Difficulty == (expectedDifficulty ?? difficulty) && frame.Round == round);
        }

        void Record(string name, (CoachDecision Decision, CoachFrame Frame) result)
        {
            rows.Add(new { Name = name, result.Frame.Difficulty, result.Frame.GoalId, result.Frame.ClearRound,
                result.Decision, result.Frame.Mode });
            CaptureDifficultyWindows(main, overlay, result, output, name);
            Console.WriteLine($"COACH_DIFFICULTY {name} difficulty={result.Frame.Difficulty} goal={result.Frame.GoalId} milestone={result.Decision.MilestoneRound}");
        }
    }

    private static void CaptureDifficultyWindows(MainWindow main, OverlayWindow overlay,
        (CoachDecision Decision, CoachFrame Frame) result, string output, string name)
    {
        overlay.Show();
        foreach (var (window, view, prefix) in new[]
                 { ((Window)main, (BeginnerCoachView)main.FindName("MainCoachView"), "main"),
                     ((Window)overlay, overlay.BeginnerView, "overlay") })
        {
            var width = window.Width;
            var height = window.Height;
            var root = (FrameworkElement)window.Content;
            var original = root.LayoutTransform;
            var scroll = (ScrollViewer)view.FindName("CoachScroll");
            try
            {
                foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                {
                    var transform = new TransformGroup();
                    transform.Children.Add(original);
                    transform.Children.Add(new ScaleTransform(scale, scale));
                    root.LayoutTransform = transform;
                    window.Width = width * scale;
                    window.Height = height * scale;
                    window.Left = SystemParameters.VirtualScreenLeft - window.Width - 20;
                    window.UpdateLayout();
                    var stage = (TextBlock)view.FindName("StageText");
                    var milestone = (TextBlock)view.FindName("MilestoneText");
                    var readiness = (TextBlock)view.FindName("ReadinessSummary");
                    if ((!result.Frame.HasKnownDifficulty || result.Frame.GoalId is null) &&
                        readiness.Visibility != Visibility.Collapsed)
                        throw new InvalidOperationException("An unconfirmed difficulty/goal displays default-goal readiness targets.");
                    if (stage.Text != $"{result.Frame.DifficultyLabel} · {result.Frame.Round}라 · {result.Decision.GoalLabel}" ||
                        milestone.Text != result.Decision.Milestone ||
                        AutomationProperties.GetName(stage) != stage.Text ||
                        AutomationProperties.GetName(milestone) != milestone.Text)
                        throw new InvalidOperationException("Displayed/accessible difficulty guidance differs from the decision.");
                    if (scroll.ExtentWidth > scroll.ViewportWidth + 1)
                        throw new InvalidOperationException("Coach content overflows horizontally.");
                    scroll.ScrollToHome();
                    window.UpdateLayout();
                    SaveCoachWindow(window, Path.Combine(output, $"{prefix}-{name}-{scale * 100:0}-top.png"));
                    scroll.ScrollToEnd();
                    window.UpdateLayout();
                    SaveCoachWindow(window, Path.Combine(output, $"{prefix}-{name}-{scale * 100:0}-bottom.png"));
                }
            }
            finally
            {
                root.LayoutTransform = original;
                window.Width = width;
                window.Height = height;
                scroll.ScrollToHome();
                window.UpdateLayout();
            }
        }
    }
}
