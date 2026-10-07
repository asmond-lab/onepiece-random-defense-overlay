using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrandOverlay;

internal static partial class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--consent" or "--telemetry-e2e") return TelemetryCapture.Run(args);
        if (args.Length > 0 && args[0] is "--diagnostic" or "--diagnostic-craft")
            return DiagnosticCapture.Run(args);
        if (args.Length > 0 && args[0] == "--first-legend-presentation")
            return FirstLegendPresentationCapture.Run(args);
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var settings = new AppSettings { Mode = PlayMode.Normal, AutoScanEnabled = true,
            TelemetryEnabled = false, ClearDataAutoRefresh = false };
        var fixture = OverlayExecutionContext.Fixture(settings);
        var app = new App { Execution = fixture }; app.InitializeComponent();
        TelemetryConsentStartup.ClearStartupUri(app);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Check(!fixture.RuntimeEnabled, "Fixture unexpectedly enables runtime");
        var main = new MainWindow(fixture) { Left = -5000, Top = 0 };
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", Private)!.GetValue(main)!;
        var browser = (NormalCandidateBrowser)typeof(MainWindow).GetField("_normalCandidates", Private)!.GetValue(main)!;
        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog", Private)!.GetValue(main)!;
        var captures = new List<string>();
        var results = new List<object>();
        try
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            main.Loaded += (_, _) => loaded.TrySetResult(); main.Show(); Pump(loaded.Task);
            overlay.Left = -6000; overlay.Show(); overlay.Stats.Left = -7000; overlay.Stats.Show();
            Check(main.Title == RandyPickBrand.ProductLabel, "Main title does not match current product/version label");
            Check(Visuals(main).OfType<TextBlock>().Any(text => text.Text == RandyPickBrand.BetaLabel), "Main beta label does not match current version");
            Check(Visuals(overlay).OfType<TextBlock>().Any(text => text.Text == RandyPickBrand.BetaLabel), "Overlay beta label does not match current version");
            Check(Visuals(overlay.Stats).OfType<TextBlock>().Any(text => text.Text == RandyPickBrand.ProductLabel), "Stats brand label does not match current version");
            Observe(new() { State = RecognitionState.Waiting, ConfirmsSessionBoundary = true });
            Check(!browser.Snapshot.IsCurrent, "Waiting inventory appears current");
            Capture("waiting");
            var rare = catalog.AllUnits.First(u => NormalCandidateBrowser.Tier(u) is "희귀함" or "희귀" && u.Recipe.Count > 0);
            var inventory = rare.Recipe.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            Observe(Ready(inventory));
            Check(browser.Snapshot.IsCurrent, "Controlled known fixture not current");
            Check(browser.Stage == NormalCandidateStage.Rare, "Pre-rare fixture stage wrong");
            var selected = browser.Snapshot.Groups.SelectMany(g => g.Candidates).First().Unit.Id;
            browser.Select(selected);
            var folded = browser.Snapshot.Groups.First().Name;
            browser.Fold(folded, true);
            Observe(Ready(inventory));
            Check(browser.SelectedUnitId == selected && browser.CollapsedCategories.Contains(folded), "Refresh replaced selection or folds");
            Capture("pinned-folded");
            browser.FoldAll(false);
            Capture("rare");
            inventory[rare.Id] = 1;
            Observe(Ready(inventory)); Check(browser.Stage == NormalCandidateStage.Legend, "Rare acquisition did not advance"); Capture("legend");
            var legend = catalog.AllUnits.First(u => NormalCandidateBrowser.Tier(u) == "전설" && u.Recipe.Count > 0);
            inventory[legend.Id] = 1;
            Observe(Ready(inventory)); Check(browser.Stage == NormalCandidateStage.Upper, "Legend acquisition did not advance");
            Check(browser.Snapshot.Groups.SelectMany(g => g.Candidates).All(c => NormalCandidateBrowser.IsUpper(c.Unit)), "Unapproved upper tier leaked");
            Capture("upper");
            var profiles = NormalCandidateBrowser.LoadProfile(Path.Combine(AppContext.BaseDirectory, "Data", "randypick-utility-48129.json"));
            Check(profiles.Count > 100, "Expected sourced role profile was not packaged for capture");
            var upper = profiles.Where(p => p.Direction == "magical").Select(p => catalog.Unit(p.UnitId))
                .First(u => NormalCandidateBrowser.IsUpper(u) && u.Recipe.Count > 0);
            inventory[upper.Id] = 1;
            Observe(Ready(inventory));
            Check(browser.Stage == NormalCandidateStage.Utility && browser.Snapshot.Direction == "magical", "First upper direction not from 48129");
            Check(browser.SelectedUnitId == selected, "Stage advancement replaced selected detail");
            var utility = browser.Snapshot.Groups.SelectMany(g => g.Candidates).FirstOrDefault(c => c.Unit.Id != upper.Id && !c.Owned);
            if (utility is not null)
            {
                var view = (FrameworkElement)main.FindName("NormalBrowserView");
                var button = Visuals(view).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "normal-candidate-" + utility.Unit.Id);
                Check(button is not null, "Expected utility candidate is not reachable in rendered main view");
                button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Capture("utility");
            var pinned = browser.SelectedUnitId;
            var normalView = (FrameworkElement)main.FindName("NormalBrowserView");
            var categoryPicker = Visuals(normalView).OfType<ComboBox>().Single(combo => AutomationProperties.GetAutomationId(combo) == "normal-category-picker");
            var chosenCategory = browser.Snapshot.Groups.First(group => group.Name == "스턴").Name;
            categoryPicker.SelectedItem = chosenCategory;
            Observe(Ready(inventory));
            Check((string?)categoryPicker.SelectedItem == chosenCategory && browser.SelectedUnitId == pinned, "Category navigation/refresh replaced pin or browsing choice");
            var foldButtons = Visuals(normalView).OfType<Button>().ToArray();
            foldButtons.Single(button => button.Content as string == "전체 접기").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(browser.CollapsedCategories.Count == browser.Snapshot.Groups.Count, "Main fold-all action did not update shared state");
            Visuals(normalView).OfType<Button>().Single(button => button.Content as string == "전체 펼치기").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Visuals(overlay.NormalView).OfType<Expander>().Where(expander => AutomationProperties.GetAutomationId(expander).StartsWith("overlay-category-", StringComparison.Ordinal)).All(expander => expander.IsExpanded), "Overlay ignored explicit main expand-all choice");
            Check(browser.SelectedUnitId == pinned, "Fold toolbar changed selected unit");
            foreach (var group in browser.Snapshot.Groups) browser.CollapsedCategories.Add(group.Name);
            Observe(Ready(inventory));
            Check(browser.SelectedUnitId == pinned, "Utility refresh replaced pin");
            Capture("utility-folded");
            Observe(new() { State = RecognitionState.TransientReadError });
            Check(!browser.Snapshot.IsCurrent, "Disconnected inventory remained current");
            Check(browser.SelectedUnitId == pinned, "Temporary disconnect discarded selection");
            Check(browser.Snapshot.Groups.SelectMany(g => g.Candidates).All(c => c.Completion is null), "Unknown observation exposes completion");
            Capture("stale");
            var modeCombo = (ComboBox)main.FindName("PlayModeCombo");
            foreach (var mode in new[] { PlayMode.Beginner, PlayMode.Guide, PlayMode.Manual, PlayMode.Normal })
            {
                var useOverlay = mode is PlayMode.Guide or PlayMode.Normal;
                var modeButton = Visuals(useOverlay ? (DependencyObject)overlay : main).OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == (useOverlay ? "overlay-mode-" : "main-mode-") + mode.ToString().ToLowerInvariant());
                modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Observe(Ready(inventory));
                main.Width = 1280; main.Height = 800; main.UpdateLayout();
                Check(((FrameworkElement)main.FindName("NormalBrowserView")).Visibility == (mode == PlayMode.Normal ? Visibility.Visible : Visibility.Collapsed), "Mode routing drifted");
                Check(Visuals(main).OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button).StartsWith("main-mode-", StringComparison.Ordinal) && AutomationProperties.GetItemStatus(button) == "selected").Single().Tag is PlayMode selectedMode && selectedMode == mode, "Main mode selection did not synchronize");
                if (mode is PlayMode.Beginner or PlayMode.Guide)
                {
                    var coachView = (BeginnerCoachView)main.FindName("MainCoachView");
                    var workspace = (Grid)coachView.FindName("WorkspaceBody");
                    var columnRatio = workspace.ColumnDefinitions[0].ActualWidth / workspace.ColumnDefinitions[1].ActualWidth;
                    Check(columnRatio > 1.19 && columnRatio < 1.21, "Coach columns no longer match the 1.2:1 B design");
                    if (coachView.Plan?.Components.Count == 0)
                        Check(!Visuals(coachView).OfType<TextBlock>().Any(text => text.Text.Contains("목표 재료 0 / 0")), "Missing material plan was presented as zero");
                }
                Save((FrameworkElement)main.Content, Path.Combine(output, "main-mode-" + mode.ToString().ToLowerInvariant() + ".png"));
                results.Add(new { Mode = mode.ToString(), Routed = true, ActualButtonClick = true, Origin = useOverlay ? "overlay" : "main" });
            }
            typeof(MainWindow).GetField("_coachPaused", Private)!.SetValue(main, true);
            typeof(MainWindow).GetMethod("InvalidateNormalCandidateObservation", Private)!.Invoke(main, null);
            var resumeViews = new[] { (NormalCandidateView)main.FindName("NormalBrowserView"), overlay.NormalView };
            Check(!browser.Snapshot.IsCurrent && resumeViews.All(view => view.CanResume), "Paused browser did not expose resume without current claims");
            var resumeButton = Visuals(resumeViews[0]).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "normal-resume");
            Check(resumeButton.Visibility == Visibility.Visible, "Resume action is hidden while paused");
            resumeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Observe(Ready(inventory));
            Check(browser.Snapshot.IsCurrent && resumeViews.All(view => !view.CanResume) && browser.SelectedUnitId == pinned, "Resume did not restore fenced observation while retaining selection");
            CaptureKnownSourceStats(catalog, overlay, output);
            CapturePatchNotes(main, output);
            Observe(new() { State = RecognitionState.Waiting, ConfirmsSessionBoundary = true });
            Observe(new() { State = RecognitionState.Waiting, ConfirmsSessionBoundary = true });
            Check(browser.SelectedUnitId is null && browser.Stage == NormalCandidateStage.Rare, "Confirmed match boundary did not reset browser");
            File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new
            {
                FixtureOnly = true, LiveRuntimeEnabled = fixture.RuntimeEnabled, StartupUriCleared = app.StartupUri is null,
                ActualWindows = "MainWindow, OverlayWindow, StatsOverlayWindow, Map2320PatchWindow", SourceMap = catalog.MapVersion,
                Captures = Directory.GetFiles(output, "*.png").Select(Path.GetFileName).Order().ToArray(), Modes = results, PinFoldStable = true, ExactUpperFilter = true, UnknownSuppressed = true,
                ProfileCount = profiles.Count, MatchReset = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("RANDYPICK_UI PASS: fixture-only actual WPF windows, stages, modes, pin/folds, source profile, stale suppression, 1280/920 layouts.");
            return 0;
        }
        finally { main.Close(); overlay.Stats.CloseForApplication(); overlay.CloseForApplication(); app.Shutdown(); }

        void Observe(RecognitionResult result)
        {
            main.ScanControlledAsync(result).GetAwaiter().GetResult();
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(CoachDecision _, CoachFrame __) => rendered.TrySetResult();
            main.CoachRendered += Handler;
            try { typeof(MainWindow).GetMethod("RefreshAll", Private)!.Invoke(main, [null]); Pump(rendered.Task); }
            finally { main.CoachRendered -= Handler; }
            main.UpdateLayout(); overlay.UpdateLayout(); overlay.Stats.UpdateLayout();
        }
        void Capture(string name)
        {
            foreach (var width in new[] { 1280d, 920d })
            {
                main.Width = width; main.Height = width == 920 ? 620 : 800; main.UpdateLayout();
                var host = (FrameworkElement)main.FindName("NormalBrowserView");
                var filename = $"main-{name}-{width}.png"; Save((FrameworkElement)main.Content, Path.Combine(output, filename)); captures.Add(filename);
                Check(host.ActualWidth > 0 && host.ActualHeight > 200, "Normal browser has no usable viewport");
                var detail = Visuals(host).OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-selected-detail");
                var bottom = detail.TransformToAncestor(host).Transform(new Point(0, detail.ActualHeight));
                Check(bottom.Y <= host.ActualHeight + 1, "Pinned detail escapes normal viewport");
                var candidateScroll = Visuals(host).OfType<ScrollViewer>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-candidate-scroll");
                var firstCards = Visuals(candidateScroll).OfType<System.Windows.Controls.Primitives.UniformGrid>()
                    .FirstOrDefault(grid => grid.IsVisible && grid.Children.OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b).StartsWith("normal-candidate-", StringComparison.Ordinal)));
                if (firstCards is not null && candidateScroll.VerticalOffset < 1)
                {
                    var index = width >= 1280 && firstCards.Children.Count > firstCards.Columns ? firstCards.Columns : 0;
                    var card = (FrameworkElement)firstCards.Children[index];
                    var cardBottom = card.TransformToAncestor(candidateScroll).Transform(new Point(0, card.ActualHeight));
                    var cardButton = (Button)card;
                    var renderedBorder = cardButton.Template.FindName("ButtonBody", cardButton) as Border;
                    Check(renderedBorder is not null && renderedBorder.BorderThickness == cardButton.BorderThickness && renderedBorder.BorderBrush.ToString() == cardButton.BorderBrush.ToString(), "Configured candidate border is not actually rendered");
                    Check(cardBottom.Y <= candidateScroll.ViewportHeight + 3, $"Candidate row clipped at {width}: {cardBottom.Y}/{candidateScroll.ViewportHeight}; host={host.ActualHeight}; rows=" + string.Join(",", ((Grid)((UserControl)host).Content).RowDefinitions.Select(row => row.ActualHeight)));
                }
            }
            overlay.Width = 600; overlay.Height = 800; overlay.UpdateLayout(); Save((FrameworkElement)overlay.Content, Path.Combine(output, $"overlay-{name}.png"));
            var statsLayout = OverlayLayoutPolicy.StatsLayout(OverlayDisplayMode.Full);
            overlay.Stats.Width = statsLayout.Width; overlay.Stats.Height = statsLayout.Height; overlay.Stats.UpdateLayout(); Save((FrameworkElement)overlay.Stats.Content, Path.Combine(output, $"stats-{name}.png"));
        }
    }
    private static void CapturePatchNotes(Window owner, string output)
    {
        var dialog = new Map2320PatchWindow { Owner = owner, Left = -9000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            dialog.Show(); dialog.UpdateLayout();
            var details = Visuals(dialog).OfType<Expander>().Single();
            var scroll = Visuals(dialog).OfType<ScrollViewer>().Single();
            Check(!details.IsExpanded, "Patch notes should start with concise player-facing copy");
            foreach (var width in new[] { 800d, 520d })
            {
                dialog.Width = width; dialog.Height = 720;
                details.IsExpanded = false; scroll.ScrollToHome(); dialog.UpdateLayout();
                Save(dialog, Path.Combine(output, $"patch-summary-{width}.png"));
                details.IsExpanded = true; scroll.ScrollToHome(); dialog.UpdateLayout();
                Save(dialog, Path.Combine(output, $"patch-evidence-top-{width}.png"));
                scroll.ScrollToEnd(); dialog.UpdateLayout();
                Save(dialog, Path.Combine(output, $"patch-evidence-bottom-{width}.png"));
                Check(scroll.VerticalOffset > 0, "Detailed patch evidence cannot be scrolled");
            }
        }
        finally { dialog.Close(); }
    }
    private static void CaptureKnownSourceStats(DataCatalog catalog, OverlayWindow overlay, string output)
    {
        var calculator = new InventoryStatsCalculator(catalog);
        var known = catalog.AllUnits.Select(unit => new
        {
            Unit = unit,
            Stats = calculator.Calculate(new[] { new InventoryEntry { UnitId = unit.Id, Count = 1 } })
        }).Where(item => item.Stats.SourceUnitCount > 0 && item.Stats.UnknownValueUnitCount == 0).ToArray();
        var selected = new[]
        {
            known.FirstOrDefault(item => item.Stats.Stun > 0),
            known.FirstOrDefault(item => item.Stats.TotalSlow > 0),
            known.FirstOrDefault(item => item.Stats.TotalArmorReduction > 0)
        }.Where(item => item is not null).Select(item => item!.Unit).DistinctBy(unit => unit.Id).ToArray();
        Check(selected.Length > 0, "No known source units available for stats fidelity fixture");
        var inventory = selected.Select(unit => new InventoryEntry { UnitId = unit.Id, Count = 1 }).ToList();
        var stats = calculator.Calculate(inventory);
        Check(stats.UnknownValueUnitCount == 0, "Known-source fixture unexpectedly has unknown numbers");
        overlay.Render("Known-source UI fixture", [], stats, [], [], false, [], "UI fixture", magicGoal: false, inventory: inventory);
        overlay.RenderCoach(true, new CoachDecision(CoachActionKind.Gather, "fixture:known-stats", "", "", "", "", ""),
            new CoachFrame { MatchGeneration = 999, Revision = 1, Round = 40, CompletedStoryStage = 13, IsCurrent = true,
                Mode = PlayMode.Normal, Difficulty = "악몽", GoalId = selected[0].Id,
                Inventory = inventory.ToImmutableDictionary(entry => entry.UnitId, entry => entry.Count) }, null);
        var layout = OverlayLayoutPolicy.StatsLayout(OverlayDisplayMode.Full);
        overlay.Stats.Width = layout.Width; overlay.Stats.Height = layout.Height; overlay.Stats.UpdateLayout();
        Save((FrameworkElement)overlay.Stats.Content, Path.Combine(output, "stats-known-source.png"));
        File.WriteAllText(Path.Combine(output, "stats-known-source.json"), JsonSerializer.Serialize(new
        {
            FixtureOnly = true, SourceMap = catalog.MapVersion,
            UnitIds = selected.Select(unit => unit.Id), Values = stats,
            Description = "Actual packaged calculator outputs for sourced test units; not a live game observation."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static RecognitionResult Ready(Dictionary<string, int> inventory)
    {
        var entries = inventory.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }).ToList();
        return new() { State = RecognitionState.Ready, Entries = entries,
            MapSignals = new(14, null, 13, ImmutableDictionary<string, int>.Empty),
            Diagnostics = new() { MapState = new(40, 0, "악몽"), ObservedObjects = entries.Sum(e => e.Count), MappedObjects = entries.Count } };
    }
    private static void Pump(Task task)
    {
        var bounded = task.WaitAsync(TimeSpan.FromSeconds(30));
        if (!bounded.IsCompleted) { var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher; bounded.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default); Dispatcher.PushFrame(frame); }
        bounded.GetAwaiter().GetResult();
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var sub in Visuals(child)) yield return sub; }
    }
    private static void Save(FrameworkElement element, string destination)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(destination); encoder.Save(stream);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
