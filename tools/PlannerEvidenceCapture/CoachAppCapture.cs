using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void CaptureCoachApp(string output)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var settings = new AppSettings
        {
            BeginnerCoachEnabled = true, ManualGoalUnitId = "rawcode:F40h",
            ExpertAutoStartGoal = false, ExpertAutoNavigation = false,
            AutoStartGoal = true, AutoRecommendNavigation = true, TelemetryEnabled = false
        };
        var main = new MainWindow(FixtureJournalContext(settings));
        settings = FixtureSettings(main);
        var recognizer = new CoachFrameRecognizer();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        var journal = FixtureJournal(main);
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        var rows = new List<object>();
        try
        {
            main.Show();
            main.Left = SystemParameters.VirtualScreenLeft - main.Width - 20;
            var opening = new[] { Entry("rawcode:S20h"), Entry("rawcode:330h"), Entry("rawcode:H00h") };
            Observe("opening", 4, 3, opening);
            var committed = Observe("auto-goal", 20, 9, opening);
            var goalId = committed.Frame.GoalId ?? throw new InvalidOperationException("Automatic goal was not selected.");
            var navigation = Observe("navigation", 21, 9, opening);
            if (navigation.Decision.Kind != CoachActionKind.Navigation)
                throw new InvalidOperationException("The navigation choice window must interrupt normal crafting.");
            WaitCoach(main, () => ((Button)view.FindName("ConfirmButton")).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent)), (_, frame) => frame.ConfirmedNavigation is not null);
            Capture("navigation-confirmed");
            Observe("round24", 24, 9, opening);
            var late = WaitCoach(main, () => ((Button)main.FindName("ClearNavigationButton")).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent)), (_, frame) => frame.ConfirmedNavigation is null);
            if (!late.Decision.RequiresNavigationChoice || late.Decision.NavigationOptionId is not null)
                throw new InvalidOperationException("Late navigation recovery must not preselect the forced default.");
            Capture("late-navigation");
            var choice = (ComboBox)view.FindName("NavigationChoice");
            var confirm = (Button)view.FindName("ConfirmButton");
            if (confirm.IsEnabled) throw new InvalidOperationException("Late confirmation must require a choice.");
            choice.SelectedItem = ((IEnumerable<NavigationOption>)choice.ItemsSource)
                .Single(option => option.Id == "PathOfKings.BountyHunter");
            main.Activate();
            Keyboard.Focus(confirm);
            main.UpdateLayout();
            if (!confirm.IsKeyboardFocused)
                throw new InvalidOperationException("Keyboard focus did not reach the confirmation button.");
            var border = (Border)confirm.Template.FindName("Body", confirm);
            if (border.BorderBrush != OverlayTheme.FocusBrush)
                throw new InvalidOperationException("Focused confirmation must have a visible teal outline.");
            Capture("keyboard-focus");
            WaitCoach(main, () => confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
                (_, frame) => frame.ConfirmedNavigation == "PathOfKings.BountyHunter");
            overlay.SetClickThrough(true);
            Capture("click-through");
            overlay.SetClickThrough(false);
            Observe("recognition-lost", 24, 9, opening, RecognitionState.TransientReadError);
            var story = Observe("story-deadline", 34, 12, opening);
            if (story.Decision.Kind != CoachActionKind.Story)
                throw new InvalidOperationException("Story deadline did not interrupt unrelated crafting.");
            var catalog = new DataCatalog();
            catalog.Load(loadCarryPolicy: false);
            var completed = Observe("goal-acquired", 55, 13,
                [Entry(goalId), Entry("rawcode:540h"), Entry("rawcode:S20h")]);
            if (completed.Frame.GoalId != goalId)
                throw new InvalidOperationException("The automatic goal changed on a later hand.");
            WaitCoach(main, () => ((Button)view.FindName("PauseButton")).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent)), (_, frame) => frame.Paused);
            Capture("paused");
            WaitCoach(main, () => ((Button)view.FindName("PauseButton")).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent)), (_, frame) => !frame.Paused);
            var mode = (ComboBox)main.FindName("PlayModeCombo");
            WaitCoach(main, () => mode.SelectedItem =
                PlayModes.Options.Single(option => option.Mode == PlayMode.Manual),
                (_, frame) => frame.Mode == PlayMode.Manual && !settings.BeginnerCoachEnabled);
            if (((ComboBox)main.FindName("GoalCombo")).SelectedItem is not UnitDefinition manual ||
                manual.Id != "rawcode:F40h" || !view.IsVisible ||
                settings.AutoStartGoal || !settings.AutoRecommendNavigation)
                throw new InvalidOperationException(
                    "Manual mode did not keep the Gaban goal, visible coach, automatic navigation, and AutoStartGoal off.");
            Capture("expert");
            var restored = WaitCoach(main, () => mode.SelectedItem =
                PlayModes.Options.Single(option => option.Mode == PlayMode.Beginner),
                (_, frame) => frame.Mode == PlayMode.Beginner && settings.BeginnerCoachEnabled);
            if (restored.Frame.GoalId != goalId || !view.IsVisible || !settings.AutoRecommendNavigation)
                throw new InvalidOperationException("Automatic mode discarded an already-owned top.");
            Capture("beginner-restored");
            Observe("wipe-first", 55, 13, []);
            var ended = Observe("ended", 55, 13, []);
            if (ended.Decision.Kind != CoachActionKind.Finished)
                throw new InvalidOperationException("Confirmed defeat must end execution guidance.");
            journal.FlushAsync().GetAwaiter().GetResult();
            var review = CoachReview.Read(journal);
            if (review.Outcome != "fail" || review.Incomplete)
                throw new InvalidOperationException("Local review did not record the confirmed outcome.");
            var reviewButton = Descendants(view).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "coach-review");
            reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var reviewWindow = Application.Current.Windows.Cast<Window>().Single(window =>
                AutomationProperties.GetAutomationId(window) == "coach-review-window");
            reviewWindow.Left = SystemParameters.VirtualScreenLeft - reviewWindow.Width - 20;
            reviewWindow.UpdateLayout();
            var reviewText = Descendants(reviewWindow).OfType<TextBlock>().Single();
            if (reviewText.Text != review.Describe())
                throw new InvalidOperationException("The review dialog differs from the local record.");
            SaveCoachWindow(reviewWindow, Path.Combine(output, "review.png"));
            reviewWindow.Close();
            WriteEvidenceText(Path.Combine(output, "coach-app.json"), JsonSerializer.Serialize(new
            {
                Kind = "real-WPF-and-production-scan-with-controlled-observations",
                Limitation = "Controlled inputs, not a Warcraft combat replay or novice clear-rate measurement.",
                AutomaticGoal = goalId, Rows = rows, Review = review
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"COACH_APP PASS goal={goalId} stages={rows.Count} outcome={review.Outcome}");
        }
        finally
        {
            main.Close();
            overlay.Stats.CloseForApplication();
            overlay.CloseForApplication();
            journal.FlushAsync().GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        (CoachDecision Decision, CoachFrame Frame) Observe(string name, int round, int story,
            InventoryEntry[] entries, RecognitionState state = RecognitionState.Ready)
        {
            recognizer.Next = new RecognitionResult
            {
                State = state, Entries = entries.ToList(), Status = "제어된 검증 관측",
                MapSignals = new MapSignals(Math.Min(14, story + 1), null, story,
                    ImmutableDictionary<string, int>.Empty),
                Diagnostics = new RecognitionDiagnostics
                {
                    ObservedObjects = entries.Sum(entry => entry.Count), MappedObjects = entries.Length,
                    ForeignObjects = 8, MapState = new MapStateSample(round, 0, "악몽")
                }
            };
            var observed = WaitCoach(main,
                () => _ = ScanFixtureAsync(main, recognizer.Next),
                (_, frame) => frame.IsCurrent == (state == RecognitionState.Ready));
            rows.Add(new { Name = name, observed.Decision, observed.Frame.Round,
                observed.Frame.GoalId, observed.Frame.Outcome, observed.Frame.Inventory });
            Capture(name);
            var planning = (AdaptivePlanningCompositionRoot)typeof(MainWindow)
                .GetField("_adaptivePlanning", flags)!.GetValue(main)!;
            Console.WriteLine($"COACH_APP_STAGE {name} round={observed.Frame.Round} kind={observed.Decision.Kind} " +
                              $"phase={planning.State.Phase} story={observed.Frame.Story?.Action} " +
                              $"stunTarget={observed.Frame.Recommendations.FirstOrDefault()?.CombatReadiness?.RequiredStun}");
            return observed;
        }

        void Capture(string name)
        {
            main.UpdateLayout();
            SaveCoachWindow(main, Path.Combine(output, $"main-{name}.png"));
            overlay.Show();
            overlay.Left = SystemParameters.VirtualScreenLeft - overlay.Width - 20;
            overlay.UpdateLayout();
            SaveCoachWindow(overlay, Path.Combine(output, $"overlay-{name}.png"));
        }

        static InventoryEntry Entry(string id) => new() { UnitId = id, Count = 1 };
    }

    private static (CoachDecision Decision, CoachFrame Frame) WaitCoach(MainWindow main,
        Action trigger, Func<CoachDecision, CoachFrame, bool> predicate)
    {
        (CoachDecision, CoachFrame)? result = null;
        var loop = new DispatcherFrame();
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timeout.Tick += (_, _) => loop.Continue = false;
        void Receive(CoachDecision decision, CoachFrame frame)
        {
            if (!predicate(decision, frame)) return;
            result = (decision, frame);
            loop.Continue = false;
        }
        main.CoachRendered += Receive;
        try
        {
            trigger();
            if (result is null)
            {
                timeout.Start();
                Dispatcher.PushFrame(loop);
            }
            return result ?? throw new TimeoutException("No matching coach render event.");
        }
        finally { timeout.Stop(); main.CoachRendered -= Receive; }
    }

    private sealed class CoachFrameRecognizer : IInventoryRecognizer
    {
        public RecognitionResult Next { get; set; } = new();
        public Task<RecognitionResult> RecognizeAsync(AppSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(Next);
    }
}
