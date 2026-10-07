using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void CaptureReadyBoundary(string output)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var main = new MainWindow(FixtureContext(new AppSettings
        {
            Mode = PlayMode.Beginner, TelemetryEnabled = false, AutoScanEnabled = true
        }));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var recognizer = new ReadyBoundaryRecognizer();

        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var rows = new List<object>();
        try
        {
            main.Show();
            var old = Observe("old-round65", "n006", new MapStateSample(65, 3, "악몽"));
            var candidate = Observe("unconfirmed-rollback", "n000", new MapStateSample(1, 0, "보통"));
            var boundary = Observe("confirmed-boundary", "n000", new MapStateSample(1, 0, "보통"));
            var next = Observe("new-round1", "n000", new MapStateSample(1, 0, "보통"));
            WriteEvidenceText(Path.Combine(output, "ready-boundary.json"), JsonSerializer.Serialize(new
            {
                Surface = "ScanControlledAsync -> CoachRendered; controlled tracker/scanner/ReadyDiagnostics inputs",
                Rows = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
            Require(old.Frame.Round == 65 && candidate.Frame.Round == 65, "Old session round was not seeded.");
            Require(candidate.Frame.MatchGeneration == old.Frame.MatchGeneration,
                "One rollback observation incorrectly reset the session.");
            Require(boundary.Payload.ConfirmsSessionBoundary && boundary.Payload.Diagnostics.MapState is null,
                "Confirmed boundary leaked pre-reset MapState.");
            Require(boundary.Frame.MatchGeneration > old.Frame.MatchGeneration && boundary.Frame.Round == 0 &&
                    boundary.Frame.Difficulty == "unknown" && boundary.Frame.Outcome == "unknown",
                "Confirmed boundary reimported old coach state.");
            Require(next.Frame.MatchGeneration == boundary.Frame.MatchGeneration && next.Frame.Round == 1 &&
                    next.Frame.Difficulty == "보통" && next.Frame.Outcome == "unknown" && next.Frame.IsCurrent,
                "Next session round 1 did not become current coach round 1.");
            Console.WriteLine("READY_BOUNDARY PASS coach=65,65,0,1; fresh difficulty; unknown outcome; one generation reset");
        }
        finally
        {
            main.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        (CoachFrame Frame, RecognitionResult Payload) Observe(string name, string objective, MapStateSample sample)
        {
            recognizer.Objective = objective;
            recognizer.Sample = sample;
            var expectedCount = recognizer.Calls + 1;
            var observed = WaitCoach(main,
                () => _ = ScanFixtureAsync(main, recognizer.RecognizeAsync(new AppSettings(), CancellationToken.None).GetAwaiter().GetResult()),
                (_, frame) => frame.IsCurrent && frame.Inventory.GetValueOrDefault("luffy_common") == expectedCount);
            var payload = recognizer.Last!;
            rows.Add(new { Name = name, payload.ConfirmsSessionBoundary, payload.Diagnostics.MapState,
                observed.Frame.Round, observed.Frame.MatchGeneration, observed.Frame.Difficulty,
                observed.Frame.Outcome, observed.Frame.IsCurrent, Kind = observed.Decision.Kind.ToString() });
            Console.WriteLine($"READY_BOUNDARY_STAGE {name} boundary={payload.ConfirmsSessionBoundary} " +
                $"payloadRound={payload.Diagnostics.MapState?.MaxRound.ToString() ?? "null"} " +
                $"coachRound={observed.Frame.Round} generation={observed.Frame.MatchGeneration} " +
                $"difficulty={observed.Frame.Difficulty} outcome={observed.Frame.Outcome}");
            main.UpdateLayout();
            SaveCoachWindow(main, Path.Combine(output, name + ".png"));
            overlay.Show();
            overlay.UpdateLayout();
            SaveCoachWindow(overlay, Path.Combine(output, name + "-overlay.png"));
            return (observed.Frame, payload);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    // Only the memory observation is controlled; session policy, outcome detection,
    // recommendation refresh, and main/overlay coach rendering remain production code.
    private sealed class ReadyBoundaryRecognizer : IInventoryRecognizer
    {
        private readonly MapSignalSnapshotTracker tracker = new(MapSignalRecognitionProfile.FromStory(
            MapStoryProfileLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"))));
        private readonly IncrementalMapStateScanner scanner = new(4096);
        public string Objective { get; set; } = "n006";
        public MapStateSample Sample { get; set; }
        public int Calls { get; private set; }
        public RecognitionResult? Last { get; private set; }

        public ReadyBoundaryRecognizer()
        {
            tracker.Observe(Snapshot(Objective));
            tracker.Observe(Snapshot(Objective));
        }

        public Task<RecognitionResult> RecognizeAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanner.Merge(Sample);
            var diagnostics = new RecognitionDiagnostics
            {
                Source = "controlled-memory", ObservedObjects = ++Calls, MappedObjects = Calls,
                ForeignObjects = 17, MapState = scanner.Current
            };
            var signals = tracker.Observe(Snapshot(Objective));
            var boundary = tracker.LastObservationConfirmedReset;
            if (boundary) scanner.Reset();
            Last = new RecognitionResult
            {
                State = RecognitionState.Ready, ConfirmsSessionBoundary = boundary,
                Entries = [new InventoryEntry { UnitId = "luffy_common", Count = Calls }],
                MapSignals = signals, Status = "제어된 세션 경계 검증",
                Diagnostics = WarcraftMemoryRecognitionService.ReadyDiagnostics(diagnostics, boundary)
            };
            return Task.FromResult(Last);
        }

        private static MapSignalRawSnapshot Snapshot(string objective)
        {
            if (!RawcodeCodec.TryParse(objective, out var code)) throw new ArgumentException(nameof(objective));
            return new MapSignalRawSnapshot([code], []);
        }
    }
}
