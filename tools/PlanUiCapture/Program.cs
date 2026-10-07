using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrandOverlay;

internal static class Program
{
    private static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var settings = new AppSettings { Mode = PlayMode.Guide, GuideNumber = 1,
            AutoScanEnabled = true, TelemetryEnabled = false, ClearDataAutoRefresh = false };
        var safeFixture = OverlayExecutionContext.Fixture(settings);
        var app = new App { Execution = safeFixture };
        app.InitializeComponent();
        TelemetryConsentStartup.ClearStartupUri(app);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var main = new MainWindow(safeFixture);
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", Flags)!.GetValue(main)!;
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        try
        {
            main.Show();
            main.Left = -4000;
            overlay.Show();
            overlay.Left = -4000;
            var waiting = new RecognitionResult { State = RecognitionState.Waiting, ConfirmsSessionBoundary = true };
            Observe(waiting);
            Observe(waiting);
            var generation = Generation();
            var idleText = ((TextBlock)main.FindName("RouteQuestStatusText")).Text;
            for (var i = 0; i < 20; i++)
            {
                Observe(waiting);
                Check(Generation() == generation, "Waiting reset generation repeatedly");
                Check(((TextBlock)main.FindName("RouteQuestStatusText")).Text == idleText, "Idle route text alternated");
            }
            Capture("idle");
            Observe(Ready([new() { UnitId = "rawcode:U20h", Count = 1 }, new() { UnitId = "rawcode:930h", Count = 1 },
                new() { UnitId = "rawcode:D00h", Count = 1 }, new() { UnitId = "rawcode:200h", Count = 3 }, new() { UnitId = "rawcode:800h", Count = 2 }]));
            Check(view.Plan!.IsCurrent, "Ready plan not current");
            Check(view.Plan.Components.Select(item => item.UnitId).SequenceEqual(new[] { "rawcode:U20h", "rawcode:930h", "rawcode:V20h" }), "Bullet component display order drifted");
            Check(view.Plan.Components.Any(item => item.UnitId == "rawcode:V20h" && !item.IsOwned), "Missing Smoker omitted");
            Capture("missing-smoker");
            var route = (CoachPlanView)view.FindName("PlanView");
            var branch = Descendants(route).OfType<Expander>().Single(item =>
                System.Windows.Automation.AutomationProperties.GetAutomationId(item) == "plan-branch:rawcode:V20h");
            branch.IsExpanded = true;
            route.Render(view.Plan, true);
            Check(Descendants(route).OfType<Expander>().Single(item =>
                System.Windows.Automation.AutomationProperties.GetAutomationId(item) == "plan-branch:rawcode:V20h").IsExpanded,
                "Route expansion was lost on refresh");
            Capture("expanded-route");
            var decision = (CoachDecision)typeof(MainWindow).GetField("_lastCoachDecision", Flags)!.GetValue(main)!;
            var frame = (CoachFrame)typeof(MainWindow).GetField("_lastCoachFrame", Flags)!.GetValue(main)!;
            var titleCatalog = new DataCatalog(); titleCatalog.Load(false);
            Check(((TextBlock)view.FindName("ActionText")).Text == CoachPresentation.ActionTitle(decision,
                decision.TargetUnitId is { } actionId ? titleCatalog.Unit(actionId) : null), "Display title does not bind projection");
            Check(view.Plan.RouteUnitIds.First() == decision.TargetUnitId && view.Plan.RouteUnitIds.Last() == frame.GoalId,
                "Route endpoints do not match the current target and goal");
            // Controlled display fixture: receipt policy itself is covered by ObservedCraftProgressTests.
            var pending = decision with { Kind = CoachActionKind.Waiting, Id = "capture:pending", Title = "조합 결과 확인 중",
                Controls = "새 패가 확인될 때까지 추가 조합을 멈추세요.", TargetUnitId = "rawcode:D00h", CraftRecipe = null,
                CraftProgress = new("rawcode:D00h", 2, 1, true) };
            var catalog = new DataCatalog(); catalog.Load(false);
            var completeIngredients = catalog.Unit("rawcode:V20h").Recipe.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToList();
            completeIngredients.Add(new() { UnitId = "rawcode:U20h", Count = 1 });
            completeIngredients.Add(new() { UnitId = "rawcode:930h", Count = 1 });
            Observe(Ready(completeIngredients));
            Capture("recipe-ready-observation");
            view.Render(pending, frame, catalog.Unit("rawcode:D00h"));
            overlay.RenderCoach(true, pending, frame, catalog.Unit("rawcode:D00h"));
            Check(view.Plan.CraftProgress == overlay.BeginnerView.Plan!.CraftProgress, "Counts diverged");
            Check(((ProgressBar)view.FindName("CraftCountBar")).Value == 1, "Progress not bound to completed count");
            Check(((ProgressBar)view.FindName("CraftCountBar")).Maximum == 2, "Progress not bound to required count");
            Capture("craft-pending");
            var beforeBoundary = Generation();
            Observe(waiting); Observe(waiting);
            Check(Generation() == beforeBoundary + 1, "Ready to Waiting did not reset once");
            for (var i = 0; i < 20; i++) Observe(waiting);
            Check(Generation() == beforeBoundary + 1, "Second Waiting boundary repeated reset");
            Capture("idle-after-ready");
            File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new {
                SafeFixture = !safeFixture.RuntimeEnabled, ClearedStartupUri = app.StartupUri is null,
                RepeatedWaiting = 20, InitialGeneration = generation, FinalGeneration = Generation(),
                MissingSmoker = true, SharedCounts = true, ActualSurface = "MainWindow / OverlayWindow",
                ObservationApi = "ScanControlledAsync (does not externally reset main window)"
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PLAN_UI PASS idle, missing-smoker, pending, narrow, shared counts, 20+20 Waiting");
            return 0;
        }
        finally { main.Close(); overlay.Stats.CloseForApplication(); overlay.CloseForApplication(); app.Shutdown(); }

        void Observe(RecognitionResult result)
        {
            var task = main.ScanControlledAsync(result);
            if (!task.IsCompleted) throw new InvalidOperationException("Controlled observation unexpectedly asynchronous");
            task.GetAwaiter().GetResult();
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnRendered(CoachDecision decision, CoachFrame frame) => rendered.TrySetResult();
            main.CoachRendered += OnRendered;
            try
            {
                typeof(MainWindow).GetMethod("RefreshAll", Flags)!.Invoke(main, [null]);
                var bounded = rendered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                var pump = new DispatcherFrame();
                bounded.ContinueWith(_ => main.Dispatcher.BeginInvoke(new Action(() => pump.Continue = false)), TaskScheduler.Default);
                Dispatcher.PushFrame(pump);
                bounded.GetAwaiter().GetResult();
            }
            finally { main.CoachRendered -= OnRendered; }
            main.UpdateLayout();
        }
        long Generation() => ((AdaptivePlanningCompositionRoot)typeof(MainWindow).GetField("_adaptivePlanning", Flags)!.GetValue(main)!).MatchGeneration;
        void Capture(string name)
        {
            main.Width = 1400; main.Height = 900; main.UpdateLayout(); Save(main, Path.Combine(output, "main-" + name + ".png"));
            main.Width = 920; main.Height = 720; main.UpdateLayout(); Save(main, Path.Combine(output, "narrow-" + name + ".png"));
            if (view.Plan?.IsCurrent == true)
            {
                var scroll = (ScrollViewer)view.FindName("CoachScroll");
                scroll.ScrollToHome(); main.UpdateLayout();
                var navigation = Descendants(main).OfType<Button>().Single(button =>
                    System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "main-plan-navigation");
                navigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                main.UpdateLayout();
                var planHost = (CoachPlanView)view.FindName("PlanView");
                var top = planHost.TransformToAncestor(scroll).Transform(new Point());
                Check(top.Y >= -1 && top.Y < scroll.ViewportHeight, "Plan navigation did not reveal route host");
                Save(main, Path.Combine(output, "narrow-plan-" + name + ".png"));
                scroll.ScrollToEnd(); main.UpdateLayout();
                Save(main, Path.Combine(output, "narrow-plan-bottom-" + name + ".png"));
                scroll.ScrollToHome(); main.UpdateLayout();
            }
            // Production intentionally hides its overlay without a match; expose only
            // this inert fixture window to inspect the underlying idle view pixels.
            overlay.Show(); overlay.Left = -4000;
            overlay.UpdateLayout(); Save(overlay, Path.Combine(output, "overlay-" + name + ".png"));
        }
    }
    private static RecognitionResult Ready(List<InventoryEntry> entries) => new()
    {
        State = RecognitionState.Ready, Entries = entries,
        MapSignals = new(14, null, 13, ImmutableDictionary<string, int>.Empty.Add("e018", 5)),
        Diagnostics = new() { MapState = new(40, 0, "악몽"), ObservedObjects = entries.Sum(x => x.Count),
            MappedObjects = entries.Count, ForeignObjects = 8 }
    };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Save(FrameworkElement view, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
}
