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
    private static void CaptureFourModes(string output)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var settings = new AppSettings
        {
            Mode = PlayMode.Beginner, ManualGoalUnitId = "rawcode:F40h",
            SecondaryGoalUnitId = "rawcode:H90H", TelemetryEnabled = false
        };
        var main = new MainWindow(FixtureJournalContext(settings));
        settings = FixtureSettings(main);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog", flags)!.GetValue(main)!;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var recognizer = new CoachFrameRecognizer();

        var modeCombo = (ComboBox)main.FindName("PlayModeCombo");
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        var records = new List<object>();
        var journal = FixtureJournal(main);
        try
        {
            main.Show();
            main.Left = SystemParameters.VirtualScreenLeft - main.Width - 20;
            var opening = new[] { Entry("rawcode:S20h"), Entry("rawcode:330h"), Entry("luffy_common") };
            Observe("opening", 4, 3, opening);
            var reward = Observe("rare-reward", 20, 9, opening, rareReward: 1);
            if (reward.Frame.GoalId is not null || reward.Decision.Kind != CoachActionKind.Reward)
                throw new InvalidOperationException("Pending rare rewards must precede automatic commitment.");
            var automatic = Observe("beginner", 20, 9, opening);
            var autoGoal = automatic.Frame.GoalId ?? throw new InvalidOperationException("Automatic goal missing.");
            var session = typeof(MainWindow).GetField("_coachSession", flags)!.GetValue(main);
            var normal = Switch(PlayMode.Normal, "normal");
            if (normal.Frame.GoalId != autoGoal ||
                !ReferenceEquals(session, typeof(MainWindow).GetField("_coachSession", flags)!.GetValue(main)))
                throw new InvalidOperationException("Explanation mode changed the goal or coach session.");
            var planner = new BeginnerCoachPlanner(catalog);
            if (planner.Decide(normal.Frame with { Mode = PlayMode.Beginner }).Id !=
                planner.Decide(normal.Frame with { Mode = PlayMode.Normal }).Id)
                throw new InvalidOperationException("Automatic modes diverged on the same frame.");
            var navigation = Observe("navigation", 21, 9, opening);
            if (navigation.Decision.Kind != CoachActionKind.Navigation || navigation.Decision.NavigationOptionId is null)
                throw new InvalidOperationException("Automatic navigation recommendation missing.");
            WaitCoach(main, () => ((Button)view.FindName("ConfirmButton")).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent)), (_, frame) => frame.ConfirmedNavigation is not null);
            var manual = Switch(PlayMode.Manual, "manual-two");
            if (manual.Frame.SelectedGoalIds.Length != 2)
                throw new InvalidOperationException("Manual mode did not restore two goals.");
            Observe("manual-before-confirmation", 24, 13, opening);
            ConfirmActual("AlliedForces.DoubleBenefit");
            var completeFirst = Observe("manual-second-active", 35, 13,
                [Entry("rawcode:F40h"), Entry("rawcode:S20h"), Entry("luffy_common")]);
            if (completeFirst.Frame.GoalId != "rawcode:H90H")
                throw new InvalidOperationException("Manual goal two did not become active.");
            ConfirmActual("PathOfKings.BountyHunter");
            var conflict = WaitCoach(main, () => typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(main, [null]),
                (_, frame) => frame.NavigationConstraint.Length > 0);
            if (conflict.Frame.SelectedGoalIds.Length != 2 ||
                conflict.Frame.CraftSteps.Any(step => TopGradePolicy.IsTopGrade(catalog.Unit(step.TargetUnitId).Tier)))
                throw new InvalidOperationException("Navigation conflict lost goals or allowed an extra top.");
            Capture("manual-conflict");
            var restored = Switch(PlayMode.Normal, "normal-owned-top");
            if (restored.Frame.GoalId != "rawcode:F40h")
                throw new InvalidOperationException("Automatic mode discarded an already-owned top.");
            var guide = Switch(PlayMode.Guide, "guide-one");
            if (guide.Frame.GuideNumber != 1 || guide.Frame.GoalId != BulletGuidePolicy.GoalId || guide.Frame.GuidePlan is null)
                throw new InvalidOperationException("Registered Guide1 did not reach the production frame.");
            Switch(PlayMode.Beginner, "beginner-restored");
            Observe("wipe-first", 55, 13, []);
            var ended = Observe("ended", 55, 13, []);
            if (ended.Decision.Kind != CoachActionKind.Finished)
                throw new InvalidOperationException("End state still emits execution guidance.");
            journal.FlushAsync().GetAwaiter().GetResult();
            WriteEvidenceText(Path.Combine(output, "four-modes.json"), JsonSerializer.Serialize(new
            {
                Kind = "production-scan-and-WPF-with-controlled-observations",
                Limitation = "Not live Warcraft combat or a novice clear-rate measurement.",
                Rows = records, Review = CoachReview.Read(journal)
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"FOUR_MODES PASS scenarios={records.Count}");
        }
        finally
        {
            main.Close();
            overlay.Stats.CloseForApplication();
            overlay.CloseForApplication();
            journal.FlushAsync().GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        (CoachDecision Decision, CoachFrame Frame) Switch(PlayMode mode, string name)
        {
            var result = WaitCoach(main, () => modeCombo.SelectedItem = PlayModes.Options.Single(item => item.Mode == mode),
                (_, frame) => frame.Mode == mode);
            CheckAndRecord(name, result);
            return result;
        }

        (CoachDecision Decision, CoachFrame Frame) Observe(string name, int round, int story,
            InventoryEntry[] entries, int rareReward = 0)
        {
            recognizer.Next = new RecognitionResult
            {
                State = RecognitionState.Ready, Entries = entries.ToList(), Status = "제어된 검증 관측",
                MapSignals = new MapSignals(Math.Min(14, story + 1), null, story,
                    rareReward > 0 ? ImmutableDictionary<string, int>.Empty.Add("e019", rareReward)
                        : ImmutableDictionary<string, int>.Empty),
                Diagnostics = new RecognitionDiagnostics
                {
                    ObservedObjects = entries.Sum(entry => entry.Count), MappedObjects = entries.Length,
                    ForeignObjects = 8, MapState = new MapStateSample(round, 0, "악몽")
                }
            };
            var result = WaitCoach(main,
                () => _ = ScanFixtureAsync(main, recognizer.Next),
                (_, frame) => frame.Round == round);
            CheckAndRecord(name, result);
            return result;
        }

        void ConfirmActual(string id)
        {
            WaitCoach(main, () => ((Button)main.FindName("ClearNavigationButton")).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent)), (_, frame) => frame.ConfirmedNavigation is null);
            var choice = (ComboBox)view.FindName("NavigationChoice");
            choice.SelectedItem = ((IEnumerable<NavigationOption>)choice.ItemsSource).Single(option => option.Id == id);
            WaitCoach(main, () => ((Button)view.FindName("ConfirmButton")).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent)), (_, frame) => frame.ConfirmedNavigation == id);
        }

        void CheckAndRecord(string name, (CoachDecision Decision, CoachFrame Frame) result)
        {
            if (!settings.AutoRecommendNavigation)
                throw new InvalidOperationException("A mode disabled automatic navigation.");
            var planning = (AdaptivePlanningCompositionRoot)typeof(MainWindow).GetField("_adaptivePlanning", flags)!.GetValue(main)!;
            if (planning.ManualLatches.NavigationOverride)
                throw new InvalidOperationException("A mode latched a manual navigation override.");
            records.Add(new { Name = name, result.Decision, result.Frame.Mode, result.Frame.GoalId,
                result.Frame.SelectedGoalIds, result.Frame.ConfirmedNavigation, result.Frame.SuggestedNavigation });
            Capture(name);
            Console.WriteLine($"FOUR_MODE {name} mode={result.Frame.Mode} goal={result.Frame.GoalId} action={result.Decision.Kind}");
        }

        void Capture(string name)
        {
            var width = main.Width;
            var height = main.Height;
            var root = (FrameworkElement)main.Content;
            var originalTransform = root.LayoutTransform;
            try
            {
                foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                {
                    var transform = new System.Windows.Media.TransformGroup();
                    transform.Children.Add(originalTransform);
                    transform.Children.Add(new System.Windows.Media.ScaleTransform(scale, scale));
                    root.LayoutTransform = transform;
                    main.Width = width * scale;
                    main.Height = height * scale;
                    main.UpdateLayout();
                    var suffix = scale == 1 ? "" : $"-{scale * 100:0}";
                    SaveCoachWindow(main, Path.Combine(output, $"main-{name}{suffix}.png"));
                    overlay.Show();
                    ApplyScale(overlay, scale);
                    overlay.Left = SystemParameters.VirtualScreenLeft - overlay.Width - 20;
                    overlay.UpdateLayout();
                    SaveCoachWindow(overlay, Path.Combine(output, $"overlay-{name}{suffix}.png"));
                }
            }
            finally
            {
                root.LayoutTransform = originalTransform;
                main.Width = width;
                main.Height = height;
                ApplyScale(overlay, 1);
            }
        }

        InventoryEntry Entry(string id) => new() { UnitId = catalog.Unit(id).Id, Count = 1 };
    }
}
