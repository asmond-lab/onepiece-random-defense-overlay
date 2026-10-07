using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrandOverlay;

internal static class FirstLegendPresentationCapture
{
    internal static int Run(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: --first-legend-presentation <outputdir>");
            return 2;
        }
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var files = new List<string>();
        var craftWindowOpened = false;
        var app = new App { Execution = OverlayExecutionContext.Fixture(new AppSettings
        {
            Mode = PlayMode.Normal,
            AutoScanEnabled = true,
            AutoUpdateEnabled = false,
            TelemetryEnabled = false,
            ClearDataAutoRefresh = false,
            OverlayDisplayMode = OverlayDisplayMode.Full,
            LastVisibleOverlayDisplayMode = OverlayDisplayMode.Full
        }), ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? main = null;
        OverlayWindow? overlay = null;
        try
        {
            app.InitializeComponent();
            TelemetryConsentStartup.ClearStartupUri(app);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            main = new MainWindow(app.Execution, fixtureMapVersion: "2.321")
            {
                Left = -7000,
                Top = 0,
                Width = 1080,
                Height = 720
            };
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            main.Loaded += (_, _) => loaded.TrySetResult();
            main.Show();
            Pump(loaded.Task);
            overlay = Field<OverlayWindow>(main, "_overlay");
            overlay.Left = -6000;
            overlay.Top = 0;
            overlay.Show();
            overlay.SetNormalBrowserActive(true, PlayMode.Normal, "첫 전설 목적별 후보");

            var catalog = Field<DataCatalog>(main, "_catalog");
            Check(catalog.MapVersion == "2.321", "capture must use bundled 2.321 catalog");
            var model = NormalCandidateBrowser.Create(catalog);
            model.ReferencePresentationIsValid = () => true;
            var normal = (NormalCandidateView)main.FindName("NormalBrowserView");
            normal.SetModel(model);
            overlay.NormalView.SetModel(model);
            var rares = catalog.AllUnits.Where(unit => NormalCandidateBrowser.Tier(unit) is "희귀함" or "희귀")
                .OrderBy(unit => unit.Id, StringComparer.Ordinal).Take(2).ToArray();
            Check(rares.Length == 2, "2.321 catalog needs two rare hand fixtures");
            long revision = 0;
            void Observe(params string[] ids)
            {
                var now = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero).AddSeconds(++revision);
                var observation = DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
                    Warcraft300Diagnostic.Hash, new string('A', 64), revision, 0,
                    now.AddMilliseconds(-10), now, TimeSpan.FromMilliseconds(10),
                    ids.Select(id => new InventoryEntry { UnitId = id, Count = 1 }), [],
                    worldStampFingerprint: new string('C', 64), bindingContextId: new string('B', 64));
                model.UpdateReference(observation, 321);
            }
            Observe();
            Render(main, overlay);
            SelectCategory(normal, "전체 카테고리");
            Folds(overlay.NormalView).Values.First().IsExpanded = false;
            Observe(rares[0].Id);
            overlay.NormalView.Render();
            normal.Render();
            main.UpdateLayout(); overlay.UpdateLayout();
            Check(model.Stage == NormalCandidateStage.Legend, "fixture did not reach first Legend stage");
            Check(model.Snapshot.Groups.Select(group => group.Name).SequenceEqual(["스토리", "공중이동", "가까운 조합"]),
                "final domain groups/order are not ready");
            Check(SelectedCategory(normal) == "스토리", "MAIN did not default to Story");
            var initialFolds = Folds(overlay.NormalView);
            Check(initialFolds["스토리"].IsExpanded && !initialFolds["공중이동"].IsExpanded && !initialFolds["가까운 조합"].IsExpanded,
                "UNIT overlay did not open only Story initially");
            overlay.NormalView.Render();
            var settledFolds = Folds(overlay.NormalView);
            overlay.NormalView.Render();
            var repeatedFolds = Folds(overlay.NormalView);
            Check(settledFolds.All(pair => ReferenceEquals(pair.Value, repeatedFolds[pair.Key])),
                "repeat Render did not settle the UNIT category tree");
            Visuals(overlay.NormalView).OfType<Button>().Single(button => Equals(button.Content, "모두 펼치기"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Folds(overlay.NormalView).Values.All(expander => expander.IsExpanded),
                "stable-cache expand-all did not open all Legend groups");
            overlay.Width = 540; overlay.Height = 480; Render(main, overlay);
            Capture(overlay, "unit-expand-all-stable-cache-540.png");
            FocusOverlayCategory(overlay.NormalView, "스토리");

            foreach (var category in new[] { "스토리", "공중이동", "가까운 조합" })
            {
                SelectCategory(normal, category);
                FocusOverlayCategory(overlay.NormalView, category);
                Render(main, overlay);
                foreach (var size in new[] { (Width: 1080d, Height: 720d, Name: "1080"), (Width: 920d, Height: 620d, Name: "920-min") })
                {
                    main.Width = size.Width; main.Height = size.Height; Render(main, overlay);
                    Capture(main, $"main-{Slug(category)}-{size.Name}.png");
                }
                foreach (var size in new[] { (Width: 540d, Height: 480d, Name: "540"), (Width: 420d, Height: 420d, Name: "420-min") })
                {
                    overlay.Width = size.Width; overlay.Height = size.Height; Render(main, overlay);
                    Capture(overlay, $"unit-{Slug(category)}-{size.Name}.png");
                }
                if (category == "가까운 조합")
                {
                    var candidate = VisibleCandidateButtons(normal).FirstOrDefault();
                    Check(candidate is not null, "populated closest category has no candidate card");
                    candidate!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Render(main, overlay);
                    Check(model.SelectedUnitId is not null && main.CraftWindow.IsVisible, "candidate selection did not open existing craft window");
                    craftWindowOpened = true;
                    Check(Visuals(main.CraftWorkspace).OfType<FrameworkElement>().Any(element =>
                        AutomationProperties.GetAutomationId(element) == "normal-craft-flow"), "craft window has no selected recipe flow");
                    Capture(main.CraftWindow, "craft-selected-from-first-legend.png");
                }
            }

            SelectCategory(normal, "가까운 조합");
            FocusOverlayCategory(overlay.NormalView, "공중이동");
            overlay.Width = 420; overlay.Height = 420;
            Render(main, overlay);
            var scroll = CandidateScroll(overlay.NormalView);
            scroll.ScrollToEnd();
            Render(main, overlay);
            var scrollBefore = scroll.VerticalOffset;
            var foldsBefore = Folds(overlay.NormalView).ToDictionary(pair => pair.Key, pair => pair.Value.IsExpanded);
            Observe(rares[0].Id, rares[1].Id);
            Render(main, overlay);
            var scrollAfterChangedHand = scroll.VerticalOffset;
            Check(SelectedCategory(normal) == "가까운 조합", "changed hand replaced explicit MAIN category");
            Check(Folds(overlay.NormalView).All(pair => pair.Value.IsExpanded == foldsBefore[pair.Key]), "changed hand replaced explicit UNIT folds");
            Check(Math.Abs(scrollAfterChangedHand - scrollBefore) < 1, "changed hand replaced UNIT scroll");
            Capture(main, "main-changed-hand-retained.png");
            Capture(overlay, "unit-changed-hand-retained.png");

            model.InvalidateReference();
            Render(main, overlay);
            Check(!model.Snapshot.IsCurrent && SelectedCategory(normal) == "가까운 조합", "stale update replaced explicit category");
            Check(Folds(overlay.NormalView).All(pair => pair.Value.IsExpanded == foldsBefore[pair.Key]), "stale update replaced explicit folds");
            Capture(main, "main-stale-retained.png");
            Capture(overlay, "unit-stale-retained.png");

            Observe(rares[0].Id);
            normal.SetModel(model);
            overlay.NormalView.SetModel(model);
            Render(main, overlay);

            var emptyModel = new NormalCandidateBrowser(catalog.AllUnits, guideProfile: [])
                { ReferencePresentationIsValid = () => true };
            var emptyNow = new DateTimeOffset(2026, 9, 22, 1, 0, 0, TimeSpan.Zero);
            var emptyObservation = DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
                Warcraft300Diagnostic.Hash, new string('D', 64), 1, 0,
                emptyNow.AddMilliseconds(-10), emptyNow, TimeSpan.FromMilliseconds(10),
                new[] { new InventoryEntry { UnitId = rares[0].Id, Count = 1 } }, [],
                worldStampFingerprint: new string('E', 64), bindingContextId: new string('F', 64));
            emptyModel.UpdateReference(emptyObservation, 1);
            Check(emptyModel.Snapshot.Groups.Select(group => group.Name).SequenceEqual(["스토리", "공중이동", "가까운 조합"]),
                "empty-tag fixture lost stable groups");
            normal.SetModel(emptyModel);
            overlay.NormalView.SetModel(emptyModel);
            SelectCategory(normal, "스토리");
            FocusOverlayCategory(overlay.NormalView, "스토리");
            CandidateScroll(normal).ScrollToHome();
            CandidateScroll(overlay.NormalView).ScrollToHome();
            main.CraftWindow.Hide();
            main.WindowState = WindowState.Normal;
            main.Width = 920; main.Height = 620; overlay.Width = 420; overlay.Height = 420;
            Render(main, overlay);
            Check(emptyModel.Snapshot.Groups.Single(group => group.Name == "스토리").Candidates.Count == 0 &&
                emptyModel.Snapshot.Groups.Single(group => group.Name == "공중이동").Candidates.Count == 0,
                "empty-tag fixture fabricated purpose members");
            Capture(main, "main-no-matching-tags-920-min.png");
            Capture(overlay, "unit-no-matching-tags-420-min.png");

            var report = new
            {
                success = true,
                fixtureOnly = true,
                diagnosticReference = model.IsDiagnosticReference,
                sealedObservationType = nameof(DiagnosticInventoryObservation),
                runtimeEnabled = app.Execution.RuntimeEnabled,
                catalogVersion = catalog.MapVersion,
                canonicalGroups = model.LastKnownSnapshot?.Groups.Select(group => group.Name).ToArray(),
                populatedBundledGuide = true,
                initialMainCategory = "스토리",
                initialUnitOpenCategory = "스토리",
                categoryChoiceRetainedChangedHand = true,
                foldChoiceRetainedChangedHand = true,
                scrollRetainedChangedHand = Math.Abs(scrollAfterChangedHand - scrollBefore) < 1,
                staleChoiceRetained = true,
                stableCacheExpandAll = true,
                selectedUnitId = model.SelectedUnitId,
                craftWindowOpened,
                emptyPurposeGroupsRemainVisible = true,
                screenshots = files.Order().ToArray()
            };
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("FIRST_LEGEND_PRESENTATION PASS: native MAIN/UNIT/craft, 2.321 bundled groups, retention, stale and empty-tag evidence.");
            return 0;

            void Capture(Window window, string name)
            {
                Render(main, overlay);
                var surface = window is MainWindow && name.Contains("no-matching", StringComparison.Ordinal)
                    ? (FrameworkElement)window.Content : window;
                SaveWindow(surface, Path.Combine(output, name));
                files.Add(name);
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString());
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (main is not null)
            {
                main.CraftWindow.CloseForApplication();
                main.Close();
            }
            if (overlay is not null)
            {
                overlay.Stats.CloseForApplication();
                overlay.CloseForApplication();
            }
            app.Shutdown();
        }
    }

    private static void Render(MainWindow main, OverlayWindow overlay)
    {
        ((NormalCandidateView)main.FindName("NormalBrowserView")).Render();
        overlay.NormalView.Render();
        main.UpdateLayout();
        overlay.UpdateLayout();
        main.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    private static void SelectCategory(NormalCandidateView view, string category)
    {
        var picker = Visuals(view).OfType<ComboBox>().Single(control =>
            AutomationProperties.GetAutomationId(control) == "normal-category-picker");
        picker.SelectedItem = picker.Items.Cast<object>().Single(item => item.ToString()!.StartsWith(category, StringComparison.Ordinal));
    }

    private static string SelectedCategory(NormalCandidateView view)
    {
        var picker = Visuals(view).OfType<ComboBox>().Single(control =>
            AutomationProperties.GetAutomationId(control) == "normal-category-picker");
        return picker.SelectedItem!.ToString()!.Split('·', 2)[0].Trim();
    }

    private static void FocusOverlayCategory(NormalCandidateView view, string category)
    {
        foreach (var pair in Folds(view)) pair.Value.IsExpanded = pair.Key == category;
    }

    private static Dictionary<string, Expander> Folds(NormalCandidateView view) => Visuals(view).OfType<Expander>()
        .Where(expander => AutomationProperties.GetAutomationId(expander).StartsWith("overlay-category-", StringComparison.Ordinal))
        .ToDictionary(expander => AutomationProperties.GetAutomationId(expander)["overlay-category-".Length..], StringComparer.Ordinal);

    private static ScrollViewer CandidateScroll(NormalCandidateView view) => Visuals(view).OfType<ScrollViewer>().Single(scroll =>
        AutomationProperties.GetAutomationId(scroll) == "normal-candidate-scroll");

    private static Button[] VisibleCandidateButtons(NormalCandidateView view) => Visuals(view).OfType<Button>()
        .Where(button => AutomationProperties.GetAutomationId(button).StartsWith("normal-candidate-", StringComparison.Ordinal))
        .ToArray();

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }

    private static string Slug(string category) => category switch
    {
        "스토리" => "story",
        "공중이동" => "air",
        _ => "all-legend-plus"
    };

    private static void Pump(Task task)
    {
        var bounded = task.WaitAsync(TimeSpan.FromSeconds(30));
        if (!bounded.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var staDispatcher = Dispatcher.CurrentDispatcher;
            bounded.ContinueWith(_ => staDispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        bounded.GetAwaiter().GetResult();
    }

    private static void SaveWindow(FrameworkElement element, string destination)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(destination);
        encoder.Save(stream);
    }

    private static T Field<T>(object owner, string name) where T : class => (T)owner.GetType()
        .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
