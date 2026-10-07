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
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            main.Loaded += (_, _) => loaded.TrySetResult();
            main.Show();
            var loadedSignal = loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var loadedPump = new DispatcherFrame();
            loadedSignal.ContinueWith(_ => main.Dispatcher.BeginInvoke(new Action(() => loadedPump.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(loadedPump); loadedSignal.GetAwaiter().GetResult();
            main.Left = -4000;
            var nativeResult = DwmGetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(main).Handle, 34, out var nativeBorder, sizeof(int));
            Console.WriteLine($"NATIVE_BORDER setter={main.NativeBorderApplyResult} readback={nativeResult} requested={MainPlanTheme.NativeBorderColor:X}");
            Check(main.NativeBorderApplyResult == 0, "Neutral native border request failed");
            if (nativeResult == 0) Check(nativeBorder == MainPlanTheme.NativeBorderColor, "Native border readback differs");
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
            CaptureBrookReference(frame, catalog);
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
        void CaptureBrookReference(CoachFrame original, DataCatalog catalog)
        {
            const string brook = "rawcode:D00h";
            var owned = new Dictionary<string, int> { ["rawcode:U20h"] = 1, ["rawcode:930h"] = 1 };
            var needed = 0;
            bool ContainsBrook(string id, HashSet<string> visiting)
            {
                if (id == brook) return true;
                if (!visiting.Add(id)) return false;
                return catalog.Unit(id).Recipe.Keys.Any(child => ContainsBrook(child, new HashSet<string>(visiting)));
            }
            void Stock(string id, int count)
            {
                if (id == brook) { needed += count; return; }
                var unit = catalog.Unit(id);
                if (ContainsBrook(id, []))
                    foreach (var pair in unit.Recipe) Stock(pair.Key, pair.Value * count);
                else owned[id] = owned.GetValueOrDefault(id) + count;
            }
            Stock("rawcode:V20h", 1);
            foreach (var pair in catalog.Unit(brook).Recipe)
                owned[pair.Key] = owned.GetValueOrDefault(pair.Key) + pair.Value * needed;
            var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
            var engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys);
            var planner = new AutoCombinePlanner(catalog, hotkeys);
            var session = new BeginnerCoachSession(catalog);
            CoachFrame Frame(long revision)
            {
                var entries = owned.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
                var recommendations = engine.RecommendNearestCrafts("rawcode:V20h", entries, difficulty: "악몽", round: 40, completedStoryStage: 13)
                    .Where(item => item.Route.GoalUnitId == "rawcode:V20h").ToArray();
                return original with { Inventory = owned.ToImmutableDictionary(), Revision = revision, RecognitionRevision = revision,
                    NativeNavigation = new(NativeNavigationStatus.Selected, BulletGuidePolicy.NavigationId, "controlled fixture observation"),
                    ConfirmedNavigation = BulletGuidePolicy.NavigationId,
                    Recommendations = recommendations, CraftSteps = planner.Plan(recommendations, entries),
                    Signals = original.Signals.SetItem("gold", 10000).SetItem("lumber", 100),
                    RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e018", 5) };
            }
            var before = Frame(100);
            var offered = session.Update(before);
            Console.WriteLine($"BROOK offer={offered.Id} target={offered.TargetUnitId} needed={needed} steps={string.Join(",", before.CraftSteps.Select(step => step.TargetUnitId))}");
            Check(offered.TargetUnitId == brook && offered.Kind == CoachActionKind.Craft, "Real Brook recipe fixture not actionable");
            Check(offered.CraftProgress is { CompletedCount: 0 } && offered.CraftProgress.RequiredCount == needed,
                "Brook requested quantity not derived from actual recipe");
            view.Render(offered, before, catalog.Unit(brook)); overlay.RenderCoach(true, offered, before, catalog.Unit(brook));
            foreach (var branch in Descendants(view).OfType<Expander>().Where(item =>
                System.Windows.Automation.AutomationProperties.GetAutomationId(item).StartsWith("plan-branch:", StringComparison.Ordinal))) branch.IsExpanded = false;
            File.WriteAllText(Path.Combine(output, "brook-reference.json"), JsonSerializer.Serialize(new { decision = offered, Frame = before, ActualRecipeBrookCount = needed }, new JsonSerializerOptions { WriteIndented = true }));
            Capture("brook-reference");
            main.Activate();
            var navigationButton = main.PlanNavigationButton;
            System.Windows.Input.Keyboard.Focus(navigationButton); main.UpdateLayout();
            Check(navigationButton.IsKeyboardFocused, "Keyboard focus did not reach plan navigation");
            var focusBorder = (Border)navigationButton.Template.FindName("NavBody", navigationButton);
            Check(focusBorder.BorderBrush == OverlayTheme.FocusBrush, "Plan keyboard focus is not visible teal");
            VerifyBorders((FrameworkElement)main.Content);
            Save((FrameworkElement)main.Content, Path.Combine(output, "main-keyboard-focus.png"));
            SaveNativeFrame(main, Path.Combine(output, "main-native-frame.png"));
            overlay.Activate();
            var pause = (Button)overlay.BeginnerView.FindName("PauseButton");
            System.Windows.Input.Keyboard.Focus(pause); overlay.UpdateLayout();
            Check(pause.IsKeyboardFocused, "Keyboard focus did not reach coach control");
            Check(((Border)pause.Template.FindName("Body", pause)).BorderBrush == OverlayTheme.FocusBrush,
                "Coach keyboard focus is not visible teal");
            VerifyBorders(overlay); Save(overlay, Path.Combine(output, "overlay-keyboard-focus.png"));
            foreach (var pair in catalog.Unit(brook).Recipe) owned[pair.Key] -= pair.Value;
            owned[brook] = 1;
            var after = Frame(101);
            var decision = session.Update(after);
            Check(after.Inventory[brook] == 1 && decision.TargetUnitId != brook, "Completed Brook did not advance the real recipe");
            var nextUnit = decision.TargetUnitId is { } nextId ? catalog.Unit(nextId) : null;
            view.Render(decision, after, nextUnit); overlay.RenderCoach(true, decision, after, nextUnit);
            Capture("brook-result");
        }

        void Capture(string name)
        {
            var client = (FrameworkElement)main.Content;
            foreach (var size in new[] { new Size(1384, 820), new Size(920, 620) })
            {
                main.Width = size.Width + main.ActualWidth - client.ActualWidth;
                main.Height = size.Height + main.ActualHeight - client.ActualHeight;
                main.UpdateLayout();
                VerifyBorders(client);
                VerifyLayout(client);
                Save(client, Path.Combine(output, $"client-{name}-{(int)size.Width}.png"));
                if (name == "brook-reference")
                {
                    var ids = new[] { "main-plan-heading", "main-plan-metrics", "main-route-panel", "main-action-panel",
                        "plan-component:rawcode:U20h", "plan-component:rawcode:930h", "plan-component:rawcode:V20h" };
                    var boxes = ids.ToDictionary(id => id, id =>
                    {
                        var element = Descendants(client).OfType<FrameworkElement>().Single(item =>
                            System.Windows.Automation.AutomationProperties.GetAutomationId(item) == id);
                        var point = element.TransformToAncestor(client).Transform(new Point());
                        return new { x = point.X, y = point.Y, width = element.ActualWidth, height = element.ActualHeight };
                    });
                    var metrics = boxes["main-plan-metrics"]; var route = boxes["main-route-panel"]; var action = boxes["main-action-panel"];
                    Check(Math.Abs(metrics.x - route.x) < 1 && Math.Abs(metrics.x + metrics.width - action.x - action.width) < 1,
                        "Metrics do not span the complete plan/action workspace");
                    Check(action.width >= 240 && route.width >= 280 && Math.Abs(action.y - route.y) < 1,
                        "Plan and action are not usable side by side at a supported width");
                    Check(boxes.Values.All(box => box.x >= 0 && box.x + box.width <= client.ActualWidth + 1),
                        "Main content overflows horizontally");
                    var tiles = Descendants(client).OfType<FrameworkElement>().Where(item =>
                        System.Windows.Automation.AutomationProperties.GetAutomationId(item).StartsWith("main-metric-tile:", StringComparison.Ordinal)).ToArray();
                    Check(tiles.Length == 4 && tiles.All(tile => tile.ActualWidth > 0) &&
                        tiles.Max(tile => tile.ActualWidth) - tiles.Min(tile => tile.ActualWidth) <= 1,
                        "Metric values lack usable equal columns");
                    var actionText = (TextBlock)view.FindName("ActionText");
                    var actionPoint = actionText.TransformToAncestor(client).Transform(new Point());
                    Check(actionPoint.Y + actionText.ActualHeight < client.ActualHeight - 32,
                        "Current action is not visible in the initial viewport");
                    File.WriteAllText(Path.Combine(output, $"geometry-{(int)size.Width}.json"), JsonSerializer.Serialize(boxes, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            main.Width = 1400; main.Height = 900; main.UpdateLayout(); Save(main, Path.Combine(output, "main-" + name + ".png"));
            main.Width = 920; main.Height = 620; main.UpdateLayout(); Save(main, Path.Combine(output, "narrow-" + name + ".png"));
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
            overlay.UpdateLayout(); VerifyBorders(overlay); VerifyLayout(overlay); Save(overlay, Path.Combine(output, "overlay-" + name + ".png"));
        }
    }
    private static RecognitionResult Ready(List<InventoryEntry> entries) => new()
    {
        State = RecognitionState.Ready, Entries = entries,
        MapSignals = new(14, null, 13, ImmutableDictionary<string, int>.Empty.Add("e018", 5)),
        Diagnostics = new() { MapState = new(40, 0, "악몽"), ObservedObjects = entries.Sum(x => x.Count),
            MappedObjects = entries.Count, ForeignObjects = 8 }
    };
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    private static void SaveNativeFrame(Window window, string path)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        Check(GetWindowRect(handle, out var rect), "Could not read fixture window bounds");
        using var bitmap = new System.Drawing.Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            var dc = graphics.GetHdc();
            try { Check(PrintWindow(handle, dc, 2), "Could not capture fixture native frame"); }
            finally { graphics.ReleaseHdc(dc); }
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        for (var y = 48; y < bitmap.Height - 16; y++)
            for (var edge = 0; edge < 8; edge++)
                foreach (var x in new[] { edge, bitmap.Width - 1 - edge })
                {
                    var c = bitmap.GetPixel(x, y);
                    Check(!(c.R > c.G && c.G > c.B + 20 && c.R > c.B + 50), "Warm native frame edge remains");
                }
    }

    private static void VerifyLayout(FrameworkElement root)
    {
        foreach (var text in Visuals(root).OfType<TextBlock>().Where(item => item.IsVisible && item.ActualWidth > 0))
        {
            var point = text.TransformToAncestor(root).Transform(new Point());
            Check(point.X >= -1 && point.X + text.ActualWidth <= root.ActualWidth + 1, "Horizontal text overflow");
            Check(text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom <= text.ActualHeight + 1,
                "Text height clipped outside the scrolling contract");
        }
    }

    private static void VerifyBorders(DependencyObject root)
    {
        foreach (var border in Visuals(root).OfType<Border>())
            if (border.BorderBrush is SolidColorBrush brush && border.BorderThickness != new Thickness())
            {
                var c = brush.Color;
                Check(!(c.A > 0 && c.R > c.G && c.G > c.B + 20 && c.R > c.B + 50),
                    $"Warm border remains: {c}");
            }
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }

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
