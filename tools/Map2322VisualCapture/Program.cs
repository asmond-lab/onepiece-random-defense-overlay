using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Map2322VisualCapture;
using OrandOverlay;

internal sealed record ScreenshotCheck(string Scenario, string Size, string Surface, string File, long Hwnd,
    int PixelWidth, int PixelHeight, string Mechanism, bool PngSignatureValid, int ScreenLeft, int ScreenTop,
    string ExpectedState, long SourceRevision, string BoundStatus, string VisibleCaptionBefore,
    string VisibleCaptionAfter, string Sha256, long PaintedHwnd, long PaintBoundary,
    long CaptureBoundary, DateTimeOffset CapturedAt, string FixtureContext,
    string NavigationSummaryBefore = "", string NavigationSummaryAfter = "");
internal sealed record ActionCheck(string Id, string Mechanism, bool Applied, string? SelectedId);
internal sealed record CaptureReport(int FixturePid, bool Synthetic, string PackagedRuntimeIsolationBlocker,
    string FixtureMapVersion, long MainHwnd, bool RuntimeEnabled,
    IReadOnlyDictionary<string, int> FixtureInventory, SourceChecks Source, ScreenshotCheck[] Screenshots, ActionCheck[] Actions, bool AutoNavigationDisabled,
    bool MainNormalVisible, bool YujiroDetailSelected, bool CraftIndependent, bool StatsVisible,
    bool ScreenshotInventoryComplete, bool AllOwnedWindowsClosed, string? Error);

internal static class Program
{
    private static System.Drawing.Rectangle _secondaryWork;
    private static string _fixtureVersion = "2.322";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is not (1 or 2) || Directory.Exists(args[0]) || args.Length == 2 && args[1] != "--2323")
        {
            Console.Error.WriteLine("Usage: Map2322VisualCapture <new-evidence-directory> [--2323]");
            return 2;
        }
        _fixtureVersion = args.Length == 2 ? "2.323" : "2.322";
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var displays = System.Windows.Forms.Screen.AllScreens.Select(s => new {
            s.DeviceName, s.Primary, bounds = s.Bounds, work = s.WorkingArea
        }).ToArray();
        var secondary = displays.Where(s => !s.Primary)
            .OrderByDescending(s => (long)s.work.Width * s.work.Height).FirstOrDefault();
        if (secondary is null)
        {
            File.WriteAllText(Path.Combine(output, "placement.json"), JsonSerializer.Serialize(new {
                displays, error = "No secondary display connected; GUI capture was not started"
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine("No secondary display connected; GUI capture was not started.");
            return 3;
        }
        var work = secondary.work;
        _secondaryWork = work;
        var monitor = MonitorFromPoint(new NativePoint(work.Left + work.Width / 2, work.Top + work.Height / 2), 2);
        if (GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Secondary monitor DPI unavailable");
        var scaleX = dpiX / 96d; var scaleY = dpiY / 96d;
        var placements = new List<object>();
        var settings = new AppSettings
        {
            Mode = PlayMode.Normal, AutoScanEnabled = true, AutoUpdateEnabled = false,
            TelemetryEnabled = false, OverlayDisplayMode = OverlayDisplayMode.Full,
            OverlayLeft = (work.Left + 430) / scaleX, OverlayTop = (work.Top + 90) / scaleY,
            StatsOverlayLeft = (work.Left + 50) / scaleX, StatsOverlayTop = (work.Top + 90) / scaleY
        };
        var execution = OverlayExecutionContext.Fixture(settings);
        var app = new App { Execution = execution, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        TelemetryConsentStartup.ClearStartupUri(app);
        var main = new MainWindow(execution, fixtureMapVersion: _fixtureVersion)
        { Left = (work.Left + 800) / scaleX, Top = (work.Top + 60) / scaleY,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false };
        void Place(Window window, int left, int top)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left / scaleX; window.Top = top / scaleY;
            window.SourceInitialized += (_, _) => ClampOwned(window, work, left, top, placements);
        }
        Place(main, work.Left + 800, work.Top + 60);
        if (main.StartupAborted) throw new InvalidOperationException("2.322 fixture catalog failed to initialize.");
        var fixturePid = Environment.ProcessId;
        var screenshots = new List<ScreenshotCheck>();
        var actions = new List<ActionCheck>();
        var identity = new List<object>();
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.ContentRendered += (_, _) => loaded.TrySetResult();
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            string? error = null;
            SourceChecks? source = null;
            long hwnd = 0;
            bool autoDisabled = false, mainNormal = false, yujiroSelected = false, independent = false, statsVisible = false;
            try
            {
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
                source = CaptureContract.Inspect(_fixtureVersion);
                if (source.FixtureMapVersion != _fixtureVersion || source.Navigation.Length != 15 ||
                    source.Navigation.Any(x => !x.ResolvesInSelectedCatalog || !x.ManualOnly) ||
                    source.Story.Length != 14 || source.Story.Any(x => !x.SourceMatches || !x.BaseOnly || x.OwnerId != 5) ||
                    source.Yujiro.FixedOnlyReady || source.Yujiro.DistinctPickReady || source.Yujiro.CombineCommandAvailable ||
                    !source.Kaido.DragonAir || source.Kaido.HybridAir || source.Kaido.UnknownAir ||
                    !source.References.BasicAccepted || !source.References.FullAccepted ||
                    !source.References.WrongFingerprintDenied || !source.References.WrongVersionDenied ||
                    !source.References.ExpiredDenied || !source.References.MissingArchiveDenied ||
                    !source.Legacy321StillLoads)
                    throw new InvalidOperationException("Selected 2.322 source contract failed.");
                var catalog = execution.CreateCatalog(); catalog.Load(loadCarryPolicy: false, mapVersion: _fixtureVersion);
                var model = NormalCandidateBrowser.Create(catalog);
                var inventory = new Dictionary<string, int>(CaptureContract.FixtureInventory());
                model.Update(inventory, current: true, generation: 322);
                model.SetStage(NormalCandidateStage.Upper);
                var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
                main.NormalBrowserView.SetCraftPlanner(new NormalCraftPlanner(catalog));
                overlay.NormalView.SetCraftPlanner(new NormalCraftPlanner(catalog));
                main.NormalBrowserView.SetModel(model);
                overlay.NormalView.SetModel(model);
                model.PresentationChanged += () => { main.NormalBrowserView.Render(); overlay.NormalView.Render(); };
                NormalCandidateBrowser? AppModel() => (NormalCandidateBrowser?)typeof(MainWindow)
                    .GetField("_diagnosticCandidates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(main);
                NormalCandidateBrowser? ViewModel(NormalCandidateView view) => (NormalCandidateBrowser?)typeof(NormalCandidateView)
                    .GetField("_model", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view);
                object? Browser(NormalCandidateBrowser? value) => value is null ? null : new {
                    objectId = RuntimeHelpers.GetHashCode(value), value.SelectedUnitId, value.SessionRevision,
                    value.IsDiagnosticReference, value.Stage, value.Snapshot.IsCurrent };
                void Observe(string boundary) => identity.Add(new { boundary, at = DateTimeOffset.UtcNow,
                    fixture = Browser(model), appOwned = Browser(AppModel()), mainView = Browser(ViewModel(main.NormalBrowserView)),
                    overlayView = Browser(ViewModel(overlay.NormalView)), craftSelected = overlay.NormalView.HasCraftSelection });
                Observe("before-action");
                var diagnosticAtSubscription = AppModel();
                if (diagnosticAtSubscription is not null)
                    diagnosticAtSubscription.PresentationChanged += () => Observe("inside-diagnostic-presentation-callback-after-app-handler");
                model.PresentationChanged += () => Observe("inside-fixture-presentation-callback");
                overlay.SetNormalBrowserActive(true, PlayMode.Normal, "관측되지 않은 조건은 조합 보류");
                main.NormalBrowserView.Visibility = Visibility.Visible;
                Place(overlay, work.Left + 430, work.Top + 90);
                Place(overlay.Stats, work.Left + 50, work.Top + 90);
                Place(main.CraftWindow, work.Left + 1330, work.Top + 80);
                overlay.Show();
                overlay.Stats.Show();
                main.CraftWindow.SetDisplayAllowed(true);
                main.CraftWindow.ShowWorkspace();
                await Rendered(main);
                hwnd = new WindowInteropHelper(main).Handle.ToInt64();
                if (hwnd == 0) throw new InvalidOperationException("MainWindow HWND missing.");
                autoDisabled = !main.AutoNavigationCheck.IsEnabled;
                mainNormal = main.NormalBrowserView.IsVisible;
                independent = main.CraftWindow.IsVisible &&
                    new WindowInteropHelper(main.CraftWindow).Handle != new WindowInteropHelper(overlay).Handle;
                statsVisible = overlay.Stats.IsVisible;
                var root = AutomationElement.FromHandle((IntPtr)hwnd);
                yujiroSelected = await SelectYujiroCard(main, model, source.Yujiro.UnresolvedConditions) &&
                    main.NormalBrowserView.IsVisible && main.CraftWindow.IsVisible;
                actions.Add(new("yujiro-selection", "UI Automation InvokePattern on visible materialized candidate card", yujiroSelected, model.SelectedUnitId));
                if (_fixtureVersion == "2.323")
                {
                    await Shot(main, "2323-main-normal", "default", output, screenshots);
                    await Shot(main.CraftWindow, "2323-craft", "default", output, screenshots);
                    await Shot(overlay.Stats, "2323-stats", "default", output, screenshots);
                    AutomationProperties.SetAutomationId(main.MainObservationStatus, "fixture-main-status");
                    AutomationProperties.SetAutomationId(main.NavigationCombo, "fixture-navigation-option");
                    AutomationProperties.SetAutomationId(main.NavigationCandidateText, "fixture-navigation-summary");
                    await CaptureReferences(main, catalog, inventory, output, screenshots, actions, Observe, AppModel,
                        source.Yujiro.UnresolvedConditions);
                    var profileButton = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "main-profile-navigation"))
                        ?? throw new InvalidOperationException("Profile navigation UIA button missing");
                    await Invoke(main, profileButton);
                    var profileWindow = app.Windows.OfType<AuxiliaryWindow>().Single(x => x.Pane == AuxiliaryPane.Profile);
                    var navigation = source.Navigation[0];
                    var selected = MainWindow.ResolveVersionNavigation(catalog, navigation.Id);
                    var summaryChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var textProperty = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
                    EventHandler onSummary = (_, _) =>
                    {
                        if (main.NavigationCandidateText.Text.Contains(selected.Name, StringComparison.Ordinal))
                            summaryChanged.TrySetResult();
                    };
                    textProperty.AddValueChanged(main.NavigationCandidateText, onSummary);
                    try
                    {
                        main.NavigationCategoryCombo.SelectedItem = NavigationProfiles.Categories.Single(x => x.Id == navigation.CategoryId);
                        main.NavigationCombo.SelectedItem = main.NavigationCombo.Items.OfType<NavigationOption>().Single(x => x.Id == navigation.Id);
                        onSummary(main, EventArgs.Empty);
                        await summaryChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    finally { textProperty.RemoveValueChanged(main.NavigationCandidateText, onSummary); }
                    await Shot(main, "2323-navigation", "default", output, screenshots);
                    await Shot(profileWindow, "2323-profile", "default", output, screenshots);
                    actions.Add(new("2323-navigation-selection", "selected 2.323 option in actual ComboBox; not a saved/game choice",
                        (main.NavigationCombo.SelectedItem as NavigationOption)?.Id == navigation.Id &&
                        main.NavigationCandidateText.Text.Contains(selected.Name, StringComparison.Ordinal), selected.Name));
                    if (!CaptureContract.ScreenshotScenarios(source).Order(StringComparer.Ordinal).SequenceEqual(
                            screenshots.Select(x => x.File).Order(StringComparer.Ordinal)) ||
                        actions.Any(x => !x.Applied) || !autoDisabled || !mainNormal || !yujiroSelected || !independent || !statsVisible)
                        throw new InvalidOperationException("2.323 fixture source or presentation mismatch");
                }
                else
                {
                var transform = (TransformPattern)root.GetCurrentPattern(TransformPattern.Pattern);
                if (!transform.Current.CanResize) throw new InvalidOperationException("MainWindow has no UIA resize support.");
                foreach (var (name, width, height) in new[] { ("small", 920d, 620d),
                    ("default", 1100d, 760d), ("large", 1384d, 900d) })
                {
                    await Resize(main, transform, width, height);
                    await Shot(main, "main-normal", name, output, screenshots);
                    await Shot(overlay, "normal-overlay", name, output, screenshots);
                    await Shot(overlay.Stats, "stats-overlay", name, output, screenshots);
                    await Shot(main.CraftWindow, "independent-craft", name, output, screenshots);
                    // Invoke actual MainWindow navigation control through the HWND UIA tree.
                    var inventoryButton = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "main-inventory-navigation"))
                        ?? throw new InvalidOperationException("Main inventory UIA button missing.");
                    await Invoke(main, inventoryButton);
                    var inventoryWindow = app.Windows.OfType<AuxiliaryWindow>().Single(x => x.Pane == AuxiliaryPane.Inventory);
                    actions.Add(new("main-inventory-navigation", "UI Automation InvokePattern", inventoryWindow.IsVisible && main.InventoryPane.IsVisible, null));
                    await Shot(inventoryWindow, "aux-inventory", name, output, screenshots);
                    var profileButton = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "main-profile-navigation"))
                        ?? throw new InvalidOperationException("Main profile UIA button missing.");
                    await Invoke(main, profileButton);
                    var profileWindow = app.Windows.OfType<AuxiliaryWindow>().Single(x => x.Pane == AuxiliaryPane.Profile);
                    actions.Add(new("main-profile-navigation", "UI Automation InvokePattern", profileWindow.IsVisible && main.ProfilePane.IsVisible, null));
                    await Shot(profileWindow, "aux-profile", name, output, screenshots);
                }
                // The detached workspace and unit browser are separate HWNDs: resize each independently.
                await ResizeOwned(main.CraftWindow, 320, 300, "small-craft-resized", output, screenshots, actions);
                await ResizeOwned(main.CraftWindow, 470, 620, "large-craft-resized", output, screenshots, actions);
                await ResizeOwned(overlay, 540, 480, "small-unit-resized", output, screenshots, actions);
                await ResizeOwned(overlay, 760, 620, "large-unit-resized", output, screenshots, actions);
                await ScrollShot(main.CraftWindow, "normal-craft-scroll", "craft-bottom", output, screenshots, actions);
                var ownedInventory = new Dictionary<string, int>(inventory) { ["rawcode:2C0h"] = 1 };
                var ownedFrame = NextRender(main.CraftWindow);
                model.Update(inventory, current: true, generation: 325);
                model.SetStage(NormalCandidateStage.Upper);
                await ownedFrame.WaitAsync(TimeSpan.FromSeconds(10));
                await SelectYujiroCard(main, model, source.Yujiro.UnresolvedConditions);
                var ownedPresentation = NextRender(main.CraftWindow);
                model.Update(ownedInventory, current: true, generation: 325);
                main.NormalBrowserView.Render(); overlay.NormalView.Render();
                await ownedPresentation.WaitAsync(TimeSpan.FromSeconds(10));
                if (model.CurrentInventory.GetValueOrDefault("rawcode:2C0h") != 1 || model.SelectedUnitId != "rawcode:2C0h")
                    throw new InvalidOperationException("Owned Yujiro model presentation missing");
                await Shot(main.CraftWindow, "yujiro-owned-top", "default", output, screenshots);
                await ScrollShot(main.CraftWindow, "normal-craft-scroll", "yujiro-owned-bottom", output, screenshots, actions);
                model.Update(inventory, current: true, generation: 326);
                model.SetStage(NormalCandidateStage.Rare);
                await Resize(overlay, (TransformPattern)AutomationElement.FromHandle(
                    new WindowInteropHelper(overlay).Handle).GetCurrentPattern(TransformPattern.Pattern), 540, 480);
                var unitRoot = AutomationElement.FromHandle(new WindowInteropHelper(overlay).Handle);
                var more = unitRoot.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, "후보 더 보기"))
                    ?? throw new InvalidOperationException("Candidate expansion button missing");
                var candidateViewport = Descendants(overlay.NormalView).OfType<ScrollViewer>().Single(x =>
                    AutomationProperties.GetAutomationId(x) == "normal-candidate-scroll");
                var priorExtent = candidateViewport.ExtentHeight;
                var extentChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ScrollChangedEventHandler onExtent = (_, e) =>
                {
                    if (e.ExtentHeightChange > 0 && candidateViewport.ExtentHeight > priorExtent)
                        extentChanged.TrySetResult();
                };
                candidateViewport.ScrollChanged += onExtent;
                try
                {
                    await Invoke(overlay, more);
                    if (candidateViewport.ExtentHeight > priorExtent) extentChanged.TrySetResult();
                    await extentChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally { candidateViewport.ScrollChanged -= onExtent; }
                await Rendered(overlay);
                actions.Add(new("candidate-expand", "UIA Invoke on candidate More; WPF ScrollChanged subscribed before action and actual extent increase awaited", true, null));
                await ScrollShot(overlay, "normal-candidate-scroll", "candidate-scrolled", output, screenshots, actions,
                    candidateModel: model);
                await CaptureLegacyPatch(main, root, output, screenshots, actions);
                var overlayHandle = new WindowInteropHelper(overlay).Handle;
                var foreground = GetForegroundWindow();
                overlay.SetClickThrough(true);
                var transparent = (GetWindowLong(overlayHandle, -20) & 0x20) != 0;
                overlay.SetClickThrough(false);
                var restored = (GetWindowLong(overlayHandle, -20) & 0x20) == 0;
                actions.Add(new("overlay-click-through-focus", "owned HWND WS_EX_TRANSPARENT toggle and GetForegroundWindow", 
                    transparent && restored && foreground == GetForegroundWindow(), null));
                // Label existing controls for UIA observation; never assign their displayed text.
                System.Windows.Automation.AutomationProperties.SetAutomationId(main.NavigationCombo, "fixture-navigation-option");
                System.Windows.Automation.AutomationProperties.SetAutomationId(main.MainObservationStatus, "fixture-main-status");
                System.Windows.Automation.AutomationProperties.SetAutomationId(main.NavigationCandidateText, "fixture-navigation-summary");
                // Exercise all 15 real category/option ComboBox selection handlers at the default geometry.
                await Resize(main, transform, 1100, 760);
                foreach (var navigation in source.Navigation)
                {
                    var option = MainWindow.ResolveVersionNavigation(catalog, navigation.Id);
                    string? summary = null;
                    var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var textProperty = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
                    EventHandler onSummary = (_, _) =>
                    {
                        if ((main.NavigationCombo.SelectedItem as NavigationOption)?.Id == navigation.Id &&
                            main.NavigationCandidateText.Text.Contains(option.Name, StringComparison.Ordinal))
                        {
                            summary = main.NavigationCandidateText.Text;
                            committed.TrySetResult();
                        }
                    };
                    textProperty.AddValueChanged(main.NavigationCandidateText, onSummary);
                    try
                    {
                        main.NavigationCategoryCombo.SelectedItem = NavigationProfiles.Categories.Single(x => x.Id == navigation.CategoryId);
                        var selectedOption = main.NavigationCombo.Items.OfType<NavigationOption>().Single(x => x.Id == navigation.Id);
                        // Programmatic fixture assignment, not a physical/UIA option click or saved/game choice.
                        main.NavigationCombo.SelectedItem = selectedOption;
                        onSummary(main, EventArgs.Empty);
                        await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    finally { textProperty.RemoveValueChanged(main.NavigationCandidateText, onSummary); }
                    var profileWindow = app.Windows.OfType<AuxiliaryWindow>().Single(x => x.Pane == AuxiliaryPane.Profile);
                    var receipt = await Shot(profileWindow, "navigation-" + navigation.Id, "default", output, screenshots,
                        option.Name, navigationSummary: summary ?? throw new InvalidOperationException("Navigation summary not presented"));
                    var applied = (main.NavigationCombo.SelectedItem as NavigationOption)?.Id == navigation.Id &&
                        profileWindow.IsVisible && !main.AutoNavigationCheck.IsEnabled &&
                        CaptureContract.MatchesNavigationReceipt(option.Name, summary!, receipt.VisibleCaptionBefore,
                            receipt.VisibleCaptionAfter, receipt.NavigationSummaryBefore, receipt.NavigationSummaryAfter);
                    actions.Add(new(navigation.Id, "programmatic ComboBox.SelectedItem; awaited WPF manual summary and auxiliary UIA selected caption before/after native paint; not a user/game/saved choice", applied,
                        receipt.VisibleCaptionAfter));
                    if (!applied) throw new InvalidOperationException("Auxiliary selector/manual summary mismatch: " + navigation.Id);
                }
                await CaptureReferences(main, catalog, inventory, output, screenshots, actions, Observe, AppModel,
                    source.Yujiro.UnresolvedConditions);
                await CaptureConsent(main, Place, work, output, screenshots, actions);
                await CapturePendingUpdates(main, Place, work, output, screenshots, actions);
                foreach (var pair in new[] { ("AlliedForces.DoubleBenefit", "AlliedForces.EmergencyCall"),
                    ("PathOfKings.BountyHunter", "PathOfKings.RoyalLoader") })
                {
                    var first = screenshots.Single(x => x.Scenario == "navigation-" + pair.Item1);
                    var second = screenshots.Single(x => x.Scenario == "navigation-" + pair.Item2);
                    if (!CaptureContract.DistinctSelectorFrames(first.VisibleCaptionAfter, second.VisibleCaptionAfter,
                            first.Sha256, second.Sha256))
                        throw new InvalidOperationException("Distinct auxiliary selectors produced identical PNGs: " + pair);
                }
                if (!CaptureContract.ScreenshotScenarios(source).Order(StringComparer.Ordinal).SequenceEqual(
                        screenshots.Select(x => x.File).Order(StringComparer.Ordinal)) ||
                    actions.Any(x => !x.Applied) || !autoDisabled || !mainNormal || !yujiroSelected || !independent || !statsVisible ||
                    screenshots.Select(s => s.Hwnd).Distinct().Count() != 8)
                    throw new InvalidOperationException("One or more fixture action/state checks failed.");
                }
            }
            catch (Exception ex) { error = ex.ToString(); }
            finally
            {
                var ownedHandles = screenshots.Select(s => s.Hwnd).Concat(
                    app.Windows.Cast<Window>().Select(w => new WindowInteropHelper(w).Handle.ToInt64()))
                    .Where(h => h != 0).Distinct().ToArray();
                if (!CaptureContract.CoversScreenshotOwnedHandles(screenshots.Select(s => s.Hwnd), ownedHandles))
                    error ??= "A captured HWND is missing from cleanup metadata.";
                var outOfBounds = screenshots.Where(s => s.ScreenLeft < work.Left || s.ScreenTop < work.Top ||
                    s.ScreenLeft + s.PixelWidth > work.Right || s.ScreenTop + s.PixelHeight > work.Bottom).ToArray();
                if (outOfBounds.Length > 0)
                    error ??= "Captured HWND outside secondary working area: " + string.Join(", ", outOfBounds.Select(s => s.File));
                File.WriteAllText(Path.Combine(output, "placement.json"), JsonSerializer.Serialize(new {
                    displays, selected = secondary.DeviceName, selectedWork = work,
                    effectiveDpi = new { x = dpiX, y = dpiY }, dipScale = new { x = scaleX, y = scaleY },
                    sourceInitialized = placements,
                    screenshots = screenshots.Select(s => new { s.File, s.Hwnd, left = s.ScreenLeft, top = s.ScreenTop,
                        width = s.PixelWidth, height = s.PixelHeight }),
                    allCapturedInsideSecondaryWork = outOfBounds.Length == 0
                }, new JsonSerializerOptions { WriteIndented = true }));
                main.Close();
                var closed = app.Windows.Count == 0;
                var report = new CaptureReport(fixturePid, true,
                    "Packaged App.OnStartup uses Production(AppPaths.UserDataDirectory); no packaged CLI/profile override selects an isolated root, so launching it would touch the user profile and single-instance IPC.",
                    source?.FixtureMapVersion ?? "", hwnd, execution.RuntimeEnabled,
                    CaptureContract.FixtureInventory(), source ?? CaptureContract.Inspect(_fixtureVersion), screenshots.ToArray(), actions.ToArray(), autoDisabled,
                    mainNormal, yujiroSelected, independent, statsVisible,
                    source is not null && CaptureContract.ScreenshotScenarios(source).Order(StringComparer.Ordinal).SequenceEqual(
                        screenshots.Select(x => x.File).Order(StringComparer.Ordinal)), closed, error);
                var json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new {
                    fixturePid, synthetic = true, fixtureInventory = report.FixtureInventory, source, expectedScreenshots = CaptureContract.ScreenshotScenarios(report.Source),
                    screenshotInventoryComplete = report.ScreenshotInventoryComplete, error }, json));
                File.WriteAllText(Path.Combine(output, "action-log.json"), JsonSerializer.Serialize(actions, json));
                File.WriteAllText(Path.Combine(output, "model-identity.json"), JsonSerializer.Serialize(identity, json));
                File.WriteAllText(Path.Combine(output, "cleanup.json"), JsonSerializer.Serialize(new {
                    fixturePid, ownedHwnds = ownedHandles, screenshotOwnedHwnds = screenshots.Select(s => s.Hwnd).Distinct().ToArray(),
                    ownedWindowsClosed = closed, hwndsDestroyedAtFixtureCleanup = ownedHandles.All(h => !IsWindow((IntPtr)h)),
                    synthetic = true, processExitNotYetObserved = true }, json));
                File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, json));
                Console.WriteLine(JsonSerializer.Serialize(new { success = error is null && closed, report = Path.Combine(output, "report.json"), error }));
                app.Shutdown(error is null && closed ? 0 : 1);
            }
        }), DispatcherPriority.Normal);
        return app.Run(main);
    }

    private static void ClampOwned(Window window, System.Drawing.Rectangle work, int left, int top,
        List<object> placements)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var initial)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var width = initial.Right - initial.Left; var height = initial.Bottom - initial.Top;
        if (width > work.Width || height > work.Height)
            throw new InvalidOperationException("Owned window exceeds secondary working area: " + window.GetType().Name);
        var x = Math.Clamp(left, work.Left, work.Right - width);
        var y = Math.Clamp(top, work.Top, work.Bottom - height);
        if (!SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Secondary placement failed");
        if (!GetWindowRect(handle, out var actual) || actual.Left < work.Left || actual.Top < work.Top ||
            actual.Right > work.Right || actual.Bottom > work.Bottom)
            throw new InvalidOperationException("Owned HWND did not land inside secondary working area");
        placements.Add(new { window = window.GetType().Name, hwnd = handle.ToInt64(),
            dpi = GetDpiForWindow(handle), requestedPhysical = new { left, top },
            initialPhysical = new { initial.Left, initial.Top, initial.Right, initial.Bottom },
            placedPhysical = new { actual.Left, actual.Top, actual.Right, actual.Bottom } });
    }

    private static NormalCandidateBrowser? GetBoundModel(NormalCandidateView view) =>
        (NormalCandidateBrowser?)typeof(NormalCandidateView).GetField("_model",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view);

    private static async Task<bool> SelectYujiroCard(MainWindow main, NormalCandidateBrowser model, string[] unresolvedConditions)
    {
        if (model.Stage != NormalCandidateStage.Upper ||
            !model.Snapshot.Groups.Any(g => g.Name == "상위" && g.Candidates.Any(c => c.Unit.Id == "rawcode:2C0h")))
            throw new InvalidOperationException("Yujiro is not in the ordinary Upper candidate group");
        await Rendered(main);
        var root = AutomationElement.FromHandle(new WindowInteropHelper(main).Handle);
        AutomationElement? card = null;
        // Each expansion materializes another page; never select a catalog-only ID.
        for (var page = 0; page <= model.Snapshot.Groups.Single(g => g.Name == "상위").Candidates.Count / 10 + 1; page++)
        {
            card = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "normal-candidate-rawcode:2C0h"));
            if (card is not null) break;
            var more = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "후보 더 보기"))
                ?? throw new InvalidOperationException("Yujiro candidate card not materialized and expansion unavailable");
            await Invoke(main, more);
        }
        if (card is not null && card.Current.IsOffscreen && card.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scrollItem))
        {
            var render = NextRender(main);
            await Task.Run(() => ((ScrollItemPattern)scrollItem).ScrollIntoView()).WaitAsync(TimeSpan.FromSeconds(10));
            await render.WaitAsync(TimeSpan.FromSeconds(10));
            await Rendered(main);
        }
        if (card is null || card.Current.IsOffscreen ||
            !card.TryGetCurrentPattern(InvokePattern.Pattern, out _))
            throw new InvalidOperationException("Yujiro materialized visible UIA InvokePattern card missing");
        var id = card.Current.AutomationId;
        var selected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSelected() { if (model.SelectedUnitId == "rawcode:2C0h") selected.TrySetResult(); }
        model.PresentationChanged += OnSelected;
        try
        {
            await Invoke(main, card);
            await selected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { model.PresentationChanged -= OnSelected; }
        var proven = CaptureContract.MatchesYujiroInvoke(id, true, true, model.SelectedUnitId, unresolvedConditions);
        if (!proven) throw new InvalidOperationException("Yujiro card UIA invocation did not select Yujiro with unresolved recipe conditions");
        return proven;
    }

    private static async Task ResizeOwned(Window window, double width, double height, string scenario,
        string output, List<ScreenshotCheck> screenshots, List<ActionCheck> actions)
    {
        var root = AutomationElement.FromHandle(new WindowInteropHelper(window).Handle);
        if (!root.TryGetCurrentPattern(TransformPattern.Pattern, out var raw) ||
            !((TransformPattern)raw).Current.CanResize)
            throw new InvalidOperationException(scenario + " independent HWND cannot resize");
        await Resize(window, (TransformPattern)raw, width, height);
        actions.Add(new(scenario, "UI Automation TransformPattern.Resize independent HWND", true, null));
        await Shot(window, scenario, "default", output, screenshots);
    }

    private static async Task ScrollShot(Window window, string automationId, string scenario,
        string output, List<ScreenshotCheck> screenshots, List<ActionCheck> actions,
        NormalCandidateBrowser? candidateModel = null)
    {
        var root = AutomationElement.FromHandle(new WindowInteropHelper(window).Handle);
        var scroll = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId))
            ?? throw new InvalidOperationException("Scroll viewport missing: " + automationId);
        var pattern = (ScrollPattern)scroll.GetCurrentPattern(ScrollPattern.Pattern);
        if (candidateModel is not null)
        {
            var view = ((OverlayWindow)window).NormalView;
            var viewport = Descendants(view).OfType<ScrollViewer>().Single(x =>
                AutomationProperties.GetAutomationId(x) == automationId);
            var materialized = Descendants(view).OfType<Button>().Where(x =>
                AutomationProperties.GetAutomationId(x).StartsWith("normal-candidate-", StringComparison.Ordinal)).ToArray();
            var more = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "후보 더 보기"))
                .Cast<AutomationElement>().Where(x =>
                    TreeWalker.RawViewWalker.GetParent(x)?.Current.AutomationId == "overlay-category-희귀함").ToArray();
            var candidateState = new {
                modelStage = candidateModel.Stage.ToString(), boundToView = ReferenceEquals(GetBoundModel(view), candidateModel),
                rareCandidates = candidateModel.Snapshot.Groups.Single(g => g.Name == "희귀함").Candidates.Count,
                selectedId = candidateModel.SelectedUnitId,
                materializedCards = materialized.Length, visibleCards = materialized.Count(x => x.IsVisible),
                moreButtons = more.Select(x => new { x.Current.Name, x.Current.AutomationId,
                    parentId = TreeWalker.RawViewWalker.GetParent(x)?.Current.AutomationId, x.Current.IsOffscreen }).ToArray(),
                wpf = new { viewport.IsVisible, viewport.ActualHeight, viewport.ExtentHeight,
                    viewport.ViewportHeight, viewport.ScrollableHeight, viewport.VerticalOffset },
                uia = new { scroll.Current.AutomationId, scroll.Current.Name, scroll.Current.IsOffscreen,
                    controlType = scroll.Current.ControlType.ProgrammaticName,
                    bounds = scroll.Current.BoundingRectangle.ToString(), pattern.Current.VerticallyScrollable,
                    pattern.Current.VerticalScrollPercent }
            };
            File.WriteAllText(Path.Combine(output, "candidate-scroll-state.json"), JsonSerializer.Serialize(candidateState,
                new JsonSerializerOptions { WriteIndented = true }));
            if (!candidateState.boundToView || candidateState.modelStage != nameof(NormalCandidateStage.Rare) ||
                !viewport.IsVisible || viewport.ActualHeight <= 0 || scroll.Current.IsOffscreen ||
                more.Length != 1 || more[0].Current.IsOffscreen || materialized.Length != 10)
                throw new InvalidOperationException("Candidate target/state invalid; inspect candidate-scroll-state.json");
            if (!pattern.Current.VerticallyScrollable && viewport.ScrollableHeight <= 0 &&
                viewport.ExtentHeight <= viewport.ViewportHeight &&
                materialized.All(x => x.IsVisible))
            {
                actions.Add(new(automationId,
                    "NoScrollNeeded: all ten materialized Rare cards and the More button visible; WPF extent fits viewport; no UIA scroll invoked",
                    true, null));
                await Shot(window, scenario, "default", output, screenshots);
                return;
            }
        }
        if (!pattern.Current.VerticallyScrollable)
            throw new InvalidOperationException("UIA scroll target not scrollable with overflow or invalid WPF state: " + automationId);
        var render = NextRender(window);
        await Task.Run(() => pattern.SetScrollPercent(ScrollPattern.NoScroll, 100)).WaitAsync(TimeSpan.FromSeconds(10));
        await render.WaitAsync(TimeSpan.FromSeconds(10));
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var applied = ((ScrollPattern)scroll.GetCurrentPattern(ScrollPattern.Pattern)).Current.VerticalScrollPercent >= 99;
        actions.Add(new(automationId, "UI Automation ScrollPattern.SetScrollPercent(100)", applied, null));
        if (!applied) throw new InvalidOperationException("Scroll did not reach bottom: " + automationId);
        await Shot(window, scenario, "default", output, screenshots);
    }

    private static async Task CaptureLegacyPatch(MainWindow main, AutomationElement root,
        string output, List<ScreenshotCheck> screenshots, List<ActionCheck> actions)
    {
        var button = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "map-2320-patch"))
            ?? throw new InvalidOperationException("Historical patch UI button missing");
        var opened = new TaskCompletionSource<Map2320PatchWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? onLoaded = null;
        onLoaded = (sender, _) =>
        {
            if (sender is Map2320PatchWindow patch) opened.TrySetResult(patch);
        };
        EventManager.RegisterClassHandler(typeof(Map2320PatchWindow), FrameworkElement.LoadedEvent, onLoaded);
        var invocation = Task.Run(() => ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke());
        var dialog = await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Shot(dialog, "historical-2320", "default", output, screenshots);
            var expander = Descendants(dialog).OfType<Expander>().Single(x =>
                AutomationProperties.GetAutomationId(x) == "map-2320-evidence");
            var expanded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler onExpanded = (_, _) => expanded.TrySetResult();
            expander.Expanded += onExpanded;
            try
            {
                var evidence = AutomationElement.FromHandle(new WindowInteropHelper(dialog).Handle)
                    .FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "map-2320-evidence"))
                    ?? throw new InvalidOperationException("Historical help expander UIA missing");
                if (evidence.Current.IsOffscreen && evidence.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var item))
                    await Task.Run(() => ((ScrollItemPattern)item).ScrollIntoView()).WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Run(() => ((ExpandCollapsePattern)evidence.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand())
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await expanded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { expander.Expanded -= onExpanded; }
            await Shot(dialog, "historical-2320-expanded", "default", output, screenshots);
            var viewport = Descendants(dialog).OfType<ScrollViewer>().Single(x =>
                AutomationProperties.GetName(x) == "2.320 주요 변경점");
            var scrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ScrollChangedEventHandler onScroll = (_, _) =>
            {
                if (viewport.VerticalOffset >= viewport.ScrollableHeight && viewport.ScrollableHeight > 0)
                    scrolled.TrySetResult();
            };
            viewport.ScrollChanged += onScroll;
            try
            {
                viewport.ScrollToEnd();
                await scrolled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { viewport.ScrollChanged -= onScroll; }
            await Shot(dialog, "historical-2320-bottom", "default", output, screenshots);
            actions.Add(new("map-2320-patch", "UI Automation InvokePattern and historical help UIA ExpandCollapsePattern", dialog.IsVisible, null));
        }
        finally { dialog.Close(); await invocation.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    private static async Task CapturePendingUpdates(MainWindow main, Action<Window, int, int> place,
        System.Drawing.Rectangle work, string output,
        List<ScreenshotCheck> screenshots, List<ActionCheck> actions)
    {
        var notice = new PendingUpdateNotice();
        var host = new Window { Owner = main, Title = "랜디픽 업데이트 알림", Content = notice,
            Width = 460, Height = 135, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Background = RandyPickTheme.Canvas };
        place(host, work.Left + 1500, work.Top + 170);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ContentRendered += (_, _) => loaded.TrySetResult();
        try
        {
            host.Show();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var caption = (TextBlock)typeof(PendingUpdateNotice)
                .GetField("_caption", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(notice)!;
            var property = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            foreach (var (scenario, ready, busy) in new[] { ("update-deferred", false, false),
                ("update-ready", true, false), ("update-busy", true, true) })
            {
                var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler onChanged = (_, _) => changed.TrySetResult();
                property.AddValueChanged(caption, onChanged);
                try
                {
                    notice.Present("1.0.16-test.2", ready, busy);
                    await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally { property.RemoveValueChanged(caption, onChanged); }
                await Shot(host, scenario, "default", output, screenshots);
                actions.Add(new(scenario, "synthetic PendingUpdateNotice.Present; bound WPF caption change before action; no update network/install", notice.IsVisible, null));
            }
        }
        finally { host.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static async Task CaptureConsent(MainWindow main, Action<Window, int, int> place,
        System.Drawing.Rectangle work, string output, List<ScreenshotCheck> screenshots, List<ActionCheck> actions)
    {
        var dialog = new TelemetryConsentWindow { Owner = main, ShowActivated = false };
        place(dialog, work.Left + 910, work.Top + 180);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.ContentRendered += (_, _) => loaded.TrySetResult();
        dialog.Closed += (_, _) => closed.TrySetResult();
        // ShowDialog is intentionally isolated: UIA decline closes only this synthetic dialog.
        var modal = dialog.Dispatcher.BeginInvoke(new Action(() => dialog.ShowDialog()));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Shot(dialog, "consent-top", "default", output, screenshots);
            var root = AutomationElement.FromHandle(new WindowInteropHelper(dialog).Handle);
            var notice = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "consent-notice"))
                ?? throw new InvalidOperationException("Consent notice UIA scroll missing");
            var scroll = (ScrollPattern)notice.GetCurrentPattern(ScrollPattern.Pattern);
            var changed = NextRender(dialog);
            await Task.Run(() => scroll.SetScrollPercent(ScrollPattern.NoScroll, 100)).WaitAsync(TimeSpan.FromSeconds(10));
            await changed.WaitAsync(TimeSpan.FromSeconds(10));
            await Rendered(dialog);
            await Shot(dialog, "consent-bottom", "default", output, screenshots);
            var consentPercent = ((ScrollPattern)notice.GetCurrentPattern(ScrollPattern.Pattern)).Current.VerticalScrollPercent;
            if (consentPercent < 99)
                throw new InvalidOperationException("Consent notice did not scroll to privacy/decline section: " + consentPercent);
            var decline = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "consent-decline"))
                ?? throw new InvalidOperationException("Consent decline UIA button missing");
            await Shot(dialog, "consent-decline", "default", output, screenshots);
            await Task.Run(() => ((InvokePattern)decline.GetCurrentPattern(InvokePattern.Pattern)).Invoke())
                .WaitAsync(TimeSpan.FromSeconds(10));
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await modal.Task.WaitAsync(TimeSpan.FromSeconds(10));
            actions.Add(new("consent-decline", "synthetic modal UIA decline; no consent receipt, app settings or transport", dialog.DialogResult == false, null));
        }
        finally { if (dialog.IsVisible) dialog.Close(); }
    }

    private static async Task CaptureReferences(MainWindow main, DataCatalog catalog,
        Dictionary<string, int> inventory, string output, List<ScreenshotCheck> screenshots, List<ActionCheck> actions,
        Action<string> observe, Func<NormalCandidateBrowser?> appModel, string[] unresolvedConditions)
    {
        var entries = inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var context = new string('A', 64);
        var diagnosticsBasic = new RecognitionDiagnostics { Source = DiagnosticBasicInventoryObservation.SourceName,
            ProcessVersion = Warcraft300Diagnostic.Version, ExecutableSha256 = Warcraft300Diagnostic.Hash };
        var diagnosticsFull = new RecognitionDiagnostics { Source = DiagnosticInventoryObservation.SourceName,
            ProcessVersion = Warcraft300Diagnostic.Version, ExecutableSha256 = Warcraft300Diagnostic.Hash };
        async Task Present(string state, DiagnosticBasicInventoryObservation? basic,
            RecognitionResult result, bool expectedCurrent)
        {
            async IAsyncEnumerable<DiagnosticRecognitionFrame> Frames()
            {
                if (basic is not null) yield return DiagnosticRecognitionFrame.ForBasic(basic, diagnosticsBasic);
                yield return DiagnosticRecognitionFrame.ForCompleted(result);
                await Task.CompletedTask;
            }
            var render = NextRender(main);
            var textProperty = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            EventHandler onBinding = (_, _) => observe("inside-actual-status-binding-" + state);
            textProperty.AddValueChanged(main.MainObservationStatus, onBinding);
            try { await main.ScanControlledFramesAsync(Frames()).WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { textProperty.RemoveValueChanged(main.MainObservationStatus, onBinding); }
            observe("after-controlled-callback-" + state);
            await render.WaitAsync(TimeSpan.FromSeconds(10));
            var proof = main.CaptureDiagnosticInventoryUiProof();
            var expectedStatus = proof.RecognitionStatus;
            var applied = proof.ReferenceCurrent == expectedCurrent && proof.RecognitionStatus == expectedStatus &&
                (expectedCurrent ? proof.ObservedCount == entries.Sum(x => x.Count) + (state == "basic" ? 1 : 0) : proof.ObservedCount is null);
            if (!applied) throw new InvalidOperationException("Reference presentation mismatch: " + state + " / " + proof.RecognitionStatus);
            var revision = state switch { "basic" => 2, "full" => 1, "wrong-fingerprint" => 3,
                "expired" => 4, _ => 0 };
            var receipt = await Shot(main, "reference-" + state, "default", output, screenshots,
                expectedStatus, revision);
            actions.Add(new("reference-" + state, "controlled diagnostic frame; MainWindow bound status and UIA caption before/after native paint",
                CaptureContract.MatchesCapture(expectedStatus, receipt.BoundStatus, receipt.VisibleCaptionBefore,
                    receipt.VisibleCaptionAfter, receipt.Sha256, receipt.Hwnd, receipt.PaintedHwnd,
                    receipt.PaintBoundary, receipt.CaptureBoundary), receipt.VisibleCaptionAfter));
        }
        RecognitionResult Completed(DiagnosticInventoryObservation? observation, RecognitionState state = RecognitionState.Ready) =>
            new() { State = state, DiagnosticObservation = observation, Diagnostics = diagnosticsFull };
        DiagnosticInventoryObservation Full(long revision, DateTimeOffset started, DateTimeOffset completed) =>
            DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                context, revision, 0, started, completed, completed - started, entries, [], observedRound: 40,
                worldStampFingerprint: new string('B', 64), bindingContextId: new string('C', 64));
        var now = DateTimeOffset.UtcNow;
        var basic = DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, context, 1, 0, now.AddMilliseconds(-100), now,
            TimeSpan.FromMilliseconds(100), entries, new string('B', 64), new string('C', 64));
        // A bound FULL frame seeds the round; the changed BASIC-only count proves the fast lane.
        await Present("full", basic, Completed(Full(1, now.AddMilliseconds(-100), now)), true);
        if (appModel() is { } diagnostic)
            diagnostic.PresentationChanged += () => observe("inside-actual-diagnostic-callback-after-app-handler");
        observe("after-full-callback-model-binding");
        now = DateTimeOffset.UtcNow;
        var changedEntries = entries.Select(e => new InventoryEntry { UnitId = e.UnitId,
            Count = e.UnitId == "rawcode:I10h" ? e.Count + 1 : e.Count }).ToArray();
        var changedBasic = DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, context, 2, 0, now.AddMilliseconds(-100), now,
            TimeSpan.FromMilliseconds(100), changedEntries, new string('D', 64), new string('C', 64));
        await Present("basic", changedBasic, new RecognitionResult { State = RecognitionState.TransientReadError,
            Diagnostics = new RecognitionDiagnostics { Source = "fixture-full-unavailable" } }, true);
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
        await Shot(overlay.Stats, "reference-stats", "default", output, screenshots);
        var root = AutomationElement.FromHandle(new WindowInteropHelper(main).Handle);
        var inventoryButton = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "main-inventory-navigation"))
            ?? throw new InvalidOperationException("Inventory navigation missing during reference replay");
        await Invoke(main, inventoryButton);
        var inventoryWindow = Application.Current.Windows.OfType<AuxiliaryWindow>().Single(x => x.Pane == AuxiliaryPane.Inventory);
        actions.Add(new("reference-inventory-navigation", "UI Automation InvokePattern on MainWindow inventory", inventoryWindow.IsVisible, null));
        await Shot(inventoryWindow, "reference-inventory", "default", output, screenshots);
        // The diagnostic callback owns both views. Publish an accepted empty reference through
        // the same controlled frame path, then select its real card, never the old fixture browser.
        now = DateTimeOffset.UtcNow;
        var empty = DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, context, 5, 0, now.AddMilliseconds(-100), now,
            TimeSpan.FromMilliseconds(100), [], [], observedRound: 40,
            worldStampFingerprint: new string('E', 64), bindingContextId: new string('C', 64));
        var emptyBasic = DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, context, 5, 0, now.AddMilliseconds(-100), now,
            TimeSpan.FromMilliseconds(100), [], new string('E', 64), new string('C', 64));
        async IAsyncEnumerable<DiagnosticRecognitionFrame> EmptyFrames()
        {
            yield return DiagnosticRecognitionFrame.ForBasic(emptyBasic, diagnosticsBasic);
            yield return DiagnosticRecognitionFrame.ForCompleted(Completed(empty));
            await Task.CompletedTask;
        }
        var statusProperty = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        var bound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler onBound = (_, _) =>
        {
            observe("inside-empty-reference-binding");
            if (ReferenceEquals(appModel(), GetBoundModel(main.NormalBrowserView)) &&
                ReferenceEquals(appModel(), GetBoundModel(overlay.NormalView)) &&
                appModel()?.CurrentInventory.Count == 0 && appModel()?.Snapshot.IsCurrent == true)
                bound.TrySetResult();
        };
        statusProperty.AddValueChanged(main.MainObservationStatus, onBound);
        try
        {
            await main.ScanControlledFramesAsync(EmptyFrames()).WaitAsync(TimeSpan.FromSeconds(10));
            onBound(main, EventArgs.Empty);
            await bound.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { statusProperty.RemoveValueChanged(main.MainObservationStatus, onBound); }
        var owned = appModel() ?? throw new InvalidOperationException("Diagnostic browser not created");
        owned.SetStage(NormalCandidateStage.Upper);
        if (!await SelectYujiroCard(main, owned, unresolvedConditions))
            throw new InvalidOperationException("App-owned empty Yujiro selection failed");
        observe("empty-selected-before-native-shot");
        await Rendered(main.CraftWindow);
        observe("empty-before-native-shot");
        await Shot(main.CraftWindow, "yujiro-empty-top", "default", output, screenshots);
        observe("empty-after-native-shot");
        await ScrollShot(main.CraftWindow, "normal-craft-scroll", "yujiro-empty-bottom", output, screenshots, actions);
        actions.Add(new("yujiro-empty-inventory", "accepted empty reference; real UIA Invoke on app-owned browser after binding callback", true, owned.SelectedUnitId));
        // A typed rejected observation cannot be passed off as accepted current-view evidence.
        now = DateTimeOffset.UtcNow;
        var wrong = DiagnosticInventoryObservation.Unavailable(catalog.MapVersion, new string('F', 64),
            Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, 3, now.AddMilliseconds(-100), now,
            TimeSpan.FromMilliseconds(100), "Wrong dataset fingerprint");
        await Present("wrong-fingerprint", null, Completed(wrong), false);
        await Present("missing-archive", null, new RecognitionResult { State = RecognitionState.Waiting,
            ConfirmsSessionBoundary = true, Diagnostics = new RecognitionDiagnostics { Source = "archive-not-proven" } }, false);
        now = DateTimeOffset.UtcNow;
        await Present("expired", null, Completed(Full(4, now.AddSeconds(-4), now.AddSeconds(-3.9))), false);
    }

    private static async Task Invoke(Window window, AutomationElement button)
    {
        var rendered = NextRender(window);
        await Task.Run(() => ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke())
            .WaitAsync(TimeSpan.FromSeconds(10));
        await rendered.WaitAsync(TimeSpan.FromSeconds(10));
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private static async Task Resize(Window window, TransformPattern transform, double width, double height)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SizeChangedEventHandler handler = (_, _) =>
        {
            if (Math.Abs(window.ActualWidth - width) < 2 && Math.Abs(window.ActualHeight - height) < 2)
                changed.TrySetResult();
        };
        window.SizeChanged += handler;
        try
        {
            await Task.Run(() => transform.Resize(width, height)).WaitAsync(TimeSpan.FromSeconds(10));
            if (Math.Abs(window.ActualWidth - width) >= 2 || Math.Abs(window.ActualHeight - height) >= 2)
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Rendered(window);
        }
        finally { window.SizeChanged -= handler; }
    }

    private static Task NextRender(Window window)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? handler = null;
        handler = (_, _) => { CompositionTarget.Rendering -= handler; signal.TrySetResult(); };
        CompositionTarget.Rendering += handler;
        window.InvalidateVisual();
        return signal.Task;
    }
    private static async Task Rendered(Window window)
    {
        // A Rendering callback begins a frame; ContextIdle is reached only after its render work.
        var frame = NextRender(window);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await frame.WaitAsync(TimeSpan.FromSeconds(10));
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private static Task<string> WindowCaption(IntPtr hwnd) =>
        Task.Run(() => AutomationElement.FromHandle(hwnd).Current.Name).WaitAsync(TimeSpan.FromSeconds(10));

    private static async Task<string> VisibleCaption(IntPtr hwnd, string? selector)
    {
        return await Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(hwnd);
            var element = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty,
                    selector is null ? "fixture-main-status" : "fixture-navigation-option"))
                ?? throw new InvalidOperationException("Visible UIA caption control missing: " + selector);
            if (element.Current.IsOffscreen) throw new InvalidOperationException("UIA caption is offscreen");
            if (selector is null) return element.Current.Name;
            var choices = ((SelectionPattern)element.GetCurrentPattern(SelectionPattern.Pattern)).Current.GetSelection();
            if (choices.Length != 1)
                throw new InvalidOperationException("Auxiliary option has no UIA selection");
            // WPF virtualizes closed ComboBox items: the selected item's IsOffscreen
            // property can throw, while the owning ComboBox is visibly on screen.
            // The WPF UIA selection exposes the record's ToString(), whereas
            // DisplayMemberPath=Name draws its Name property in the closed selector.
            var selected = choices[0].Current.Name;
            if (!selected.StartsWith("NavigationOption { Id = " + selector + ", ", StringComparison.Ordinal))
                throw new InvalidOperationException("Auxiliary UIA selected a different option: " + selected);
            const string marker = ", Name = ";
            var start = selected.IndexOf(marker, StringComparison.Ordinal);
            var end = start < 0 ? -1 : selected.IndexOf(", TopUnitLimit = ", start, StringComparison.Ordinal);
            if (end < 0) throw new InvalidOperationException("Auxiliary option UIA name unavailable: " + selected);
            return selected[(start + marker.Length)..end];
        }).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task<ScreenshotCheck> Shot(Window window, string scenario, string size, string output,
        List<ScreenshotCheck> inventory, string? expected = null, long revision = 0, string? navigationSummary = null)
    {
        await Rendered(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (!window.IsVisible || handle == IntPtr.Zero) throw new InvalidOperationException($"{scenario} HWND is not visible");
        var selector = scenario.StartsWith("navigation-", StringComparison.Ordinal) ? scenario["navigation-".Length..] : null;
        var before = expected is null ? await WindowCaption(handle) : await VisibleCaption(handle, selector);
        var craftBefore = scenario == "yujiro-empty-top" ? await CraftUiNames(handle) : null;
        var summaryBefore = navigationSummary is null ? "" : await NavigationSummary((MainWindow)Application.Current.MainWindow);
        if (navigationSummary is not null && summaryBefore != navigationSummary)
            throw new InvalidOperationException($"{scenario} stale manual summary before capture: {summaryBefore}");
        if (expected is not null && before != expected)
            throw new InvalidOperationException($"{scenario} UIA before capture: expected '{expected}', got '{before}'");
        // Native validation runs on a worker so WM_PRINT can be serviced by the WPF dispatcher.
        var paintBoundary = Stopwatch.GetTimestamp();
        var file = size + "-" + scenario + ".png";
        var path = Path.Combine(output, file);
        if (!GetWindowRect(handle, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            throw new InvalidOperationException("Invalid owned HWND bounds: " + scenario);
        var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        if (rect.Left < _secondaryWork.Left || rect.Top < _secondaryWork.Top ||
            rect.Right > _secondaryWork.Right || rect.Bottom > _secondaryWork.Bottom)
            throw new InvalidOperationException("Capture-owned HWND outside secondary work area: " + scenario);
        var native = await Task.Run(() =>
        {
            if (!RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0100 | 0x0400))
                throw new InvalidOperationException("Native HWND redraw failed: " + scenario);
            var dwm = DwmFlush();
            if (dwm < 0) throw new InvalidOperationException("DwmFlush failed: " + dwm);
            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            var dc = graphics.GetHdc();
            bool printed;
            try { printed = PrintWindow(handle, dc, 2); }
            finally { graphics.ReleaseHdc(dc); }
            if (!printed) throw new InvalidOperationException("PrintWindow failed: " + scenario);
            bitmap.Save(path, ImageFormat.Png);
            return Stopwatch.GetTimestamp();
        }).WaitAsync(TimeSpan.FromSeconds(15));
        var after = expected is null ? await WindowCaption(handle) : await VisibleCaption(handle, selector);
        if (craftBefore is not null)
        {
            var craftAfter = await CraftUiNames(handle);
            File.WriteAllText(Path.Combine(output, "yujiro-empty-top-uia.json"), JsonSerializer.Serialize(new {
                before = craftBefore, after = craftAfter }, new JsonSerializerOptions { WriteIndented = true }));
            if (!craftBefore.Any(x => x.Contains("한마 유지로", StringComparison.Ordinal)) ||
                !craftAfter.Any(x => x.Contains("한마 유지로", StringComparison.Ordinal)) ||
                !craftBefore.Any(x => x.Contains("보유 0", StringComparison.Ordinal)) ||
                !craftAfter.Any(x => x.Contains("보유 0", StringComparison.Ordinal)))
                throw new InvalidOperationException("Empty Yujiro title/missing strip not visible in UIA before and after native paint");
        }
        var summaryAfter = navigationSummary is null ? "" : await NavigationSummary((MainWindow)Application.Current.MainWindow);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        using var signature = File.OpenRead(path);
        var bytes = new byte[8];
        var valid = signature.Read(bytes, 0, bytes.Length) == 8 &&
            bytes.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var mainWindow = window as MainWindow ?? Application.Current.MainWindow as MainWindow
            ?? throw new InvalidOperationException("MainWindow missing at capture boundary");
        var actualBound = selector is not null
            ? (mainWindow.NavigationCombo.SelectedItem as NavigationOption)?.Name ?? ""
            : mainWindow.MainObservationStatus.Text;
        var receipt = new ScreenshotCheck(scenario, size, window.GetType().Name, file, handle.ToInt64(),
            width, height, "Dispatcher.Render + Rendering + ContextIdle; RedrawWindow + DwmFlush + PrintWindow(PW_RENDERFULLCONTENT)",
            valid, rect.Left, rect.Top, expected ?? scenario, revision, actualBound, before, after, hash,
            handle.ToInt64(), paintBoundary, native, DateTimeOffset.UtcNow, "Fixture/" + _fixtureVersion + "/non-runtime", summaryBefore, summaryAfter);
        inventory.Add(receipt);
        if (size == "default")
        {
            var accessible = await Task.Run(() =>
            {
                var root = AutomationElement.FromHandle(handle);
                return root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition)
                    .Cast<AutomationElement>().Select(element =>
                    {
                        var parent = TreeWalker.RawViewWalker.GetParent(element);
                        return new {
                            id = element.Current.AutomationId, name = element.Current.Name,
                            controlType = element.Current.ControlType.ProgrammaticName,
                            parentId = parent?.Current.AutomationId ?? "",
                            parentName = parent?.Current.Name ?? "",
                            parentControlType = parent?.Current.ControlType.ProgrammaticName ?? "",
                            helpText = element.Current.HelpText, itemStatus = element.Current.ItemStatus,
                            offscreen = element.Current.IsOffscreen
                        };
                    }).Where(element => element.id.Length > 0 || element.name.Length > 0 ||
                        element.helpText.Length > 0 || element.itemStatus.Length > 0).ToArray();
            }).WaitAsync(TimeSpan.FromSeconds(10));
            var tooltips = Descendants(window).OfType<FrameworkElement>()
                .Where(element => element.ToolTip is not null)
                .Select(element => new { id = AutomationProperties.GetAutomationId(element),
                    name = AutomationProperties.GetName(element), tooltip = element.ToolTip?.ToString() }).ToArray();
            File.WriteAllText(Path.Combine(output, file + ".uia.json"), JsonSerializer.Serialize(new {
                hwnd = handle.ToInt64(), accessible, tooltips }, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (!valid || expected is not null &&
            !CaptureContract.MatchesCapture(expected, actualBound, before, after, hash,
                receipt.Hwnd, receipt.PaintedHwnd, paintBoundary, native) ||
            navigationSummary is not null && !CaptureContract.MatchesNavigationReceipt(expected!, navigationSummary,
                before, after, summaryBefore, summaryAfter))
            throw new InvalidOperationException("Capture state/paint receipt failed: " + file + " / " + after);
        return receipt;
    }
    private static Task<string[]> CraftUiNames(IntPtr handle) => Task.Run(() =>
    {
        var root = AutomationElement.FromHandle(handle);
        return root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition).Cast<AutomationElement>()
            .Where(element => !element.Current.IsOffscreen)
            .Select(element => element.Current.Name).Where(name => !string.IsNullOrEmpty(name)).ToArray();
    }).WaitAsync(TimeSpan.FromSeconds(10));

    private static Task<string> NavigationSummary(MainWindow main)
    {
        var handle = new WindowInteropHelper(main).Handle;
        return Task.Run(() =>
    {
        var root = AutomationElement.FromHandle(handle);
        var summary = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "fixture-navigation-summary"))
            ?? throw new InvalidOperationException("Manual-plan summary UIA text missing");
        if (summary.Current.IsOffscreen) throw new InvalidOperationException("Manual-plan summary is offscreen");
        return summary.Current.Name;
    }).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint x, out uint y);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hwnd, IntPtr update, IntPtr region, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
}
