using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrandOverlay;

// Process-isolated synthetic replay of production scan -> acceptance -> presentation -> OverlayWindow rendering.
// Samples preserve the observed 0003 raw/world/binding values, but are not gameplay evidence.
internal static class UnitListStabilityCapture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Binding = "90B766FFD155CC00543FB4B432E9D58F2046F72599B638F8DB0EB7C3DE47D230";
    private const string Context = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string World0407 = "04079B10AD2A3465339E13ECDFD6C319507B9D36BB8CDD1B51D9D186F178456D";
    private const string WorldF01D = "F01D9C76A0508978F19FC1B4E8964949643790D57965C37A613810CD332A44E2";
    private const string WorldE69E = "E69ED7513983D2FC123C0FAAAFBC64BCD28F1B5D741AB459E56804C9F556347C";
    private const string WorldA488 = "A488C1E85C2A3158F33FF045D8DE56A4CEA2FC56472BC7B70215D931C7E2E6E0";
    private const string World8A58 = "8A586623A2355D93D07D61207685AD4081BFAC9BBE46E695218293F548CDBDC7";
    private const string World3EE6 = "3EE66A4731A4DDD3E33695F926A42CDB79ACC33DD79CD99F8C15208CA2DCE8DD";
    private const string World27DE = "27DEDBFDED2781978D2CA6463903BD5E3A189BF13774B29B83339476D327126F";

    private static readonly string[] BaseOwned = ["900h", "500h", "700h", "100h", "N00h", "400h", "600h"];
    private static readonly string[] AddedOwned = ["900h", "500h", "700h", "100h", "N00h", "400h", "800h", "600h"];
    private static readonly string[] RemovedOwned = ["900h", "500h", "700h", "100h", "N00h", "800h", "600h"];
    private static readonly string[] GrowthTwoOwned = ["900h", "500h", "700h", "100h", "N00h", "800h", "I10h", "600h"];
    private static readonly Warcraft300Diagnostic.View GrowthView = new(0x10000, 0, 0x20000);
    private static readonly Warcraft300Diagnostic.Unit GrowthUnit = new(0x90000, 27, Code("I10h"),
        new(0x100000, 0x90000, 0x200000, 0x300000, 0x400000, 0x90000, 7, 0x500000, 100,
            0x600000, 0xFFFFFFFE, 0x700000, 7, Warcraft300HandleValidator.UnitTypeId, 0x90000, 0, 0));
    private static readonly string GrowthWitness = DiagnosticInventoryBinding.GrowthUnit("sealed-unit-list-replay", GrowthView, GrowthUnit);
    private static readonly ImmutableDictionary<string, int> Raw17 = Raw(
        "260h", "600h", "XI0e", "A70h", "100h", "700h", "500h", "R60h", "Q60h",
        "4C0H", "M50H", "400h", "A80h", "S60h", "N00h", "U50h", "900h");
    private static readonly ImmutableDictionary<string, int> Raw18 = Raw(
        "260h", "600h", "XI0e", "A70h", "100h", "700h", "500h", "R60h", "Q60h",
        "4C0H", "M50H", "400h", "A80h", "S60h", "N00h", "U50h", "900h", "800h");
    private static readonly ImmutableDictionary<string, int> RawAfterWisp = Raw(
        "260h", "600h", "A70h", "100h", "700h", "500h", "R60h", "Q60h",
        "4C0H", "M50H", "400h", "A80h", "S60h", "N00h", "U50h", "900h", "800h");
    private static readonly ImmutableDictionary<string, int> RawAfterOwnedRemoval = Raw(
        "260h", "600h", "A70h", "100h", "700h", "500h", "R60h", "Q60h",
        "4C0H", "M50H", "A80h", "S60h", "N00h", "U50h", "900h", "800h");

    internal static int Run(string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Evidence directory already exists.");
        Directory.CreateDirectory(output);
        var checks = new List<CheckResult>();
        var frames = new List<FrameEvidence>();
        var settings = new AppSettings
        {
            Mode = PlayMode.Normal, AutoScanEnabled = true, AutoUpdateEnabled = false,
            ClearDataAutoRefresh = false, TelemetryEnabled = false,
            OverlayDisplayMode = OverlayDisplayMode.Full, ClickThroughOverlay = false
        };
        var log = new LocalActivityLog(Path.Combine(output, "activity"));
        var fixture = OverlayExecutionContext.FixtureWithActivityLog(settings, log);
        var app = new App { Execution = fixture, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        TelemetryConsentStartup.ClearStartupUri(app);
        var main = new MainWindow(fixture, fixtureMapVersion: "2.321") { ShowActivated = false };
        var overlay = Field<OverlayWindow>(main, "_overlay");
        overlay.ShowActivated = false; overlay.Left = 40; overlay.Top = 40; overlay.Width = 520; overlay.Height = 780;
        overlay.Stats.ShowActivated = false;
        var catalog = Field<DataCatalog>(main, "_catalog");
        var now = TimeSpan.Zero;
        var reader = new ReplayReader(catalog);
        main.ConfigureControlledCadence(reader, () => now);
        string? infrastructureError = null;
        var success = false;
        string? stableCards = null;
        string? stableRows = null;

        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                Check(!fixture.LiveMemoryEnabled && !fixture.RuntimeEnabled, "fixture-has-no-native-reader-or-runtime-effects");
                Check(Raw17.Count == 17 && Raw17.ContainsKey("XI0e") && Raw17.ContainsKey("U50h") && Raw17.ContainsKey("400h"),
                    "source-raw-sample-61841-61846-61861-exact-identity");
                Check(Raw18.Count == 18 && RawAfterWisp.Count == 17 && Raw18.ContainsKey("XI0e") && !RawAfterWisp.ContainsKey("XI0e"),
                    "source-raw-sample-61903-addition-and-61950-wisp-removal-distinguished");

                reader.Full.Enqueue(Full(BaseOwned, 1, World0407, Raw17, "seq61841-full"));
                reader.Basic.Enqueue(Basic(BaseOwned, WorldF01D, Raw17, "seq61846-basic-world-change"));
                reader.Basic.Enqueue(Basic(BaseOwned, WorldF01D, Raw17, "seq61846-repeat"));
                reader.Full.Enqueue(Full(BaseOwned, 1, WorldF01D, Raw17, "seq61861-full"));
                reader.Basic.Enqueue(Basic(AddedOwned, WorldE69E, Raw18, "real-basic-add-800h-before-full"));
                reader.Basic.Enqueue(Basic(AddedOwned, WorldE69E, Raw18, "real-basic-add-repeat"));
                reader.Full.Enqueue(Full(AddedOwned, 1, WorldE69E, Raw18, "seq61903-full-addition"));
                reader.Basic.Enqueue(Basic(AddedOwned, WorldA488, Raw18, "irrelevant-world-after-add"));
                reader.Basic.Enqueue(Basic(AddedOwned, WorldA488, Raw18, "before-wisp-removal"));
                reader.Full.Enqueue(Full(AddedOwned, 1, World8A58, RawAfterWisp, "seq61950-full-wisp-removal"));
                reader.Basic.Enqueue(Basic(AddedOwned, World8A58, RawAfterWisp, "post-wisp-removal-basic"));
                reader.Basic.Enqueue(Basic(RemovedOwned, World3EE6, RawAfterOwnedRemoval, "real-owned-removal-400h"));
                reader.Full.Enqueue(RejectedFull("seq61881-explicit-full-rejection"));
                reader.Basic.Enqueue(Basic(RemovedOwned, World3EE6, RawAfterOwnedRemoval, "after-explicit-full-rejection"));
                reader.Basic.Enqueue(Basic(RemovedOwned, World3EE6, RawAfterOwnedRemoval, "before-growth-removal"));
                reader.Full.Enqueue(Full(RemovedOwned, 0, World3EE6, RawAfterOwnedRemoval, "real-growth-removal"));
                reader.Basic.Enqueue(Basic(RemovedOwned, World27DE, RawAfterOwnedRemoval, "after-growth-removal"));
                reader.Basic.Enqueue(Basic(RemovedOwned, World27DE, RawAfterOwnedRemoval, "before-growth-change"));
                var rawGrowthTwo = RawAfterOwnedRemoval.SetItem("I10h", 2);
                reader.Full.Enqueue(Full(GrowthTwoOwned, 1, World27DE, rawGrowthTwo, "real-growth-count-change"));
                reader.Basic.Enqueue(Basic(GrowthTwoOwned, WorldF01D, rawGrowthTwo, "stable-after-growth-change"));
                reader.Basic.Enqueue(Basic(GrowthTwoOwned, WorldF01D, rawGrowthTwo, "expired-input", expired: true));
                reader.Full.Enqueue(Full(GrowthTwoOwned, 1, WorldF01D, rawGrowthTwo, "full-before-source-fence"));
                reader.Basic.Enqueue(Basic(GrowthTwoOwned, WorldF01D, rawGrowthTwo, "mismatched-source", mismatchedSource: true));

                var initial = await Tick(0, "01-full-61841", Expected(BaseOwned, 1));
                stableRows = initial.InventoryRowsKey; stableCards = initial.CardKey;
                var changedWorld = await Tick(100, "02-basic-61846-world-change", Expected(BaseOwned, 1));
                Check(changedWorld.InventoryRowsKey == stableRows, "unchanged-owned-growth-keeps-unit-list-order-across-world-change");
                Check(changedWorld.CardKey == stableCards, "unchanged-owned-growth-keeps-overlay-card-order-and-material-counts");
                await Tick(200, "03-basic-repeat", Expected(BaseOwned, 1));
                await Tick(250, "04-full-61861-restored", Expected(BaseOwned, 1));
                await Tick(350, "05-basic-real-add-800h", Expected(AddedOwned, 1));
                await Tick(450, "06-basic-add-repeat", Expected(AddedOwned, 1));
                await Tick(500, "07-full-61903-add", Expected(AddedOwned, 1));
                await Tick(600, "08-basic-irrelevant-world", Expected(AddedOwned, 1));
                await Tick(700, "09-basic-before-wisp-removal", Expected(AddedOwned, 1));
                await Tick(750, "10-full-61950-wisp-removed", Expected(AddedOwned, 1));
                await Tick(850, "11-basic-after-wisp-removal", Expected(AddedOwned, 1));
                await Tick(950, "12-basic-real-remove-400h", Expected(RemovedOwned, 1));
                await Tick(1000, "13-full-61881-rejected", Expected(RemovedOwned, 1));
                await Tick(1100, "14-basic-after-rejection", Expected(RemovedOwned, 1));
                await Tick(1200, "15-basic-before-growth-removal", Expected(RemovedOwned, 1));
                await Tick(1250, "16-full-growth-removed", Expected(RemovedOwned, 0));
                await Tick(1350, "17-basic-after-growth-removal", Expected(RemovedOwned, 0));
                await Tick(1450, "18-basic-before-growth-change", Expected(RemovedOwned, 0));
                await Tick(1500, "19-full-growth-count-two", Expected(GrowthTwoOwned, 1));
                var beforeBad = await Tick(1600, "20-basic-world-change-retains-growth-two", Expected(GrowthTwoOwned, 1));
                var expired = await Tick(1700, "21-expired-basic-rejected", Expected(GrowthTwoOwned, 1));
                Check(expired.InventoryRowsKey == beforeBad.InventoryRowsKey && expired.CardKey == beforeBad.CardKey,
                    "expired-basic-input-cannot-mutate-visible-list-or-cards");
                await Tick(1750, "22-full-before-source-fence", Expected(GrowthTwoOwned, 1));
                var beforeSource = frames[^1];
                var mismatched = await Tick(1850, "23-mismatched-basic-source-rejected", Expected(GrowthTwoOwned, 1));
                Check(mismatched.InventoryRowsKey == beforeSource.InventoryRowsKey && mismatched.CardKey == beforeSource.CardKey,
                    "mismatched-basic-source-cannot-mutate-visible-list-or-cards");

                Check(reader.Full.Count == 0 && reader.Basic.Count == 0, "all-sealed-replay-observations-consumed");
                Check(reader.MaxActive == 1, "single-reader-admission-preserved");
                Check(frames.All(frame => frame.Goal == "랜디픽 · 유닛 확인"), "captured-window-is-unit-confirmation-overlay");
                Check(frames.All(frame => frame.Hwnd != 0 && frame.ImageWritten), "all-frames-captured-from-created-overlay-hwnd");
                Check(frames.All(frame => !frame.GameplayReady && !frame.CoachCurrent && frame.AutomaticCount == 0),
                    "unknown-reference-only-provenance-never-upgrades-gameplay-authority");
                await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                success = checks.All(check => check.Passed);
            }
            catch (Exception error)
            {
                infrastructureError = error.ToString();
                Console.Error.WriteLine(error);
            }
            finally
            {
                try
                {
                    Field<CancellationTokenSource?>(main, "_scanCancellation")?.Cancel();
                    await Field<TaskCompletionSource?>(main, "_activityScanFinished")!.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    main.Close();
                    await log.DisposeAsync();
                    var remaining = app.Windows.Count;
                    if (remaining != 0) success = false;
                    var report = new
                    {
                        success, synthetic = true, actualGameData = false, productionVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(),
                        processId = Environment.ProcessId, scenario = "unit-list-stability-0003-replay", remainingWindows = remaining,
                        checks, failures = checks.Where(check => !check.Passed).Select(check => check.Name).ToArray(), frames,
                        sourceEvidence = new
                        {
                            log = "artifacts/unit-list-stability-20260922/reproduction-source.jsonl",
                            sequences = new[] { 61841, 61846, 61861, 61881, 61903, 61950 }, binding = Binding, viewSlot = 0,
                            worlds = new[] { World0407, WorldF01D, WorldE69E, WorldA488, World8A58, World3EE6, World27DE },
                            raw61841_61846_61861 = Raw17, raw61903 = Raw18, raw61950 = RawAfterWisp,
                            note = "Sealed synthetic replay preserving captured values; not real gameplay or a native memory read."
                        },
                        infrastructureError,
                        cleanup = new { ownedWindowsClosed = remaining == 0, processExitsWithTool = true, nativeReaderCreated = false, userInputSent = false }
                    };
                    File.WriteAllText(Path.Combine(output, "unit-list-stability.json"), JsonSerializer.Serialize(report,
                        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                    Console.WriteLine($"UNIT LIST STABILITY WPF {(success ? "PASS" : "FAIL")}; checks={checks.Count}; failures={checks.Count(x => !x.Passed)}; frames={frames.Count}; remainingWindows={remaining}");
                }
                catch (Exception cleanupError)
                {
                    success = false; Console.Error.WriteLine(cleanupError);
                }
                app.Shutdown(success ? 0 : 1);
            }
        }), DispatcherPriority.Normal);
        return app.Run();

        async Task<FrameEvidence> Tick(int milliseconds, string name, IReadOnlyDictionary<string, int> expected)
        {
            now = TimeSpan.FromMilliseconds(milliseconds);
            var rendered = Signal();
            EventHandler onRender = (_, _) => rendered.TrySetResult();
            CompositionTarget.Rendering += onRender;
            try
            {
                await main.ScanControlledCadenceAsync();
                overlay.Stats.Hide();
                if (!overlay.IsVisible) overlay.Show();
                overlay.UpdateLayout();
                await overlay.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { CompositionTarget.Rendering -= onRender; }

            var reference = Field<IDiagnosticInventoryReference?>(main, "_diagnosticInventory");
            var actual = reference?.Counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) ?? new Dictionary<string, int>();
            var expectedOrdered = expected.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var rows = ((ListBox)main.FindName("InventoryList")).Items.Cast<object>().Select(x => x.ToString() ?? "").ToArray();
            var cards = Visuals(overlay.NormalView).OfType<Button>()
                .Select(button => new { Id = AutomationProperties.GetAutomationId(button), Text = TextOf(button) })
                .Where(card => card.Id.StartsWith("normal-candidate-", StringComparison.Ordinal))
                .Select(card => card.Id + "=" + card.Text).ToArray();
            var proof = main.CaptureDiagnosticInventoryUiProof();
            var goal = Visuals(overlay).OfType<TextBlock>().Select(text => text.Text)
                .FirstOrDefault(text => text == "랜디픽 · 유닛 확인") ?? "";
            var file = name + ".png";
            Save(overlay, Path.Combine(output, file));
            var frame = new FrameEvidence(name, milliseconds, new WindowInteropHelper(overlay).Handle.ToInt64(), file,
                File.Exists(Path.Combine(output, file)), goal, reference?.Source ?? "", reference?.WorldStampFingerprint ?? "",
                reference?.GrowthAttributionAvailable ?? false, actual, rows, string.Join("|", rows), cards, string.Join("|", cards),
                proof.ReferenceCurrent, proof.AutomaticCount, proof.CoachCurrent, proof.LiveSessionActive,
                reference?.GameplayReady ?? false);
            frames.Add(frame);
            Check(DictionariesEqual(actual, expectedOrdered), name + "-visible-inventory-exact");
            Check(proof.ReferenceCurrent && proof.ObservedCount == expected.Values.Sum(), name + "-reference-current-and-count");
            Check(rows.Length == expected.Count && rows.All(row => row.Contains("×", StringComparison.Ordinal)), name + "-ordered-unit-rows-rendered");
            Check(cards.Length > 0 && cards.All(card => card.Contains("%", StringComparison.Ordinal) || card.Contains("미확인", StringComparison.Ordinal)),
                name + "-ordered-cards-show-material-state");
            return frame;
        }
        void Check(bool condition, string name) => checks.Add(new(name, condition));
    }

    private static IReadOnlyDictionary<string, int> Expected(IEnumerable<string> owned, int growth)
    {
        var value = owned.ToDictionary(raw => "rawcode:" + raw, _ => 1, StringComparer.Ordinal);
        if (growth > 0) value["rawcode:I10h"] = value.GetValueOrDefault("rawcode:I10h") + growth;
        return value;
    }

    private sealed record BasicSpec(string[] Owned, string World, ImmutableDictionary<string, int> Rawcodes,
        string Label, bool Expired = false, bool MismatchedSource = false);
    private sealed record FullSpec(string[] Owned, int Growth, string World, ImmutableDictionary<string, int> Rawcodes,
        string Label, bool Rejected = false);
    private static BasicSpec Basic(string[] owned, string world, ImmutableDictionary<string, int> rawcodes, string label,
        bool expired = false, bool mismatchedSource = false) => new(owned, world, rawcodes, label, expired, mismatchedSource);
    private static FullSpec Full(string[] owned, int growth, string world, ImmutableDictionary<string, int> rawcodes, string label) =>
        new(owned, growth, world, rawcodes, label);
    private static FullSpec RejectedFull(string label) => new([], 0, "", ImmutableDictionary<string, int>.Empty, label, true);

    private sealed class ReplayReader(DataCatalog catalog) : IInventoryRecognizer, IModernBasicInventorySource
    {
        internal Queue<BasicSpec> Basic { get; } = new();
        internal Queue<FullSpec> Full { get; } = new();
        internal int Active, MaxActive;
        private long _basicRevision = 11974, _fullRevision = 3204;
        public Task<RecognitionResult> RecognizeAsync(AppSettings settings, CancellationToken token) =>
            throw new InvalidOperationException("Modern replay must use typed lanes.");

        public Task<DiagnosticBasicInventoryRead> RecognizeBasicAsync(AppSettings settings, CancellationToken token)
        {
            Enter();
            try
            {
                token.ThrowIfCancellationRequested();
                var spec = Basic.Dequeue();
                var sample = MakeBasic(spec.Owned, spec.World, spec.Rawcodes, spec.Label, spec.Expired, spec.MismatchedSource);
                return Task.FromResult(DiagnosticBasicInventoryRead.ForSample(sample));
            }
            finally { Active--; }
        }

        public async IAsyncEnumerable<DiagnosticRecognitionFrame> RecognizeFramesAsync(AppSettings settings,
            [EnumeratorCancellation] CancellationToken token)
        {
            Enter();
            try
            {
                token.ThrowIfCancellationRequested();
                var spec = Full.Dequeue();
                await Task.CompletedTask;
                if (spec.Rejected)
                {
                    yield return DiagnosticRecognitionFrame.ForCompleted(new RecognitionResult
                    {
                        State = RecognitionState.TransientReadError,
                        Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName, spec.Rawcodes, spec.Label),
                        Status = "Handle index or table limit is outside safety bounds."
                    });
                    yield break;
                }
                var now = DateTimeOffset.UtcNow;
                var basic = MakeBasic(spec.Owned, spec.World, spec.Rawcodes, spec.Label + "-completion", false, false);
                var entries = Entries(spec.Owned).ToList();
                if (spec.Growth > 0)
                {
                    var existing = entries.FindIndex(entry => entry.UnitId == "rawcode:I10h");
                    if (existing >= 0) entries[existing].Count += 1;
                    else entries.Insert(Math.Max(0, entries.Count - 1), new InventoryEntry { UnitId = "rawcode:I10h", Count = 1 });
                }
                var full = DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                    Context, ++_fullRevision, 0, now.AddMilliseconds(-36), now, TimeSpan.FromMilliseconds(36), entries,
                    spec.Growth > 0 ? ["rawcode:I10h"] : [], 2, spec.World, Binding,
                    spec.Growth > 0 ? GrowthWitness : "");
                yield return DiagnosticRecognitionFrame.ForCompleted(new RecognitionResult
                {
                    State = RecognitionState.Ready, Entries = entries, DiagnosticObservation = full, CompletionBasicSample = basic,
                    Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName, spec.Rawcodes, spec.Label, spec.Growth)
                });
            }
            finally { Active--; }
        }

        private DiagnosticBasicInventorySample MakeBasic(string[] owned, string world,
            ImmutableDictionary<string, int> rawcodes, string label, bool expired, bool mismatchedSource)
        {
            var completed = DateTimeOffset.UtcNow;
            var started = expired ? completed.AddSeconds(-4) : completed.AddMilliseconds(-9);
            var value = DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                Context, ++_basicRevision, 0, started, completed, TimeSpan.FromMilliseconds(9), Entries(owned), world, Binding,
                [GrowthWitness]);
            var source = mismatchedSource ? DiagnosticInventoryObservation.SourceName : DiagnosticBasicInventoryObservation.SourceName;
            return new(value, Diagnostics(source, rawcodes, label));
        }
        private static IEnumerable<InventoryEntry> Entries(IEnumerable<string> rawcodes) =>
            rawcodes.Select(raw => new InventoryEntry { UnitId = "rawcode:" + raw, Count = 1 });
        private static RecognitionDiagnostics Diagnostics(string source, ImmutableDictionary<string, int> rawcodes,
            string label, int growth = 0)
        {
            var projected = growth > 0 ? rawcodes.SetItem("I10h", growth) : rawcodes;
            return new RecognitionDiagnostics
            {
                Source = source, ProcessVersion = Warcraft300Diagnostic.Version, ExecutableSha256 = Warcraft300Diagnostic.Hash,
                ActivityRawcodes = rawcodes, ActivityProjectedRawcodes = projected,
                ObservedObjects = rawcodes.Count + growth, ForeignObjects = 313,
                UnknownObjects = rawcodes.Count(raw => raw.Key is "260h" or "4C0H" or "A70h" or "A80h" or "M50H" or "Q60h" or "R60h" or "S60h" or "U50h" or "XI0e"),
                UnknownRawcodes = rawcodes.Keys.Where(raw => raw is "260h" or "4C0H" or "A70h" or "A80h" or "M50H" or "Q60h" or "R60h" or "S60h" or "U50h" or "XI0e").OrderBy(x => x, StringComparer.Ordinal).ToList(),
                Detail = "sealed synthetic source sample " + label
            };
        }
        private void Enter()
        {
            Active++; MaxActive = Math.Max(MaxActive, Active);
            if (Active != 1) throw new InvalidOperationException("Replay admitted overlapping reader work.");
        }
    }

    private sealed record CheckResult(string Name, bool Passed);
    private sealed record FrameEvidence(string Name, int Milliseconds, long Hwnd, string Image, bool ImageWritten,
        string Goal, string Source, string World, bool GrowthAttributionAvailable,
        IReadOnlyDictionary<string, int> Inventory, string[] InventoryRows, string InventoryRowsKey,
        string[] Cards, string CardKey, bool ReferenceCurrent, int AutomaticCount, bool CoachCurrent,
        bool LiveSessionActive, bool GameplayReady);

    private static bool DictionariesEqual(IReadOnlyDictionary<string, int> left, IReadOnlyDictionary<string, int> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var count) && count == pair.Value);
    private static ImmutableDictionary<string, int> Raw(params string[] values) =>
        values.ToImmutableDictionary(value => value, _ => 1, StringComparer.Ordinal);
    private static uint Code(string rawcode) => RawcodeCodec.TryParse(rawcode, out var value) ? value :
        throw new InvalidOperationException("Invalid replay rawcode: " + rawcode);
    private static string TextOf(DependencyObject root) => string.Join(" ", Visuals(root).OfType<TextBlock>()
        .Select(text => new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text.Trim())
        .Where(text => !string.IsNullOrWhiteSpace(text)));
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    private static void Save(FrameworkElement content, string path)
    {
        content.UpdateLayout();
        var width = checked((int)Math.Ceiling(content.ActualWidth));
        var height = checked((int)Math.Ceiling(content.ActualHeight));
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Overlay has no rendered bounds.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); encoder.Save(file);
    }
}
