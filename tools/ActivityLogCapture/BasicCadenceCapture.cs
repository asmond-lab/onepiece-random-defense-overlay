using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrandOverlay;

// An isolated, controllable-time WPF integration. No production context or native reader exists here.
internal static class BasicCadenceCapture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run(string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Evidence directory already exists.");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        var settings = new AppSettings { Mode = PlayMode.Normal, AutoScanEnabled = true,
            AutoUpdateEnabled = false, ClearDataAutoRefresh = false, TelemetryEnabled = false,
            OverlayDisplayMode = OverlayDisplayMode.Full };
        var log = new LocalActivityLog(Path.Combine(output, "activity"));
        var fixture = OverlayExecutionContext.FixtureWithActivityLog(settings, log);
        var app = new App { Execution = fixture, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        TelemetryConsentStartup.ClearStartupUri(app);
        var main = new MainWindow(fixture, fixtureMapVersion: "2.321")
            { ShowActivated = false, Left = -5000, Top = 0, Width = 1100, Height = 800 };
        var overlay = Field<OverlayWindow>(main, "_overlay");
        overlay.Left = -6500; overlay.Stats.Left = -7500;
        var catalog = Field<DataCatalog>(main, "_catalog");
        var autoScan = (CheckBox)main.FindName("AutoScanCheck");
        var timer = Field<DispatcherTimer>(main, "_timer");
        var now = TimeSpan.Zero;
        var reader = new Reader(catalog, () => now);
        string? error = null;
        var success = false;
        var basicBeforeDeadline = false;
        var fullCallsAtDeadline = 0;
        var image = Path.Combine(output, "basic-before-full-deadline.png");
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                Check(!fixture.LiveMemoryEnabled && !fixture.RuntimeEnabled, "no-native-reader-or-runtime-effects");
                var loaded = Signal();
                main.Loaded += (_, _) => loaded.TrySetResult();
                main.Show();
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
                main.ConfigureControlledCadence(reader, () => now);
                await Tick(0);
                Check(Proof().ObservedCount == 1 && reader.FullCalls == 1, "initial-full-and-paired-basic-presented");
                reader.Count = 2;
                await Tick(99);
                Check(reader.BasicCalls == 0 && reader.FullCalls == 1, "no-early-native-opportunity");
                await Tick(100);
                var proof = Proof();
                basicBeforeDeadline = proof.ReferenceCurrent && proof.ObservedCount == 2 && reader.FullCalls == 1 && now.TotalMilliseconds < 250;
                Check(basicBeforeDeadline, "changed-basic-presented-before-next-full-deadline");
                Check(!Field<IDiagnosticInventoryReference>(main, "_diagnosticInventory").GrowthAttributionAvailable,
                    "changed-world-basic-does-not-borrow-old-growth");
                Check(proof.AutomaticCount == 0 && !proof.CoachCurrent && !proof.LiveSessionActive,
                    "basic-reference-never-upgrades-gameplay-authority");
                Check(timer.Interval == TimeSpan.FromMilliseconds(100), "real-timer-rearmed-for-basic-deadline");
                Save((FrameworkElement)main.Content, image);
                await Tick(200);
                Check(timer.Interval == TimeSpan.FromMilliseconds(50), "real-timer-keeps-independent-full-deadline");
                await Tick(250);
                fullCallsAtDeadline = reader.FullCalls;
                Check(fullCallsAtDeadline == 2 && Field<IDiagnosticInventoryReference>(main, "_diagnosticInventory").GrowthAttributionAvailable,
                    "full-completion-pairing-preserved-at-250ms");
                await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var pairedRows = ReadRows(log.CurrentPath!);
                Check(pairedRows.Count(row => IsLane(row, "memory.read", "full")) == 2 &&
                    pairedRows.Count(row => IsLane(row, "memory.read", "basic")) == 4,
                    "basic-only-reads-never-log-fabricated-full-completions");
                Check(pairedRows.Any(row => IsLane(row, "inventory.delta", "basic")) &&
                    pairedRows.Any(row => IsLane(row, "inventory.delta", "full")),
                    "basic-and-full-action-histories-advance-independently");
                foreach (var ms in new[] { 350, 450, 500, 600, 700, 750, 850, 950, 1000 }) await Tick(ms);
                Check(reader.FullCalls == 5 && reader.BasicCalls == 8, "five-full-eight-basic-calls-through-1000ms");

                var fullHold = reader.HoldFull = new Hold();
                now = TimeSpan.FromMilliseconds(1250);
                var slowFull = main.ScanControlledCadenceAsync();
                await fullHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var calls = reader.FullCalls + reader.BasicCalls;
                await Tick(5000); await Tick(5000);
                Check(reader.FullCalls + reader.BasicCalls == calls && reader.Active == 1, "slow-full-excludes-all-other-native-work");
                fullHold.Release.SetResult();
                await slowFull.WaitAsync(TimeSpan.FromSeconds(5));
                await Tick(5000);
                Check(reader.FullCalls + reader.BasicCalls == calls, "slow-full-no-catch-up-burst");

                var basicHold = reader.HoldBasic = new Hold();
                now = TimeSpan.FromMilliseconds(5100);
                var slowBasic = main.ScanControlledCadenceAsync();
                await basicHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                calls = reader.FullCalls + reader.BasicCalls;
                await Tick(6000); await Tick(6000);
                Check(reader.FullCalls + reader.BasicCalls == calls && reader.Active == 1, "slow-basic-excludes-full-and-basic");
                basicHold.Release.SetResult();
                await slowBasic.WaitAsync(TimeSpan.FromSeconds(5));
                await Tick(6000);
                Check(reader.FullCalls + reader.BasicCalls == calls + 1 && reader.FullStarts.Last() == now,
                    "slow-basic-retains-one-current-full-opportunity");
                await Tick(6000);
                Check(reader.FullCalls + reader.BasicCalls == calls + 1, "slow-basic-no-catch-up-burst");

                var cancelHold = reader.HoldBasic = new Hold();
                now = TimeSpan.FromMilliseconds(6100);
                var cancelled = main.ScanControlledCadenceAsync();
                await cancelHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                autoScan.IsChecked = false;
                main.StopControlledObservation();
                await cancelled.WaitAsync(TimeSpan.FromSeconds(5));
                Check(reader.Active == 0 && !Proof().ReferenceCurrent, "stop-cancels-active-basic-and-clears-presentation");
                autoScan.IsChecked = true;
                await Tick(6101);
                Check(Proof().ReferenceCurrent && reader.FullStarts.Last() == now, "restart-admits-new-full-without-old-deadline");

                var fullCancelHold = reader.HoldFull = new Hold();
                now += TimeSpan.FromMilliseconds(250);
                var fullCancelled = main.ScanControlledCadenceAsync();
                await fullCancelHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                autoScan.IsChecked = false;
                main.StopControlledObservation();
                await fullCancelled.WaitAsync(TimeSpan.FromSeconds(5));
                Check(reader.Active == 0 && !Proof().ReferenceCurrent, "stop-cancels-active-full-and-joins-stream");
                autoScan.IsChecked = true;
                await main.ScanControlledCadenceAsync();
                Check(Proof().ReferenceCurrent, "restart-after-full-cancellation");

                // A non-cooperating old source must still be fenced after replacement.
                var replacementHold = reader.HoldBasic = new Hold { IgnoreCancellation = true };
                now += TimeSpan.FromMilliseconds(100);
                var replaced = main.ScanControlledCadenceAsync();
                await replacementHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var replacement = new Reader(catalog, () => now) { Count = 7 };
                Set(main, "_recognizer", replacement);
                replacementHold.Release.SetResult();
                await replaced.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!Proof().ReferenceCurrent, "old-basic-completion-cannot-cross-recognizer-replacement");
                reader = replacement;
                await main.ScanControlledCadenceAsync();
                Check(Proof().ObservedCount == 7 && reader.FullCalls == 1, "replacement-recognizer-gets-immediate-full");

                var matchHold = reader.HoldFull = new Hold { IgnoreCancellation = true };
                now += TimeSpan.FromMilliseconds(250);
                var oldMatch = main.ScanControlledCadenceAsync();
                await matchHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Invoke(main, "ResetMatchSession");
                matchHold.Release.SetResult();
                await oldMatch.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!Proof().ReferenceCurrent, "old-full-completion-cannot-cross-new-match");
                await main.ScanControlledCadenceAsync();
                Check(Proof().ReferenceCurrent, "new-match-starts-with-new-full");

                var mapHold = reader.HoldBasic = new Hold { IgnoreCancellation = true };
                now += TimeSpan.FromMilliseconds(100);
                var oldMap = main.ScanControlledCadenceAsync();
                await mapHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                catalog.Load(mapVersion: "2.320");
                mapHold.Release.SetResult();
                await oldMap.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!Proof().ReferenceCurrent, "old-basic-completion-cannot-cross-selected-map-replacement");
                await main.ScanControlledCadenceAsync();
                Check(Proof().ReferenceCurrent, "selected-map-replacement-restarts-full-cadence");

                var fullMapHold = reader.HoldFull = new Hold { IgnoreCancellation = true };
                now += TimeSpan.FromMilliseconds(250);
                var oldFullMap = main.ScanControlledCadenceAsync();
                await fullMapHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                catalog.Load(mapVersion: "2.321");
                fullMapHold.Release.SetResult();
                await oldFullMap.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!Proof().ReferenceCurrent, "old-full-completion-cannot-cross-selected-map-replacement");
                await main.ScanControlledCadenceAsync();
                Check(Proof().ReferenceCurrent, "full-map-replacement-recovers");

                foreach (var state in new[] { RecognitionState.TransientReadError, RecognitionState.Waiting,
                    RecognitionState.Unsupported, RecognitionState.UnverifiedProfile, RecognitionState.ConfigurationError })
                {
                    var hard = state is RecognitionState.Unsupported or RecognitionState.UnverifiedProfile or RecognitionState.ConfigurationError;
                    reader = new Reader(catalog, () => now);
                    main.ConfigureControlledCadence(reader, () => now);
                    await main.ScanControlledCadenceAsync();
                    await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    var fullRowsBeforeFailure = ReadRows(log.CurrentPath!).Count(row => IsLane(row, "memory.read", "full"));
                    reader.Failure = new RecognitionResult { State = state, Diagnostics = new() { Detail = "synthetic preflight failure" } };
                    now += TimeSpan.FromMilliseconds(100);
                    await main.ScanControlledCadenceAsync();
                    Check(Proof().ReferenceCurrent == !hard && reader.FullCalls == 1, "basic-preflight-failure-policy-" + state);
                    await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    var failureRows = ReadRows(log.CurrentPath!);
                    Check(failureRows.Count(row => IsLane(row, "memory.read", "full")) == fullRowsBeforeFailure,
                        "basic-preflight-failure-does-not-log-full-read-" + state);
                    Check(failureRows.Any(row => IsLane(row, "observation.gap", "basic") &&
                        row.GetProperty("data").GetProperty("reason").GetString() == "Basic preflight failed: " + state &&
                        !row.GetProperty("data").GetProperty("correlationReset").GetBoolean()),
                        "basic-preflight-failure-records-independent-lane-gap-" + state);
                    if (!hard)
                    {
                        var deltas = failureRows.Count(row => IsLane(row, "inventory.delta", "full"));
                        reader.Failure = null; reader.Count = 2;
                        now += TimeSpan.FromMilliseconds(150);
                        await main.ScanControlledCadenceAsync();
                        await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                        Check(ReadRows(log.CurrentPath!).Count(row => IsLane(row, "inventory.delta", "full")) == deltas + 1,
                            "basic-preflight-failure-preserves-full-action-history-" + state);
                    }
                }
                now = TimeSpan.Zero;
                reader = new Reader(catalog, () => now);
                main.ConfigureControlledCadence(reader, () => now);
                await main.ScanControlledCadenceAsync();
                Check(reader.FullStarts.Single() == TimeSpan.Zero, "controlled-clock-reset-starts-fresh-full-cadence");
                await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var beforeRejection = ReadRows(log.CurrentPath!);
                reader.RejectBasic = true;
                now += TimeSpan.FromMilliseconds(100);
                await main.ScanControlledCadenceAsync();
                Check(Proof().ReferenceCurrent && reader.FullCalls == 1 && reader.BasicCalls == 1,
                    "native-basic-rejection-keeps-independently-fresh-reference");
                await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var afterRejection = ReadRows(log.CurrentPath!);
                Check(afterRejection.Count(row => IsLane(row, "memory.read", "full")) ==
                    beforeRejection.Count(row => IsLane(row, "memory.read", "full")),
                    "native-basic-rejection-does-not-fabricate-full-completion");
                Check(afterRejection.Any(row => IsLane(row, "observation.gap", "basic") &&
                    !row.GetProperty("data").GetProperty("correlationReset").GetBoolean()),
                    "native-basic-rejection-only-resets-basic-action-lane");
                reader.RejectBasic = false; reader.Count = 2;
                now += TimeSpan.FromMilliseconds(150);
                await main.ScanControlledCadenceAsync();
                await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Check(ReadRows(log.CurrentPath!).Count(row => IsLane(row, "inventory.delta", "full")) ==
                    beforeRejection.Count(row => IsLane(row, "inventory.delta", "full")) + 1,
                    "native-basic-rejection-preserves-full-action-history");
                reader = new Reader(catalog, () => now);
                main.ConfigureControlledCadence(reader, () => now);
                await main.ScanControlledCadenceAsync();
                reader.Failure = new RecognitionResult { State = RecognitionState.Waiting, ConfirmsSessionBoundary = true };
                now += TimeSpan.FromMilliseconds(100);
                await main.ScanControlledCadenceAsync();
                Check(!Proof().ReferenceCurrent, "confirmed-process-session-boundary-clears-basic-and-full");
                Check(reader.MaxActive == 1, "serialized-reader-admission");

                await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var rows = ReadRows(log.CurrentPath!);
                var reads = rows.Where(row => row.GetProperty("kind").GetString() == "memory.read").ToArray();
                Check(reads.Any(row => row.GetProperty("data").GetProperty("lane").GetString() == "basic"), "basic-action-log-lane-present");
                Check(reads.Any(row => row.GetProperty("data").GetProperty("lane").GetString() == "full"), "full-action-log-lane-present");
                success = true;
            }
            catch (Exception failure) { error = failure.ToString(); Console.Error.WriteLine(error); }
            finally
            {
                try
                {
                    Field<CancellationTokenSource?>(main, "_scanCancellation")?.Cancel();
                    await Field<TaskCompletionSource?>(main, "_activityScanFinished")!.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    main.Close();
                    await log.DisposeAsync();
                    var remaining = app.Windows.Count;
                    if (remaining != 0) { success = false; error += " Remaining fixture windows: " + remaining; }
                    File.WriteAllText(Path.Combine(output, "basic-cadence.json"), JsonSerializer.Serialize(new
                    {
                        success, synthetic = true, actualGameData = false, processId = Environment.ProcessId,
                        basicBeforeDeadline, fullCallsAtDeadline, remainingWindows = remaining, checks, error,
                        cleanup = new { ownedWindowsClosed = remaining == 0, processExitsWithTool = true }
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"BASIC CADENCE WPF {(success ? "PASS" : "FAIL")}; checks={checks.Count}; remainingWindows={remaining}");
                }
                catch (Exception cleanupError) { success = false; Console.Error.WriteLine(cleanupError); }
                app.Shutdown(success ? 0 : 1);
            }
        }));
        return app.Run();

        async Task Tick(int milliseconds)
        {
            now = TimeSpan.FromMilliseconds(milliseconds);
            await main.ScanControlledCadenceAsync();
        }
        DiagnosticInventoryUiProof Proof()
        {
            main.UpdateLayout();
            return main.CaptureDiagnosticInventoryUiProof();
        }
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Cadence fixture check failed: " + name);
            checks.Add(name);
        }
    }

    private sealed class Hold
    {
        internal TaskCompletionSource Entered { get; } = Signal();
        internal TaskCompletionSource Release { get; } = Signal();
        internal bool IgnoreCancellation { get; init; }
        internal async Task Wait(CancellationToken token)
        {
            Entered.TrySetResult();
            if (IgnoreCancellation) await Release.Task;
            else await Release.Task.WaitAsync(token);
        }
    }

    private sealed class Reader(DataCatalog catalog, Func<TimeSpan> elapsed) : IInventoryRecognizer, IModernBasicInventorySource
    {
        internal int Count = 1;
        internal int FullCalls, BasicCalls, Active, MaxActive;
        internal readonly List<TimeSpan> FullStarts = [];
        internal Hold? HoldFull, HoldBasic;
        internal RecognitionResult? Failure;
        internal bool RejectBasic;
        private long _basicRevision, _fullRevision;
        public Task<RecognitionResult> RecognizeAsync(AppSettings settings, CancellationToken token) =>
            throw new InvalidOperationException("Modern cadence must use typed lanes.");

        public async Task<DiagnosticBasicInventoryRead> RecognizeBasicAsync(AppSettings settings, CancellationToken token)
        {
            Enter(); BasicCalls++;
            try
            {
                var sample = Basic();
                if (RejectBasic)
                {
                    var value = sample.Observation;
                    sample = new(DiagnosticBasicInventoryObservation.Unavailable(value.SelectedMapVersion,
                        value.DatasetFingerprint, value.ExecutableVersion, value.ExecutableFingerprint,
                        value.SourceRevision, value.StartedAt, value.CompletedAt, value.ReadDuration,
                        "synthetic native basic validation rejected"), sample.Diagnostics);
                }
                var hold = HoldBasic; HoldBasic = null;
                if (hold is not null) await hold.Wait(token);
                return Failure is { } failure ? DiagnosticBasicInventoryRead.ForFailure(failure) : DiagnosticBasicInventoryRead.ForSample(sample);
            }
            finally { Active--; }
        }

        public async IAsyncEnumerable<DiagnosticRecognitionFrame> RecognizeFramesAsync(AppSettings settings,
            [EnumeratorCancellation] CancellationToken token)
        {
            Enter(); FullCalls++; FullStarts.Add(elapsed());
            try
            {
                var initial = Basic();
                yield return DiagnosticRecognitionFrame.ForBasic(initial.Observation, initial.Diagnostics);
                var completion = Basic();
                var now = DateTimeOffset.UtcNow;
                var full = new RecognitionResult { State = RecognitionState.Ready,
                    Entries = completion.Observation.CloneEntries(), CompletionBasicSample = completion,
                    Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName),
                    DiagnosticObservation = DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
                        Warcraft300Diagnostic.Hash, new string('C', 64), ++_fullRevision, 0, now, now, TimeSpan.Zero,
                        completion.Observation.CloneEntries(), [], null, completion.Observation.WorldStampFingerprint,
                        completion.Observation.BindingContextId) };
                var hold = HoldFull; HoldFull = null;
                if (hold is not null) await hold.Wait(token);
                yield return DiagnosticRecognitionFrame.ForCompleted(full);
            }
            finally { Active--; }
        }

        private DiagnosticBasicInventorySample Basic()
        {
            var now = DateTimeOffset.UtcNow;
            return new(DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
                Warcraft300Diagnostic.Hash, new string('A', 64), ++_basicRevision, 0, now, now, TimeSpan.Zero,
                [new() { UnitId = "rawcode:I10h", Count = Count }], Count.ToString("X64"), new string('A', 64)),
                Diagnostics(DiagnosticBasicInventoryObservation.SourceName));
        }
        private RecognitionDiagnostics Diagnostics(string source) => new()
        {
            Source = source, ProcessVersion = Warcraft300Diagnostic.Version, ExecutableSha256 = Warcraft300Diagnostic.Hash,
            ActivityRawcodes = ImmutableDictionary<string, int>.Empty.Add("I10h", Count)
        };
        private void Enter()
        {
            Active++; MaxActive = Math.Max(MaxActive, Active);
            if (Active != 1) throw new InvalidOperationException("Overlapping fake native operation.");
        }
    }

    private static bool IsLane(JsonElement row, string kind, string lane) =>
        row.GetProperty("kind").GetString() == kind &&
        row.GetProperty("data").TryGetProperty("lane", out var value) && value.GetString() == lane;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Private)!.SetValue(instance, value);
    private static void Invoke(object instance, string name) => instance.GetType().GetMethod(name, Private)!.Invoke(instance, null);
    private static List<JsonElement> ReadRows(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var rows = new List<JsonElement>();
        while (reader.ReadLine() is { } line)
        {
            using var json = JsonDocument.Parse(line); rows.Add(json.RootElement.Clone());
        }
        return rows;
    }
    private static void Save(FrameworkElement content, string path)
    {
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
