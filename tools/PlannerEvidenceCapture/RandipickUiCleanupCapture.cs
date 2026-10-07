using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OrandOverlay;
#if RANDIPICK_UI_CLEANUP
[assembly: AssemblyMetadata("CaptureEntryContract", "PlannerEvidenceCapture.RandipickUiCleanupEntry")]
#endif
namespace PlannerEvidenceCapture;

#if RANDIPICK_UI_CLEANUP
internal static class RandipickUiCleanupEntry
{
    [STAThread]
    private static int Main(string[] args) => Program.RunRandipickUiCleanup(args);
}
#endif

internal static partial class Program
{
    // No WPF execution has been authorized by the writer. Parent must bind/review this exact candidate.
    internal static int RunRandipickUiCleanup(string[] args)
    {
        RandipickCleanupArguments.Validate(args);
        var ownSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Program).Assembly.Location))).ToLowerInvariant();
        if (ownSha != args[4]) throw new ArgumentException("Candidate assembly SHA differs.");
        using var scope = CaptureOutputScope.Create(Path.GetTempPath(), args[2]);
        Output = scope;
        CaptureInputContract.ValidateBundledInputs(Path.Combine(AppContext.BaseDirectory, "Data"));
        CaptureInputContract.InitializeBundledAllowlist();
        var app = new App { Execution = FixtureContext(new AppSettings { TelemetryEnabled = false, ClearDataAutoRefresh = false }) };
        app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        typeof(Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, null);
        try { return CaptureRandipickCleanup(args[2], ownSha); }
        finally { app.Shutdown(); }
    }

    private static int CaptureRandipickCleanup(string output, string binding)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var main = new MainWindow(FixtureContext(new AppSettings
        {
            Mode = PlayMode.Guide, GuideNumber = 1, TelemetryEnabled = false,
            ClearDataAutoRefresh = false, AutoScanEnabled = true
        }));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog", flags)!.GetValue(main)!;
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        var rows = new List<object>();
        try
        {
            main.Show(); main.Left = SystemParameters.VirtualScreenLeft - main.Width - 40;
            overlay.Show(); overlay.Left = SystemParameters.VirtualScreenLeft - overlay.Width - 40;
            var initial = WaitCoach(main, () => typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(main, [null]),
                (d, f) => d.Id == "start" && f.Round == 0 && f.Inventory.Count == 0);
            Capture("waiting", initial.Decision, initial.Frame, null);
            foreach (var item in RandipickCleanupFixture.Build(catalog))
            {
                var generation = ((AdaptivePlanningCompositionRoot)typeof(MainWindow).GetField("_adaptivePlanning", flags)!.GetValue(main)!).MatchGeneration + 1;
                var revision = (long)typeof(MainWindow).GetField("_recognitionRevision", flags)!.GetValue(main)! + 1;
                var request = FastUniqueUiFixture.Request(item, generation, revision);
                var inventory = request.Entries.ToImmutableDictionary(e => e.UnitId, e => e.Count);
                var result = WaitCoach(main, () => _ = ScanFixtureAsync(main, request), (_, frame) =>
                    BulletGuideRowProjection.MatchesObservation(frame, item.Round, generation, revision, inventory, request.Diagnostics.GoroseiMarker));
                if (item.Name == "selection-1" && (result.Decision.RewardWispId == "e018" ||
                    result.Decision.SelectionBatch is not null || result.Frame.RewardWisps.GetValueOrDefault("e018") != 1))
                    throw new InvalidOperationException("Nonurgent selection wisp was not conserved.");
                if (item.Name == "first-rare-7" && result.Decision.Kind != CoachActionKind.Craft ||
                    item.Name == "e016-received" && result.Decision.Kind != CoachActionKind.Reward ||
                    item.Name == "navigation-conflict-held" && result.Decision.Id != "navigation-conflict")
                    throw new InvalidOperationException("Actual accepted frame did not produce the required state.");
                Capture(item.Name, result.Decision, result.Frame, request);
            }
            // Same real UI controls, no runtime guard bypass or production game input.
            var paused = WaitCoach(main, () => ((Button)view.FindName("PauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
                (d, f) => f.Paused && d.Id == "paused");
            Capture("paused", paused.Decision, paused.Frame, null);
            var resumed = WaitCoach(main, () => ((Button)view.FindName("PauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
                (_, f) => !f.Paused);
            foreach (var mode in new[] { PlayMode.Normal, PlayMode.Manual, PlayMode.Beginner })
            {
                var changed = WaitCoach(main, () => ((ComboBox)main.FindName("PlayModeCombo")).SelectedItem =
                    PlayModes.Options.Single(option => option.Mode == mode), (_, f) => f.Mode == mode);
                Capture("mode-" + mode, changed.Decision, changed.Frame, null);
            }
            WriteEvidenceText(Path.Combine(output, "ui-cleanup.json"), JsonSerializer.Serialize(new
            {
                Kind = "synthetic-recognition-real-accepted-WPF-main-overlay-no-game-input", Binding = binding,
                Rows = rows, Limitations = "Not live native/game evidence; keyboard is routed WPF input, not OS injection. Hardware DPI untested."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"RANDIPICK_UI_CLEANUP PASS rows={rows.Count}");
            return 0;
        }
        finally
        {
            main.Close(); overlay.Stats.CloseForApplication(); overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        void Capture(string name, CoachDecision decision, CoachFrame frame, RecognitionResult? request)
        {
            SetViewport(main, "main", 1080);
            SetViewport(overlay, "overlay", 1080);
            var display = CoachPresentation.Create(decision, frame);
            var surfaces = new[] { view, overlay.BeginnerView };
            var evidence = new List<object>();
            foreach (var surface in surfaces)
            {
                var detail = (Expander)surface.FindName("DetailsPanel");
                if (detail.IsExpanded) throw new InvalidOperationException("Disclosure unexpectedly opened during a frame transition.");
                foreach (var compact in new[] { false, true })
                {
                    surface.SetCompact(compact); surface.UpdateLayout();
                    Check("ActionText", display.Title); Check("ControlsText", display.Controls);
                    Check("ConstraintText", decision.Constraint); Check("ReasonText", decision.Reason);
                    Check("NavigationAlertText", display.NavigationAlert);
                    if (display.ShowEssentialReason && !((TextBlock)surface.FindName("ReasonText")).IsVisible)
                        throw new InvalidOperationException("Essential budget/hold reason is hidden.");
                    if (display.ShowConfirmation && !string.IsNullOrEmpty(decision.Confirmation) && !((TextBlock)surface.FindName("ConfirmationText")).IsVisible)
                        throw new InvalidOperationException("Action confirmation is hidden.");
                    if (((TextBlock)surface.FindName("OperationText")).IsVisible || ((TextBlock)surface.FindName("RecipePreviewText")).IsVisible)
                        throw new InvalidOperationException("Plan/diagnostic text leaked out of collapsed disclosure.");
                    if (((ComboBox)surface.FindName("QueenCondition")).IsVisible)
                        throw new InvalidOperationException("Optional Queen conversion leaked into the main guidance.");
                    if (display.IsStart && (((TextBlock)surface.FindName("ReasonText")).IsVisible || ((TextBlock)surface.FindName("PreserveText")).IsVisible))
                        throw new InvalidOperationException("Idle filler remains visible.");
                }
                var peer = new ExpanderAutomationPeer(detail);
                var provider = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)!;
                provider.Expand(); surface.UpdateLayout();
                if (!detail.IsExpanded || !((TextBlock)surface.FindName("DecisionDetailsText")).IsVisible)
                    throw new InvalidOperationException("Accessible disclosure failed to expose original decision.");
                var queen = (ComboBox)surface.FindName("QueenCondition");
                if (frame.Mode == PlayMode.Guide && frame.GuideNumber == 1 &&
                    frame.Inventory.GetValueOrDefault("rawcode:HA0h") > 0 && !queen.IsVisible)
                    throw new InvalidOperationException("Optional Queen conversion is unavailable in expanded details.");
                if (name is "waiting" or "selection-1" or "legend-19-auxiliary-consumable")
                {
                    if (queen.IsVisible) queen.BringIntoView();
                    var window = surface == view ? (Window)main : overlay;
                    window.UpdateLayout();
                    SaveCoachWindow(window, Path.Combine(output, $"details-{(surface == view ? "main" : "overlay")}-{name}.png"));
                }
                // A repaint must not overwrite the user's disclosure choice.
                surface.Render(decision, frame, decision.TargetUnitId is { } id ? catalog.Unit(id) : null);
                if (!detail.IsExpanded) throw new InvalidOperationException("Render reset disclosure selection.");
                provider.Collapse(); surface.UpdateLayout();
                var toggle = CleanupDescendants(detail).OfType<ToggleButton>().First();
                if (!toggle.Focusable || !toggle.IsTabStop) throw new InvalidOperationException("Disclosure header is not keyboard reachable.");
                detail.Focus(); detail.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                var source = PresentationSource.FromVisual(toggle) ?? throw new InvalidOperationException("No presentation source.");
                toggle.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
                toggle.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
                surface.UpdateLayout();
                if (!detail.IsExpanded) throw new InvalidOperationException("WPF Space key did not open disclosure.");
                provider.Collapse(); surface.SetCompact(frame.Mode != PlayMode.Beginner);
                ((ScrollViewer)surface.FindName("CoachScroll")).ScrollToTop();
                evidence.Add(new { Surface = surface == view ? "main" : "overlay", KeyboardRoutedSpace = true,
                    AccessibleExpandCollapse = true, Text = CleanupDescendants(surface).OfType<TextBlock>()
                        .Where(t => !string.IsNullOrEmpty(t.Name)).Select(t => new { t.Name, t.Text, t.IsVisible }).ToArray() });

                void Check(string field, string expected)
                {
                    var text = (TextBlock)surface.FindName(field);
                    if (text.Text != expected || AutomationProperties.GetName(text) != expected || text.TextTrimming != TextTrimming.None)
                        throw new InvalidOperationException(field + " differs from actual presentation or truncates text.");
                }
            }
            foreach (var width in new[] { 1080, 920 })
            {
                SetViewport(main, "main", width);
                SaveCoachWindow(main, Path.Combine(output, $"main-{width}-{name}.png"));
            }
            overlay.UpdateLayout(); SaveCoachWindow(overlay, Path.Combine(output, $"overlay-540-{name}.png"));
            main.Width = 1080; main.Height = 720;
            rows.Add(new { Name = name, Request = request, Decision = decision, Display = display,
                Projection = BulletGuideRowProjection.Observe(name, decision, frame), Surfaces = evidence });
        }

        void SetViewport(Window window, string surface, int width)
        {
            var viewport = RandipickCleanupViewport.Resolve(surface, window.Width, window.Height,
                window.Content is FrameworkElement content ? content.LayoutTransform.Value.M11 : 1, width);
            if (window.Content is FrameworkElement root)
                root.LayoutTransform = new ScaleTransform(viewport.ContentScale, viewport.ContentScale);
            window.Width = viewport.Width;
            window.Height = viewport.Height;
            window.UpdateLayout();
        }
    }

    private static IEnumerable<DependencyObject> CleanupDescendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in CleanupDescendants(child)) yield return nested;
        }
    }
}
