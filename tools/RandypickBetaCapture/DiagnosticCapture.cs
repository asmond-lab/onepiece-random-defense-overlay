using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrandOverlay;

// Actual WPF rendering of invented catalog-known inputs. Never a game capture or native reader.
internal static partial class DiagnosticCapture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Caption = "SYNTHETIC FIXTURE | 2.320 diagnostic reference | NOT GAME DATA";

    internal static int Run(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: --diagnostic <outputdir>"); return 2; }
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var checks = new List<object>();
        var captures = new List<object>();
        App? app = null;
        MainWindow? main = null;
        OverlayWindow? overlay = null;
        Exception? failure = null;
        long revision = 0;
        int? fixtureRound = 1;
        var context = new string('a', 64);
        var settings = new AppSettings { Mode = PlayMode.Normal, AutoScanEnabled = true,
            TelemetryEnabled = false, ClearDataAutoRefresh = false, AutoUpdateEnabled = false,
            OverlayDisplayMode = OverlayDisplayMode.Full, LastVisibleOverlayDisplayMode = OverlayDisplayMode.Full,
            ClickThroughOverlay = false };
        var fixture = OverlayExecutionContext.Fixture(settings);
        try
        {
            app = new App { Execution = fixture, ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            TelemetryConsentStartup.ClearStartupUri(app);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Check(!fixture.RuntimeEnabled && !fixture.LiveMemoryEnabled && !fixture.HasCurrentConsent,
                "fixture-authority-no-runtime-reader-or-consent");
            main = new MainWindow(fixture, fixtureMapVersion: "2.320") { Left = -5000, Top = 0, Width = 1280, Height = 800 };
            overlay = Field<OverlayWindow>(main, "_overlay");
            var catalog = Field<DataCatalog>(main, "_catalog");
            Check(catalog.MapVersion == "2.320", "modern-catalog");
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            main.Loaded += (_, _) => loaded.TrySetResult();
            main.Show(); Pump(loaded.Task); Drain();
            Check(!main.CraftWindow.IsVisible, "craft-window-hidden-until-target-selection");
            overlay.Width = 600; overlay.Height = 800;
            var statsLayout = OverlayLayoutPolicy.StatsLayout(OverlayDisplayMode.Full);
            overlay.Stats.Width = statsLayout.Width; overlay.Stats.Height = statsLayout.Height;
            var rare = catalog.AllUnits.First(u => NormalCandidateBrowser.Tier(u) is "희귀함" or "희귀" && u.Recipe.Count > 0);
            var legend = catalog.AllUnits.First(u => NormalCandidateBrowser.Tier(u) == "전설");
            var upper = catalog.AllUnits.First(NormalCandidateBrowser.IsUpper);
            var known = catalog.AllUnits.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
            var ids = rare.Recipe.Keys.Where(known.Contains).Concat(new[] { rare.Id, legend.Id, upper.Id }).Distinct(StringComparer.Ordinal).ToArray();
            var entries = ids.Select(id => new InventoryEntry { UnitId = id, Count = 1 }).ToList();
            var growthIds = new[] { ids[0] };
            RecognitionResult? lastReady = null;
            Fresh();
            var model = Field<NormalCandidateBrowser>(main, "_diagnosticCandidates");
            if (args[0] == "--diagnostic-craft")
            {
                var guide = NormalGuideProfile.LoadBundled();
                var craftTarget = guide.Where(item => item.StoryFast).Select(item => catalog.Unit(item.UnitId))
                    .First(unit => NormalCandidateBrowser.Tier(unit) is "희귀함" or "희귀" && unit.Recipe.Count > 0);
                entries = craftTarget.Recipe.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToList();
                growthIds = []; Invoke(main, "ResetMatchSession"); Fresh(); Drain();
                SelectCraftTarget(main, model, craftTarget.Id); Drain();
                RunRoundedCraftEvidence(main, model, () => { Fresh(); Drain(); }, output, Check);
                Console.WriteLine("CRAFT WPF FIXTURE PASS (synthetic reference only)");
                return 0;
            }
            var chromeSource = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(overlay).Handle)!;
            var chromeChanges = 0;
            IntPtr CountChrome(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (message == 0x0083) chromeChanges++; // WM_NCCALCSIZE, from native frame refreshes.
                return IntPtr.Zero;
            }
            chromeSource.AddHook(CountChrome);
            try { for (var sample = 0; sample < 5; sample++) Fresh(); }
            finally { chromeSource.RemoveHook(CountChrome); }
            var visibilityChanges = 0;
            DependencyPropertyChangedEventHandler countVisibility = (_, _) => visibilityChanges++;
            overlay.IsVisibleChanged += countVisibility; overlay.Stats.IsVisibleChanged += countVisibility; main.CraftWindow.IsVisibleChanged += countVisibility;
            try
            {
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    Fresh(); Pump(Task.Delay(3200));
                    var stable = overlay.IsVisible && overlay.Stats.IsVisible && !main.CraftWindow.IsVisible;
                    var cleared = !model.Snapshot.IsCurrent && model.Snapshot.Groups.SelectMany(g => g.Candidates).All(c => c.Completion is null);
                    checks.Add(new { Name = "flicker-cycle-" + cycle, ChromeChanges = chromeChanges, VisibilityChanges = visibilityChanges, WindowsRemainVisible = stable, ValuesCleared = cleared });
                    Check(stable && cleared && chromeChanges == 0 && visibilityChanges == 0, "fresh-expired-cycles-keep-windows-stable-clear-values-and-avoid-native-frame-refresh");
                }
            }
            finally { overlay.IsVisibleChanged -= countVisibility; overlay.Stats.IsVisibleChanged -= countVisibility; main.CraftWindow.IsVisibleChanged -= countVisibility; }
            Fresh();

            Check(((TextBlock)main.FindName("MainObservationStatus")).Text.Contains("1라운드") &&
                overlay.Stats.DiagnosticReferenceDisplayText.Contains("1라운드"), "observed-round-one-main-and-stats");
            fixtureRound = 2; Fresh();
            Check(((TextBlock)main.FindName("MainObservationStatus")).Text.Contains("2라운드") &&
                overlay.Stats.DiagnosticReferenceDisplayText.Contains("2라운드"), "observed-round-transition-two");
            fixtureRound = null; Fresh();
            Check(((TextBlock)main.FindName("MainObservationStatus")).Text.Contains("라운드 확인 중") &&
                overlay.Stats.DiagnosticReferenceDisplayText.Contains("라운드 확인 중"), "unknown-round-clears-not-last-value");
            fixtureRound = 1; Fresh();
            Check(model.IsDiagnosticReference && model.Stage == NormalCandidateStage.Utility && model.FirstUpperId == upper.Id,
                "upper-entry-catches-up-observed-stage-without-gameplay-authority");
            Check(model.Snapshot.Groups.SelectMany(g => g.Candidates).Any(c => c.Completion > 0), "conditional-progress-present");
            Check(entries.Count == ids.Length && model.Snapshot.Groups.SelectMany(g => g.Candidates).All(c => !c.Owned),
                "growth-reservation-no-extra-entry-no-gameplay-owned");
            var normal = (NormalCandidateView)main.FindName("NormalBrowserView");
            Fresh();
            // The view renders a bounded, category-filtered subset, not the first catalog rare.
            // Pick a real visible button joined to its current model candidate, preferring progress.
            var candidatesByAutomationId = model.Snapshot.Groups.SelectMany(g => g.Candidates)
                .GroupBy(c => c.Unit.Id, StringComparer.Ordinal)
                .ToDictionary(g => "normal-candidate-" + g.Key, g => g.First(), StringComparer.Ordinal);
            var renderedCandidates = Visuals(normal).OfType<Button>()
                .Where(b => b.IsVisible && b.IsEnabled && b.ActualWidth > 0 && b.ActualHeight > 0)
                .Select(b => new { Button = b, Candidate = candidatesByAutomationId.GetValueOrDefault(AutomationProperties.GetAutomationId(b)) })
                .Where(item => item.Candidate is not null)
                .OrderByDescending(item => item.Candidate!.Completion > 0)
                .ThenByDescending(item => item.Candidate!.Completion ?? -1)
                .ToArray();
            if (renderedCandidates.Length == 0) { Save(main, Path.Combine(output, "before-selection-failure.png")); File.WriteAllText(Path.Combine(output, "before-selection-failure.json"), JsonSerializer.Serialize(new { MainMode=settings.Mode.ToString(), NormalVisible=normal.IsVisible, NormalVisibility=normal.Visibility.ToString(), ModelCount=candidatesByAutomationId.Count, Buttons=Visuals(normal).OfType<Button>().Select(b=>new {Id=AutomationProperties.GetAutomationId(b),b.IsVisible,b.IsEnabled,b.ActualWidth,b.ActualHeight}), Expanders=Visuals(normal).OfType<Expander>().Select(e=>new{e.IsExpanded,e.IsVisible}) }, new JsonSerializerOptions {WriteIndented=true})); }
            Check(renderedCandidates.Length > 0, "actual-rendered-candidate-with-valid-model-id");
            var chosenCandidate = renderedCandidates[0];
            var selectedId = chosenCandidate.Candidate!.Unit.Id;
            Click(chosenCandidate.Button);
            Check(model.SelectedUnitId == selectedId, "actual-candidate-button-selection");
            checks.Add(new { Name = "rendered-candidate-selection", SelectedUnitId = selectedId,
                Completion = chosenCandidate.Candidate.Completion, RenderedCandidateCount = renderedCandidates.Length,
                ActualButtonClick = true });
            var groupName = model.Snapshot.Groups.First(g => g.Candidates.Any(c => c.Unit.Id == selectedId)).Name;
            model.Fold(groupName, true); Fresh();
            Check(model.SelectedUnitId == selectedId && model.CollapsedCategories.Contains(groupName), "fresh-revision-preserves-pin-fold");
            model.FoldAll(false);
            Capture("normal", main, "main"); Capture("normal", overlay, "overlay"); Capture("normal", overlay.Stats, "stats");
            CaptureHotfixStates(main, overlay, model, Fresh, output, (file) => captures.Add(new { File = file, SyntheticFixture = true }), Check);
            Fresh();
            Click(Visuals(main).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "main-inventory-navigation"));
            var inventoryWindow = Field<AuxiliaryWindow>(main, "_auxiliaryWindow");
            inventoryWindow.Left = -8000;
            Capture("observed-list", inventoryWindow, "inventory");
            inventoryWindow.Close(); Drain();
            foreach (var stage in new[] { NormalCandidateStage.Legend, NormalCandidateStage.Upper, NormalCandidateStage.Rare })
            {
                Fresh();
                var picker = Visuals(normal).OfType<ComboBox>().Single(c => AutomationProperties.GetAutomationId(c) == "normal-stage-picker");
                picker.SelectedIndex = (int)stage; Drain();
                Check(model.Stage == stage, "actual-explicit-stage-" + stage);
                Fresh(); Check(model.Stage == stage && model.FirstUpperId == upper.Id, "refresh-no-auto-stage-" + stage);
                Check(model.SelectedUnitId == selectedId, "explicit-stage-preserves-selected-id-" + stage);
            }
            Fresh();
            Check(Visuals(normal).OfType<TextBlock>().Any(t => t.Text.Contains("재료")), "rendered-reference-material-label");
            Check(model.Snapshot.Groups.SelectMany(g => g.Candidates).All(c => !c.Owned),
                "reference-advice-preserves-gameplay-ownership-fence");
            foreach (var host in new DependencyObject[] { main, overlay })
            {
                var prefix = host == main ? "main-mode-" : "overlay-mode-";
                foreach (var mode in new[] { PlayMode.Beginner, PlayMode.Guide, PlayMode.Manual })
                {
                    var button = Visuals(host).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == prefix + mode.ToString().ToLowerInvariant());
                    Check(!button.IsEnabled && AutomationProperties.GetItemStatus(button) == "locked" && button.Content.ToString()!.Contains("잠금"), "beta-locked-button-" + prefix + mode);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Drain(); Fresh();
                    Check(settings.Mode == PlayMode.Normal && Proof("locked-request").NormalBrowserVisible, "beta-programmatic-request-guard-" + prefix + mode);
                }
            }
            var modeCombo = (ComboBox)main.FindName("PlayModeCombo");
            foreach (var option in PlayModes.Options.Where(o => !o.IsBetaAvailable))
            {
                modeCombo.SelectedItem = option; Drain(); Fresh();
                Check(settings.Mode == PlayMode.Normal && modeCombo.SelectedItem is PlayModeOption { Mode: PlayMode.Normal }, "beta-settings-cannot-activate-" + option.Mode);
            }
            Capture("beta-modes-locked", main, "main"); Capture("beta-modes-locked", overlay, "overlay");
            Fresh();
            var preference = (ComboBox)main.FindName("OverlayModeCombo");
            preference.SelectedIndex = 2; Drain();
            Fresh();
            var hidden = Proof("hidden-preference");
            Check(hidden.ReferenceCurrent && !hidden.RecommendationVisible && !hidden.StatsVisible &&
                Field<AppSettings>(main, "_settings").OverlayDisplayMode == OverlayDisplayMode.Hidden,
                "fresh-results-never-auto-unhide-preference");
            preference.SelectedIndex = 0; Drain(); Fresh(); CurrentNormal("explicit-display-restore");

            foreach (var kind in new[] { "failed", "spoofed", "untyped-ready" })
            {
                Fresh();
                var typed = NewObservation();
                Observe(new RecognitionResult { State = kind == "failed" ? RecognitionState.TransientReadError : RecognitionState.Ready,
                    DiagnosticObservation = kind == "untyped-ready" ? null : typed,
                    Entries = entries, Status = Caption + " / adversarial " + kind,
                    Diagnostics = Diagnostics(kind == "spoofed" ? "SPOOFED_SYNTHETIC_SOURCE" : DiagnosticInventoryObservation.SourceName) });
                Unavailable(kind, keepVisible: true);
            }
            Fresh();
            var replay = lastReady!;
            var scanCheck = (CheckBox)main.FindName("AutoScanCheck");
            scanCheck.IsChecked = false; // Real event is intentionally inert without runtime authority.
            main.StopControlledObservation(); Drain(); Unavailable("controlled-stop");
            scanCheck.IsChecked = true; Observe(replay); Unavailable("stop-old-revision-replay-rejected");
            Fresh(); CurrentNormal("stop-fresh-revision-recovery");

            // Controlled results are synchronous: exercise the cancellation invalidation seam and
            // generation fence, NOT a fictitious in-flight/native cancelled read.
            Fresh(); replay = lastReady!;
            var beforeGeneration = Field<int>(main, "_scanGeneration");
            typeof(MainWindow).GetField("_scanGeneration", Private)!.SetValue(main, beforeGeneration + 1);
            Invoke(main, "InvalidateDiagnosticInventoryObservation", false);
            Unavailable("cancel-generation-fence");
            Observe(replay); Unavailable("cancel-old-revision-replay-rejected");
            checks.Add(new { Name = "cancellation-coverage", Passed = true,
                Scope = "Fixture generation change plus shared invalidation hook; no asynchronous read or OperationCanceledException injection" });

            Fresh(); replay = lastReady!;
            var coachView = (BeginnerCoachView)main.FindName("MainCoachView");
            Click((Button)coachView.FindName("PauseButton")); Unavailable("actual-pause-button");
            Observe(NewReady()); Unavailable("paused-fresh-input-rejected");
            var resume = Visuals(normal).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "normal-resume");
            Check(resume.Visibility == Visibility.Visible, "resume-button-visible");
            Click(resume); Observe(replay); Unavailable("resume-old-revision-rejected");
            Fresh(); CurrentNormal("resume-new-revision-only");

            Fresh(); model.SetReferenceStage(NormalCandidateStage.Upper); model.Select(upper.Id); model.Fold("fixture-fold", true);
            context = new string('b', 64); Fresh();
            Check(model.Stage == NormalCandidateStage.Utility && model.FirstUpperId == upper.Id && model.SelectedUnitId is null && model.CollapsedCategories.Count == 0,
                "new-context-resets-history-then-catches-up-from-new-observed-hand");
            Fresh(); model.SetReferenceStage(NormalCandidateStage.Upper); model.Select(upper.Id);
            Invoke(main, "ResetMatchSession"); Drain(); Unavailable("match-reset");
            Check(model.Stage == NormalCandidateStage.Rare && model.SelectedUnitId is null, "reset-stage-and-pin");
            Fresh(); CurrentNormal("reset-fresh-recovery");
            Fresh(); Observe(new RecognitionResult { State = RecognitionState.Waiting, ConfirmsSessionBoundary = true, Status = Caption });
            Unavailable("confirmed-boundary"); Check(model.Stage == NormalCandidateStage.Rare, "boundary-stage-reset");

            Fresh(); model.Select(selectedId); model.FoldAll(false);
            var expiryRevision = revision;
            var expiryWatch = Stopwatch.StartNew();
            Pump(Task.Delay(TimeSpan.FromMilliseconds(3200))); // No new result, unchanged real 3-second budget.
            var timerKeptWindows = overlay.IsVisible && overlay.Stats.IsVisible;
            var timerClearedProgress = !model.Snapshot.IsCurrent && model.Snapshot.Groups.SelectMany(g => g.Candidates).All(c => c.Completion is null);
            Check(timerKeptWindows && timerClearedProgress, "idle-expiry-timer-keeps-windows-and-clears-before-proof-or-user-action");
            Unavailable("idle-expiry-3200ms", keepVisible: true);
            model.Select(selectedId); model.Fold(groupName, true); model.SetReferenceStage(NormalCandidateStage.Legend);
            Unavailable("selection-fold-stage-cannot-revive-expired", keepVisible: true);
            Check(revision == expiryRevision && expiryWatch.Elapsed.TotalMilliseconds >= 3200 &&
                DiagnosticInventoryObservation.FreshnessBudget == TimeSpan.FromSeconds(3), "expiry-budget-not-weakened-no-new-result");
            Save((FrameworkElement)main.Content, Path.Combine(output, "diagnostic-main-expired.png"));
            captures.Add(new { File = "diagnostic-main-expired.png", SyntheticFixture = true, Current = false });
            Save((FrameworkElement)overlay.Content, Path.Combine(output, "diagnostic-overlay-expired.png"));
            captures.Add(new { File = "diagnostic-overlay-expired.png", SyntheticFixture = true, Current = false });
            Save((FrameworkElement)overlay.Stats.Content, Path.Combine(output, "diagnostic-stats-expired.png"));
            captures.Add(new { File = "diagnostic-stats-expired.png", SyntheticFixture = true, Current = false });
            Check(main.CraftWindow.IsVisible && Visuals(main.CraftWorkspace).OfType<FrameworkElement>().Any(e => AutomationProperties.GetAutomationId(e) == "normal-craft-flow") &&
                Visuals(main.CraftWorkspace).OfType<TextBlock>().Any(t => t.Text.Contains("현재 패 확인 중") || t.Text.Contains("마지막 인식 기준")),
                $"detached-expired-keeps-last-plan-marked-stale (visible={main.CraftWindow.IsVisible}, requested={Field<bool>(main.CraftWindow, "_requestedVisible")}, allowed={Field<bool>(main.CraftWindow, "_displayAllowed")}, session={Field<bool>(main, "_diagnosticOverlaySession")}, fences={typeof(MainWindow).GetMethod("CurrentOverlayDisplayState", Private)!.Invoke(main, null) is OverlayDisplayState state && state.Available})");
            Save((FrameworkElement)main.CraftWindow.Content, Path.Combine(output, "diagnostic-craft-expired.png"));
            captures.Add(new { File = "diagnostic-craft-expired.png", SyntheticFixture = true, Current = false });
            Fresh(); CurrentNormal("final-fresh-recovery");
            long basicRevision = 0;
            var basicEntries = entries.Take(1).ToArray();
            var basicContext = new string('e', 64);
            var binding = new string('d', 64);
            var worldStamp = new string('c', 64);
            DiagnosticBasicInventoryObservation Basic(string? stamp = null)
            {
                var now = DateTimeOffset.UtcNow;
                return DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                    basicContext, ++basicRevision, 0, now.AddMilliseconds(-1), now, TimeSpan.FromMilliseconds(1),
                    basicEntries, stamp ?? worldStamp, binding);
            }
            DiagnosticRecognitionFrame BasicFrame(DiagnosticBasicInventoryObservation value) =>
                DiagnosticRecognitionFrame.ForBasic(value, Diagnostics(DiagnosticBasicInventoryObservation.SourceName));
            RecognitionResult PairedFull(TimeSpan age, string? stamp = null)
            {
                var now = DateTimeOffset.UtcNow;
                return new() { State = RecognitionState.Ready, Entries = entries, Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName),
                    DiagnosticObservation = DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                        context, ++revision, 0, now - age, now, age, entries, growthIds, fixtureRound, stamp ?? worldStamp, binding) };
            }
            async IAsyncEnumerable<DiagnosticRecognitionFrame> Frames(params DiagnosticRecognitionFrame[] frames)
            {
                foreach (var frame in frames) { yield return frame; await Task.CompletedTask; }
            }
            async IAsyncEnumerable<DiagnosticRecognitionFrame> SlowGrowth()
            {
                for (var tick = 0; tick < 6; tick++)
                {
                    yield return BasicFrame(Basic());
                    await Task.Delay(800);
                    var proof = Proof("basic-during-slow-growth-" + tick);
                    Check(proof.ReferenceCurrent && proof.ObservedCount == basicEntries.Sum(x => x.Count),
                        "basic-remains-current-while-growth-exceeds-three-seconds-" + tick);
                }
                yield return DiagnosticRecognitionFrame.ForCompleted(new RecognitionResult { State = RecognitionState.TransientReadError,
                    Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName), Status = Caption + " / growth failure" });
            }
            var slowStarted = Stopwatch.StartNew();
            Pump(main.ScanControlledFramesAsync(SlowGrowth()));
            Check(slowStarted.Elapsed >= TimeSpan.FromSeconds(4), "actual-delayed-frame-stream-exceeds-full-freshness-budget");
            CurrentNormal("growth-failure-keeps-new-basic-reference");
            Check(Proof("basic-after-growth-failure").ObservedCount == basicEntries.Sum(x => x.Count), "growth-failure-does-not-clear-basic-list");
            Save((FrameworkElement)main.Content, Path.Combine(output, "diagnostic-basic-during-growth.png"));
            captures.Add(new { File = "diagnostic-basic-during-growth.png", SyntheticFixture = true, Current = true });

            var pairedBasic = Basic();
            var oldFull = PairedFull(TimeSpan.FromMilliseconds(2700));
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(pairedBasic), DiagnosticRecognitionFrame.ForCompleted(oldFull))));
            Check(Proof("paired-full-current").ObservedCount == entries.Sum(x => x.Count), "matching-fresh-growth-reference-selected");
            Pump(Task.Delay(600));
            Check(Proof("growth-expiry-basic-fallback").ReferenceCurrent &&
                Proof("growth-expiry-basic-count").ObservedCount == basicEntries.Sum(x => x.Count), "growth-expiry-keeps-fresh-basic");
            Check(ReferenceEquals(Field<IDiagnosticInventoryReference>(main, "_diagnosticInventory"), pairedBasic),
                "fallback-reuses-original-basic-object-without-renewing-timestamps");

            var differentBasic = Basic(new string('f', 64));
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(differentBasic), DiagnosticRecognitionFrame.ForCompleted(PairedFull(TimeSpan.FromMilliseconds(1))))));
            Check(Proof("changed-world-basic-only").ObservedCount == basicEntries.Sum(x => x.Count), "mismatched-growth-stamp-does-not-replace-basic");
            var failedAt = DateTimeOffset.UtcNow;
            var failedBasic = DiagnosticBasicInventoryObservation.Unavailable(catalog.MapVersion, catalog.OfflineBundle!.Fingerprint,
                Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, ++basicRevision, failedAt, failedAt, TimeSpan.Zero, "SYNTHETIC basic read failed");
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(failedBasic), DiagnosticRecognitionFrame.ForCompleted(PairedFull(TimeSpan.FromMilliseconds(1))))));
            Unavailable("basic-failure-cannot-be-revived-by-full-only-result", keepVisible: true);
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(Basic()), DiagnosticRecognitionFrame.ForCompleted(PairedFull(TimeSpan.FromMilliseconds(1))))));
            CurrentNormal("both-lanes-fresh-recovery");

            async IAsyncEnumerable<DiagnosticRecognitionFrame> LateAfterStop()
            {
                yield return BasicFrame(Basic());
                scanCheck.IsChecked = false;
                main.StopControlledObservation();
                await Task.Delay(10);
                yield return BasicFrame(Basic());
                yield return DiagnosticRecognitionFrame.ForCompleted(PairedFull(TimeSpan.FromMilliseconds(1)));
            }
            Pump(main.ScanControlledFramesAsync(LateAfterStop()));
            Unavailable("stopped-stream-late-basic-cannot-revive");
            scanCheck.IsChecked = true;
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(Basic()), DiagnosticRecognitionFrame.ForCompleted(PairedFull(TimeSpan.FromMilliseconds(1))))));
            CurrentNormal("stopped-stream-new-request-recovers");
            Pump(Task.Delay(3200));
            Unavailable("both-lanes-expire-without-new-read", keepVisible: true);
            DiagnosticBasicInventoryObservation SameListBasic(string? bindingContext = null)
            {
                var now = DateTimeOffset.UtcNow;
                return DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                    basicContext, ++basicRevision, 0, now.AddMilliseconds(-1), now, TimeSpan.FromMilliseconds(1), entries, worldStamp, bindingContext ?? binding);
            }
            var laneTransitions = new List<object>();
            var stableLaneRendering = true;
            for (var cycle = 0; cycle < 3; cycle++)
            {
                Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(SameListBasic()),
                    DiagnosticRecognitionFrame.ForCompleted(PairedFull(TimeSpan.FromMilliseconds(2300))))));
                if (cycle == 0)
                {
                    model.SetReferenceStage(NormalCandidateStage.Upper);
                    var category = Visuals(normal).OfType<ComboBox>().Single(c => AutomationProperties.GetAutomationId(c) == "normal-category-picker");
                    category.SelectedItem = model.Snapshot.Groups.First().Name;
                    Drain();
                }
                if (cycle > 0) Check(Field<IDiagnosticInventoryReference>(main, "_diagnosticInventory").GrowthAttributionAvailable,
                    "growth-reference-current-before-same-list-transition-" + cycle);
                var mainGroup = Field<StackPanel>(normal, "_groups").Children[0];
                var overlayGroup = Field<StackPanel>(overlay.NormalView, "_groups").Children[0];
                var beforeStatus = ((TextBlock)main.FindName("MainObservationStatus")).Text;
                var beforeStats = overlay.Stats.DiagnosticReferenceDisplayText;
                var beforeMetric = ((System.Windows.Controls.Primitives.UniformGrid)overlay.Stats.FindName("CoreKpiPanel")).Children[0];
                if (cycle == 0) Save((FrameworkElement)main.Content, Path.Combine(output, "diagnostic-upper-growth-current.png"));
                Pump(Task.Delay(900));
                var fallback = Proof("same-list-growth-fallback-" + cycle);
                var mainSame = ReferenceEquals(mainGroup, Field<StackPanel>(normal, "_groups").Children[0]);
                var overlaySame = ReferenceEquals(overlayGroup, Field<StackPanel>(overlay.NormalView, "_groups").Children[0]);
                var statusSame = beforeStatus == ((TextBlock)main.FindName("MainObservationStatus")).Text;
                var statsSame = beforeStats == overlay.Stats.DiagnosticReferenceDisplayText;
                laneTransitions.Add(new { cycle, mainSame, overlaySame, statusSame, statsSame, beforeStatus,
                    afterStatus = ((TextBlock)main.FindName("MainObservationStatus")).Text, beforeStats,
                    afterStats = overlay.Stats.DiagnosticReferenceDisplayText });
                var metricSame = ReferenceEquals(beforeMetric, ((System.Windows.Controls.Primitives.UniformGrid)overlay.Stats.FindName("CoreKpiPanel")).Children[0]);
                Check(metricSame, "same-list-growth-basic-keeps-combat-metric-objects-" + cycle);
                stableLaneRendering &= mainSame && overlaySame && statusSame && statsSame && metricSame;
                Check(fallback.ReferenceCurrent && fallback.ObservedCount == entries.Sum(x => x.Count), "same-list-growth-fallback-keeps-current-" + cycle);
                if (cycle == 0)
                {
                    Save((FrameworkElement)main.Content, Path.Combine(output, "diagnostic-upper-basic-current.png"));
                    Save((FrameworkElement)overlay.Content, Path.Combine(output, "diagnostic-upper-overlay-basic.png"));
                    Save((FrameworkElement)overlay.Stats.Content, Path.Combine(output, "diagnostic-upper-stats-basic.png"));
                }
            }
            foreach (var file in new[] { "diagnostic-upper-growth-current.png", "diagnostic-upper-basic-current.png", "diagnostic-upper-overlay-basic.png", "diagnostic-upper-stats-basic.png" })
                captures.Add(new { File = file, SyntheticFixture = true, Current = true });
            checks.Add(new { Name = "same-list-growth-basic-transitions", Transitions = laneTransitions });
            Check(stableLaneRendering, "same-list-growth-expiry-preserves-card-objects-status-and-stats");
            Check(((TextBlock)main.FindName("MainObservationStatus")).Text.Contains("최근 확인") &&
                Field<IDiagnosticInventoryReference>(main, "_diagnosticInventory").ObservedRound is null,
                "display-history-does-not-upgrade-basic-round-authority");
            // Start the independent choice-material visual case with a fresh observation.
            var choiceTiming = Stopwatch.StartNew();
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(SameListBasic()),
                DiagnosticRecognitionFrame.ForCompleted(PairedFull(TimeSpan.FromMilliseconds(1))))));
            checks.Add(new { Name = "choice-before-selection", Elapsed = choiceTiming.Elapsed.TotalMilliseconds, Reference = Proof("choice-before-selection-current") });
            SelectCraftTarget(main, model, "rawcode:AA0H");
            checks.Add(new { Name = "choice-after-select-sync", Elapsed = choiceTiming.Elapsed.TotalMilliseconds });
            Drain();
            checks.Add(new { Name = "choice-material-state", Elapsed = choiceTiming.Elapsed.TotalMilliseconds, model.SelectedUnitId, model.Snapshot.IsCurrent,
                Completion = model.SelectedCandidate?.Completion,
                Reason = model.SelectedCandidate?.Caveat,
                Reference = Proof("choice-material-current"),
                RenderedReason = Visuals(main.CraftWorkspace).OfType<TextBlock>().Where(t => AutomationProperties.GetAutomationId(t) == "normal-material-unavailable-reason").Select(t => t.Text).ToArray() });
            Check(model.SelectedCandidate is { Completion: null } && Visuals(main.CraftWorkspace).OfType<TextBlock>().Any(t =>
                AutomationProperties.GetAutomationId(t) == "normal-material-unavailable-reason" && t.Text.Contains("선택형 재료")),
                "choice-material-unknown-has-visible-explanation");
            Save((FrameworkElement)main.Content, Path.Combine(output, "diagnostic-upper-choice-material.png"));
            Save((FrameworkElement)overlay.Content, Path.Combine(output, "diagnostic-upper-choice-overlay.png"));
            captures.Add(new { File = "diagnostic-upper-choice-material.png", SyntheticFixture = true, Current = true });
            captures.Add(new { File = "diagnostic-upper-choice-overlay.png", SyntheticFixture = true, Current = true });
            Save((FrameworkElement)main.CraftWindow.Content, Path.Combine(output, "diagnostic-craft-choice-material.png"));
            captures.Add(new { File = "diagnostic-craft-choice-material.png", SyntheticFixture = true, Current = true });
            var changedBindingBasic = SameListBasic(new string('b', 64));
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(changedBindingBasic),
                DiagnosticRecognitionFrame.ForCompleted(new RecognitionResult { State = RecognitionState.TransientReadError,
                    Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName) }))));
            Check(((TextBlock)main.FindName("MainObservationStatus")).Text.Contains("라운드 확인 중") &&
                overlay.Stats.DiagnosticReferenceDisplayText.Contains("라운드 확인 중"), "binding-change-clears-display-round-history");
            Pump(Task.Delay(3200));
            Check(!Proof("display-history-basic-expiry").ReferenceCurrent &&
                overlay.Stats.DiagnosticReferenceDisplayText.Contains("라운드 확인 중"), "basic-expiry-clears-display-history");
            var calculator = new InventoryStatsCalculator(catalog);
            var statFixtures = catalog.AllUnits.Select(u => new { Unit = u,
                Stats = calculator.Calculate([new InventoryEntry { UnitId = u.Id, Count = 1 }]) })
                .Where(x => x.Stats.UnknownValueUnitCount == 0).ToArray();
            var numericFixtureIds = new[] {
                statFixtures.First(x => x.Stats.Stun > 0).Unit.Id,
                statFixtures.First(x => x.Stats.TotalSlow > 0).Unit.Id,
                statFixtures.First(x => x.Stats.TotalArmorReduction > 0).Unit.Id,
                statFixtures.First(x => x.Stats.ManaRegen > 0).Unit.Id,
                statFixtures.First(x => x.Stats.ExplosionAmp > 0).Unit.Id,
                statFixtures.First(x => x.Stats.SingleDamageProviders > 0).Unit.Id,
                statFixtures.First(x => x.Stats.FinisherDamageProviders > 0).Unit.Id
            }.Distinct(StringComparer.Ordinal).ToArray();
            entries = numericFixtureIds.Select(id => new InventoryEntry { UnitId = id, Count = 1 }).ToList();
            growthIds = [entries[0].UnitId];
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(SameListBasic()), DiagnosticRecognitionFrame.ForCompleted(PairedFull(TimeSpan.FromMilliseconds(1))))));
            CurrentNormal("combat-numeric-current");
            var numericStats = calculator.Calculate(entries);
            Check(numericStats.UnknownValueUnitCount == 0 && numericStats.Stun > 0 && numericStats.ManaRegen > 0 &&
                numericStats.ExplosionAmp > 0, "numeric-combat-fixture-has-known-nonzero-values");
            Check(Visuals(overlay.Stats).OfType<TextBlock>().Where(t => AutomationProperties.GetAutomationId(t) == "stats-current")
                .All(t => t.Text != "?"), "known-reference-displays-real-numeric-values");
            var stunBefore = Visuals(overlay.Stats).OfType<StatsMetricView>().Single(v => AutomationProperties.GetAutomationId(v) == "stats-metric:스턴").Current;
            Save((FrameworkElement)overlay.Stats.Content, Path.Combine(output, "diagnostic-stats-combat-known.png"));
            captures.Add(new { File = "diagnostic-stats-combat-known.png", SyntheticFixture = true, Current = true });
            entries = [new InventoryEntry { UnitId = entries[0].UnitId, Count = 2 }];
            growthIds = [entries[0].UnitId];
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(SameListBasic()), DiagnosticRecognitionFrame.ForCompleted(PairedFull(TimeSpan.FromMilliseconds(1))))));
            CurrentNormal("combat-changed-current");
            var changedStats = calculator.Calculate(entries);
            var stunAfter = Visuals(overlay.Stats).OfType<StatsMetricView>().Single(v => AutomationProperties.GetAutomationId(v) == "stats-metric:스턴").Current;
            Check(stunAfter == changedStats.Stun && stunAfter != stunBefore, "changed-hand-recalculates-combat-values");
            Save((FrameworkElement)overlay.Stats.Content, Path.Combine(output, "diagnostic-stats-combat-changed.png"));
            captures.Add(new { File = "diagnostic-stats-combat-changed.png", SyntheticFixture = true, Current = true });
            void FreshProgression()
            {
                Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(SameListBasic()),
                    DiagnosticRecognitionFrame.ForCompleted(PairedFull(TimeSpan.FromMilliseconds(1))))));
                Check(Proof("progression-fresh-" + revision).ReferenceCurrent, "progression-current-" + revision);
                Drain(); main.UpdateLayout(); overlay.UpdateLayout(); overlay.Stats.UpdateLayout();
            }
            RunNormalProgression(main, overlay, catalog, model, (hand, reset) =>
            {
                if (reset) Invoke(main, "ResetMatchSession");
                entries = hand.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }).ToList();
                growthIds = [];
                FreshProgression();
            }, (name, window, role) =>
            {
                FreshProgression();
                var file = "diagnostic-" + role + "-" + name + ".png";
                Save((FrameworkElement)window.Content, Path.Combine(output, file));
                captures.Add(new { File = file, SyntheticFixture = true, Current = true });
            }, Check);
            Console.WriteLine("DIAGNOSTIC WPF FIXTURE PASS (synthetic reference only)");

            DiagnosticInventoryObservation NewObservation()
            {
                var now = DateTimeOffset.UtcNow;
                var value = DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                    context, ++revision, 0, now.AddMilliseconds(-1), now, TimeSpan.FromMilliseconds(1), entries, growthIds, fixtureRound);
                Check(value.Availability == DiagnosticInventoryAvailability.Ready && value.Entries.Length == entries.Count,
                    "synthetic-contract-r" + revision);
                return value;
            }
            RecognitionDiagnostics Diagnostics(string source) => new() { Source = source, ProcessVersion = Warcraft300Diagnostic.Version,
                ExecutableSha256 = Warcraft300Diagnostic.Hash, Detail = Caption };
            RecognitionResult NewReady() => new() { State = RecognitionState.Ready, DiagnosticObservation = NewObservation(),
                Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName), Entries = entries, Status = Caption };
            void Fresh()
            {
                lastReady = NewReady(); Observe(lastReady);
                Check(Proof("fresh-r" + revision).ReferenceCurrent, "accepted-r" + revision);
                var list = (ListBox)main.FindName("InventoryList");
                var expectedRows = entries.Select(e => $"{catalog.Unit(e.UnitId).Name}  ×{e.Count}").OrderBy(x => x);
                Check(list.Items.Cast<string>().OrderBy(x => x).SequenceEqual(expectedRows), "actual-observed-unit-list-r" + revision);
                Check(((TextBlock)main.FindName("InventoryOriginText")).Text.Contains("실제 보유 유닛과 다를 수 있어요"),
                    "observed-list-is-reference-not-owned-r" + revision);
                Check(((Button)main.FindName("CompatibilityCopyButton")).IsEnabled, "reference-feedback-info-available-r" + revision);
            }
            void Observe(RecognitionResult result) { Pump(main.ScanControlledAsync(result)); Drain(); main.UpdateLayout(); overlay.UpdateLayout(); overlay.Stats.UpdateLayout(); }
            DiagnosticInventoryUiProof Proof(string label)
            {
                var proof = main.CaptureDiagnosticInventoryUiProof();
                // The proof seam re-renders to enforce freshness. Measure the replacement controls before querying geometry.
                main.UpdateLayout(); overlay.UpdateLayout(); overlay.Stats.UpdateLayout();
                checks.Add(new { Name = label, Proof = proof, SourceRevision = revision, SyntheticFixture = true });
                Check(proof.AutomaticCount == 0 && !proof.CoachCurrent && !proof.LiveSessionActive &&
                    proof.Outcome == "unknown", label + "-no-ordinary-side-effects");
                if (args[0] != "--diagnostic-craft") CheckCombatStats(proof, label);
                return proof;
            }
            void CheckCombatStats(DiagnosticInventoryUiProof proof, string label)
            {
                var stats = overlay.Stats;
                var scroll = (ScrollViewer)stats.FindName("StatsScroll");
                Check(!stats.HasCurrentObservation && !stats.TargetsKnown, label + "-reference-stats-no-coach-authority");
                Check(scroll.Visibility == (proof.ReferenceCurrent ? Visibility.Visible : Visibility.Collapsed), label + "-combat-values-follow-freshness");
                var nodes = Visuals(stats).ToArray();
                if (!proof.ReferenceCurrent)
                {
                    Check(nodes.OfType<TextBlock>().Where(t => AutomationProperties.GetAutomationId(t) == "stats-current").All(t => t.Text == "?"), label + "-no-stale-combat-values");
                    return;
                }
                foreach (var name in new[] { "스턴", "이감" })
                    Check(nodes.OfType<StatsMetricView>().Any(v => AutomationProperties.GetAutomationId(v) == "stats-metric:" + name && !v.TargetsKnown), label + "-combat-primary-" + name);
                Check(nodes.OfType<FrameworkElement>().Any(v => AutomationProperties.GetAutomationId(v) is "stats-metric:방깎" or "stats-value:방깎"), label + "-armor-retained-in-every-direction");
                foreach (var name in new[] { "단일", "끝딜", "폭뎀증", "마젠" })
                    Check(nodes.OfType<FrameworkElement>().Any(v => AutomationProperties.GetAutomationId(v) == "stats-value:" + name), label + "-combat-secondary-" + name);
                Check(((TextBlock)stats.FindName("ObservationText")).Text.Contains("참고"), label + "-combat-reference-label");
            }
            void CurrentNormal(string label)
            {
                var proof = Proof(label);
                Check(proof.ReferenceCurrent && proof.ObservedCount > 0 && proof.RecommendationVisible && proof.StatsVisible && proof.NormalBrowserVisible,
                    label + "-actual-normal-and-stats-visible");
            }
            void Unavailable(string label, bool keepVisible = false)
            {
                var proof = Proof(label);
                var list = (ListBox)main.FindName("InventoryList");
                Check(list.Items.Count == 1 && list.Items[0] as string == "유닛 확인 중", label + "-clears-observed-unit-list");
                Check(!proof.ReferenceCurrent && proof.ObservedCount is null && proof.RecommendationVisible == keepVisible && proof.StatsVisible == keepVisible &&
                    !model.Snapshot.IsCurrent && model.Snapshot.Groups.SelectMany(g => g.Candidates).All(c => c.Completion is null), label + "-invalidated");
            }
            void Capture(string name, Window window, string role)
            {
                Fresh();
                var proof = Proof("capture-" + role + "-" + name);
                Check(proof.ReferenceCurrent && proof.ObservedCount > 0 && window.IsVisible, "capture-current-" + role + "-" + name);
                Check(Field<TextBlock>(main, "MainObservationStatus").Text == proof.RecognitionStatus,
                    "capture-main-badge-current-" + role + "-" + name);
                main.Left = -5000; overlay.Left = -6500; overlay.Stats.Left = -7500;
                window.UpdateLayout();
                var file = "diagnostic-" + role + "-" + name + ".png";
                var started = DateTimeOffset.UtcNow;
                Save((FrameworkElement)window.Content, Path.Combine(output, file));
                Check(main.CaptureDiagnosticInventoryUiProof().ReferenceCurrent, "capture-finished-before-expiry-" + file);
                captures.Add(new { File = file, SyntheticFixture = true, Caption, SourceRevision = revision, StartedAt = started,
                    FinishedAt = DateTimeOffset.UtcNow, Proof = proof });
            }
        }
        catch (Exception error) { failure = error; Console.Error.WriteLine(error); }
        finally
        {
            try { main?.Close(); }
            catch (Exception error) { failure ??= error; }
            finally
            {
                try { overlay?.Stats.CloseForApplication(); overlay?.CloseForApplication(); }
                catch (Exception error) { failure ??= error; }
                finally { try { app?.Shutdown(); } catch (Exception error) { failure ??= error; } }
            }
            File.WriteAllText(Path.Combine(output, "diagnostic-checks.json"), JsonSerializer.Serialize(new {
                SyntheticFixture = true, NotActualGameData = true, Caption, Passed = failure is null,
                LiveRuntimeEnabled = fixture.RuntimeEnabled, Source = DiagnosticInventoryObservation.SourceName,
                ExecutableVersion = Warcraft300Diagnostic.Version, ExecutableHash = Warcraft300Diagnostic.Hash,
                SourceMap = "2.320", FreshnessBudgetMilliseconds = DiagnosticInventoryObservation.FreshnessBudget.TotalMilliseconds,
                StartupUriCleared = app?.StartupUri is null, Checks = checks, Captures = captures,
                Error = failure?.ToString(), CancellationScope = "Actual ordered delayed frames; stop rejects late basic and full results; a new request recovers"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failure is null ? 0 : 1;
        void Check(bool passed, string name)
        {
            checks.Add(new { Name = name, Passed = passed });
            if (!passed) throw new InvalidOperationException("Diagnostic fixture check failed: " + name);
        }
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    private static void Invoke(object instance, string name, params object?[] arguments) =>
        instance.GetType().GetMethod(name, Private)!.Invoke(instance, arguments);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Drain(); }
    private static void Drain() => Pump(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
    private static void Pump(Task task)
    {
        if (task.IsCompleted) { task.GetAwaiter().GetResult(); return; }
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (task.IsCompleted || elapsed.Elapsed > TimeSpan.FromSeconds(30)) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        if (!task.IsCompleted) throw new TimeoutException("Fixture dispatcher wait exceeded 30 seconds.");
        task.GetAwaiter().GetResult();
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Save(FrameworkElement content, string file)
    {
        content.UpdateLayout();
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Actual WPF content has no render size.");
        var actual = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); actual.Render(content);
        const int captionHeight = 44;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height + captionHeight));
            var text = new FormattedText(Caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), width < 450 ? 10 : 13, Brushes.Gold, 1) { MaxTextWidth = Math.Max(1, width - 16) };
            dc.DrawText(text, new Point(8, 4));
            dc.DrawImage(actual, new Rect(0, captionHeight, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height + captionHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); encoder.Save(stream);
    }
}
