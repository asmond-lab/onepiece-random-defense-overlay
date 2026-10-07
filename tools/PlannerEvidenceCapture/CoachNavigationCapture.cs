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
    private static void CaptureCoachNavigationGuidance(string output)
    {
        const string bounty = "PathOfKings.BountyHunter";
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var main = new MainWindow(FixtureContext(new AppSettings
            { Mode = PlayMode.Beginner, TelemetryEnabled = false, ClearDataAutoRefresh = false }));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog", flags)!.GetValue(main)!;
        var planner = new BeginnerCoachPlanner(catalog);
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
                var basis = new CoachFrame
                {
                    Mode = mode, MatchGeneration = 1, Revision = 1, Round = 19, CompletedStoryStage = 13,
                    Difficulty = "악몽", IsCurrent = true, SuggestedNavigation = bounty,
                    Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy_common", 1)
                };
                var cases = new (string Name, CoachFrame Frame, bool Tip, bool Confirm, bool Choice)[]
                {
                    ("round19", basis, true, false, false),
                    ("round20", basis with { Round = 20 }, false, false, false),
                    ("round21", basis with { Round = 21 }, false, true, false),
                    ("round23", basis with { Round = 23 }, false, true, false),
                    ("round24", basis with { Round = 24 }, false, true, true),
                    ("confirmed", basis with { ConfirmedNavigation = bounty }, false, false, false),
                    ("other", basis with { SuggestedNavigation = "AlliedForces.DoubleBenefit" }, false, false, false),
                    ("stale", basis with { IsCurrent = false }, false, false, false),
                    ("line", basis with { Signals = ImmutableDictionary<string, long?>.Empty.Add("line-count", 70) }, false, false, false),
                    ("reward", basis with { RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e019", 1) }, false, false, false)
                };
                foreach (var (name, frame, tip, confirmation, choice) in cases)
                {
                    var decision = planner.Decide(frame);
                    if (decision.ShowBountyHunterPreparationTip != tip || decision.RequiresUserConfirmation != confirmation ||
                        decision.RequiresNavigationChoice != choice)
                        throw new InvalidOperationException("Navigation guidance policy mismatch: " + name);
                    view.Render(decision, frame, null);
                    overlay.RenderCoach(true, decision, frame, null);
                    foreach (var surface in new[] { view, overlay.BeginnerView })
                    {
                        var advisory = (TextBlock)surface.FindName("BountyPreparationText");
                        var timing = (TextBlock)surface.FindName("NavigationTiming");
                        var confirm = (Button)surface.FindName("ConfirmButton");
                        var actualChoice = (ComboBox)surface.FindName("NavigationChoice");
                        if ((advisory.Visibility == Visibility.Visible) != tip ||
                            (advisory.Text.Length > 0) != tip || advisory.TextWrapping != TextWrapping.Wrap ||
                            AutomationProperties.GetName(advisory) != advisory.Text ||
                            timing.Visibility != Visibility.Visible || timing.Text.Length == 0 ||
                            AutomationProperties.GetName(timing) != timing.Text ||
                            (confirm.Visibility == Visibility.Visible) != confirmation ||
                            (actualChoice.Visibility == Visibility.Visible) != choice ||
                            choice && confirm.IsEnabled ||
                            ((TextBlock)surface.FindName("ControlsText")).Text != decision.Controls)
                            throw new InvalidOperationException("Rendered navigation/advisory contract mismatch: " + name);
                    }
                    var key = "navigation-" + mode + "-" + name;
                    CaptureDifficultyWindows(main, overlay, (decision, frame), output, key);
                    rows.Add(new { Name = key, frame.Round, frame.SuggestedNavigation, frame.ConfirmedNavigation,
                        decision, Timing = ((TextBlock)view.FindName("NavigationTiming")).Text,
                        Tip = ((TextBlock)view.FindName("BountyPreparationText")).Text });
                }
            }
            WriteEvidenceText(Path.Combine(output, "coach-navigation.json"), JsonSerializer.Serialize(new
            {
                Kind = "real-main-and-overlay-shared-WPF-views",
                Limitation = "Controlled planner frames; real windows and mode switch, not live Warcraft gameplay or hardware DPI.",
                Scales = new[] { 100, 125, 150 }, Rows = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"COACH_NAVIGATION PASS states={rows.Count} surfaces=2 scales=3 screenshots={rows.Count * 12}");
        }
        finally
        {
            main.Close(); overlay.Stats.CloseForApplication(); overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
