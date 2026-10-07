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
    private static void CaptureCoachFinishedRewards(string output)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var main = new MainWindow(FixtureJournalContext(new AppSettings
            { Mode = PlayMode.Beginner, TelemetryEnabled = false, ClearDataAutoRefresh = false }));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog", flags)!.GetValue(main)!;
        var journal = FixtureJournal(main);
        var failures = new List<string>();
        var rows = new List<object>();
        var revision = 0;
        var reviews = 0;
        try
        {
            main.Show();
            main.Left = SystemParameters.VirtualScreenLeft - main.Width - 20;
            WaitCoach(main, () => typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(main, [null]),
                (_, frame) => frame.Round == 0);
            overlay.Show();
            foreach (var mode in new[] { PlayMode.Beginner, PlayMode.Normal })
            {
                if (mode == PlayMode.Normal)
                    WaitCoach(main, () => ((ComboBox)main.FindName("PlayModeCombo")).SelectedItem =
                        PlayModes.Options.Single(option => option.Mode == mode), (_, frame) => frame.Mode == mode);
                var session = new BeginnerCoachSession(catalog);
                var basis = new CoachFrame
                {
                    Mode = mode, MatchGeneration = mode == PlayMode.Beginner ? 10 : 20,
                    Revision = 1, Round = 21, CompletedStoryStage = 13, Difficulty = "악몽",
                    IsCurrent = true, GoalId = "rawcode:F40h", SuggestedNavigation = "PathOfKings.BountyHunter",
                    Inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:540h", 1).Add("luffy_common", 1)
                };
                foreach (var (name, frame) in new[]
                {
                    ("active", basis),
                    ("clear", basis with { Revision = 2, Outcome = "clear", Round = 65 }),
                    ("after-clear", basis with { MatchGeneration = basis.MatchGeneration + 1 }),
                    ("fail", basis with { MatchGeneration = basis.MatchGeneration + 1, Revision = 2, Outcome = "fail" }),
                    ("after-fail", basis with { MatchGeneration = basis.MatchGeneration + 2 })
                })
                {
                    var decision = session.Update(frame);
                    var finished = frame.Outcome is "clear" or "fail";
                    Check((decision.Kind == CoachActionKind.Finished) == finished, name + ": decision state");
                    Check(decision.OperationGuide.Length > 0, name + ": fixture must contain real combat guidance");
                    Record(mode + "-" + name, frame, decision, checkFinished: true);
                }
                foreach (var (name, action, wisp) in new[]
                {
                    ("special", StorySequenceAction.SpendStoryWisps, "e016"),
                    ("uncommon", StorySequenceAction.SpendStoryWisps, "e017"),
                    ("rare", StorySequenceAction.SpendRareWisps, "e019")
                })
                foreach (var quantity in new[] { 1, 2, 12 })
                {
                    var frame = basis with
                    {
                        MatchGeneration = basis.MatchGeneration + 3, Revision = ++revision, Round = 10,
                        Story = new StoryRewardSequenceDecision(RecommendationSequenceStage.StoryReward,
                            action, "", "", "", "", "", null, null, 0, false),
                        RewardWisps = ImmutableDictionary<string, int>.Empty.Add(wisp, quantity)
                    };
                    var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
                    Check(decision.Kind == CoachActionKind.Reward && decision.RewardWispId == wisp, name + ": reward type");
                    Record($"{mode}-{name}-{quantity}", frame, decision, checkFinished: false);
                }
            }
            WriteEvidenceText(Path.Combine(output, "matrix.json"), JsonSerializer.Serialize(new
            {
                Kind = "real-main-overlay-WPF-session-transitions-and-reward-quantity-matrix",
                Limitation = "Controlled coach frames; real windows, mode switch and recap, not live Warcraft or physical monitor DPI.",
                Scales = new[] { 100, 125, 150 }, ReviewsOpened = reviews, Failures = failures, Rows = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"COACH_FINISHED_REWARDS rows={rows.Count} reviews={reviews} failures={failures.Count}");
            foreach (var failure in failures) Console.WriteLine("FAIL " + failure);
            if (failures.Count > 0) throw new InvalidOperationException("Coach finished/reward WPF contract failed.");
            Console.WriteLine("COACH_FINISHED_REWARDS PASS");
        }
        finally
        {
            foreach (var owned in main.OwnedWindows.Cast<Window>().ToArray()) owned.Close();
            main.Close();
            overlay.Stats.CloseForApplication();
            overlay.CloseForApplication();
            journal.FlushAsync().GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(previous);
            Console.WriteLine($"CLEANUP remaining-task-windows={Application.Current.Windows.Count}");
        }

        void Check(bool condition, string message)
        {
            if (!condition) failures.Add(message);
        }

        void Record(string name, CoachFrame frame, CoachDecision decision, bool checkFinished)
        {
            if (!journal.RecordAsync(frame, decision).GetAwaiter().GetResult() && journal.LastError is not null)
                throw new IOException(journal.LastError);
            view.Render(decision, frame, null);
            overlay.RenderCoach(true, decision, frame, null);
            foreach (var (window, surface, prefix) in new[]
            {
                ((Window)main, view, "main"), ((Window)overlay, overlay.BeginnerView, "overlay")
            })
            {
                var width = window.Width;
                var height = window.Height;
                var root = (FrameworkElement)window.Content;
                var original = root.LayoutTransform;
                var scroll = (ScrollViewer)surface.FindName("CoachScroll");
                var operation = (TextBlock)surface.FindName("OperationText");
                var timing = (TextBlock)surface.FindName("NavigationTiming");
                var reason = (TextBlock)surface.FindName("ReasonText");
                DependencyObject node = operation;
                while (node is not Expander)
                    node = LogicalTreeHelper.GetParent(node) ??
                        VisualTreeHelper.GetParent(node) ??
                        throw new InvalidOperationException("Operation expander parent was not found.");
                var expander = (Expander)node;
                expander.IsExpanded = true;
                try
                {
                    foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                    {
                        var key = $"{prefix}-{name}-{scale * 100:0}";
                        var transform = new TransformGroup();
                        transform.Children.Add(original);
                        transform.Children.Add(new ScaleTransform(scale, scale));
                        root.LayoutTransform = transform;
                        window.Width = width * scale;
                        window.Height = height * scale;
                        window.Left = SystemParameters.VirtualScreenLeft - window.Width - 20;
                        scroll.ScrollToHome();
                        window.UpdateLayout();
                        Check(scroll.ExtentWidth <= scroll.ViewportWidth + 1, key + ": horizontal overflow");
                        Check(reason.Text == decision.Reason && AutomationProperties.GetName(reason) == decision.Reason,
                            key + ": displayed/accessible reason differs from decision");
                        var bounds = reason.TransformToAncestor(window).TransformBounds(new Rect(reason.RenderSize));
                        SaveCoachWindow(window, Path.Combine(output, key + "-top.png"));
                        scroll.ScrollToEnd();
                        window.UpdateLayout();
                        SaveCoachWindow(window, Path.Combine(output, key + "-bottom.png"));
                        if (checkFinished)
                        {
                            var expected = decision.Kind == CoachActionKind.Finished ? Visibility.Collapsed : Visibility.Visible;
                            Check(timing.Visibility == expected, key + ": navigation timing " + timing.Visibility + " != " + expected);
                            Check(operation.Visibility == expected, key + ": combat operations " + operation.Visibility + " != " + expected);
                            var reviewButton = Descendants(surface).OfType<Button>().Single(button =>
                                AutomationProperties.GetAutomationId(button) == "coach-review");
                            var reviewBounds = reviewButton.TransformToAncestor(scroll).TransformBounds(new Rect(reviewButton.RenderSize));
                            Check(reviewButton.IsVisible && reviewButton.IsEnabled && reviewBounds.Top >= 0 &&
                                reviewBounds.Bottom <= scroll.ActualHeight + 1, key + ": recap inaccessible after scroll");
                            reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            var reviewWindow = Application.Current.Windows.Cast<Window>().Single(window =>
                                AutomationProperties.GetAutomationId(window) == "coach-review-window");
                            try
                            {
                                reviewWindow.Left = SystemParameters.VirtualScreenLeft - reviewWindow.Width - 20;
                                reviewWindow.UpdateLayout();
                                Check(Descendants(reviewWindow).OfType<TextBlock>().Single().Text ==
                                    CoachReview.Read(journal).Describe(), key + ": recap differs from local journal");
                                if (prefix == "main" && scale == 1)
                                    SaveCoachWindow(reviewWindow, Path.Combine(output, name + "-recap.png"));
                                reviews++;
                            }
                            finally { reviewWindow.Close(); }
                        }
                        rows.Add(new { Key = key, frame.Mode, frame.MatchGeneration, frame.Outcome,
                            decision.Kind, decision.RewardWispId, frame.RewardWisps, decision.Reason,
                            NavigationVisibility = timing.Visibility.ToString(), OperationVisibility = operation.Visibility.ToString(),
                            ReasonBounds = new { bounds.X, bounds.Y, bounds.Width, bounds.Height },
                            Width = window.ActualWidth, Height = window.ActualHeight });
                    }
                }
                finally
                {
                    root.LayoutTransform = original;
                    window.Width = width;
                    window.Height = height;
                    expander.IsExpanded = false;
                    scroll.ScrollToHome();
                    window.UpdateLayout();
                }
            }
        }
    }
}
