using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using OrandOverlay;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args[2]);
        if (File.Exists(output)) throw new IOException("Evidence already exists.");
        var settings = new AppSettings
        {
            Mode = PlayMode.Normal, AutoScanEnabled = true, AutoUpdateEnabled = false,
            TelemetryEnabled = false, ClearDataAutoRefresh = false,
            OverlayDisplayMode = OverlayDisplayMode.Hidden
        };
        var fixture = OverlayExecutionContext.Fixture(settings);
        var app = new App { Execution = fixture, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        TelemetryConsentStartup.ClearStartupUri(app);
        var main = new MainWindow(fixture, fixtureMapVersion: "2.321") { ShowActivated = false };
        var catalog = (DataCatalog)typeof(MainWindow)
            .GetField("_catalog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var reader = new WarcraftMemoryRecognitionService(catalog,
            Path.Combine(Path.GetDirectoryName(output)!, "ui-isolated-reader"),
            new ExpectedReadTarget(int.Parse(args[0]), DateTimeOffset.Parse(args[1])));
        reader.ActivityRules = Map2321ActivityRules.LoadBundled();
        var events = new List<object>();
        var cycles = new List<object>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var clock = Stopwatch.StartNew();
        var nativeWindows = 0;
        foreach (Window window in app.Windows) window.SourceInitialized += (_, _) => nativeWindows++;
        string? error = null;
        var acceptedCycles = 0;
        AppDisplayObserver? display = null;
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                if (args.Length > 3) display = new AppDisplayObserver(int.Parse(args[3]));
                Console.WriteLine("UI_PROBE_READY");
                for (var cycle = 1; cycle <= (display is null ? 30 : 60); cycle++)
                {
                    double fullDeliveredMs = 0;
                    async IAsyncEnumerable<DiagnosticRecognitionFrame> Frames()
                    {
                        await foreach (var frame in ((IModernInventoryFrameSource)reader)
                            .RecognizeFramesAsync(settings, timeout.Token))
                        {
                            var arrived = clock.Elapsed.TotalMilliseconds;
                            var arrivedUtc = DateTimeOffset.UtcNow;
                            var started = Stopwatch.GetTimestamp();
                            yield return frame;
                            var consumerMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                            if (frame.CompletedResult is not null) fullDeliveredMs = arrived;
                            events.Add(new
                            {
                                cycle, lane = frame.Basic is null ? "full" : "basic", arrived, arrivedUtc,
                                consumerMs, basicReadMs = frame.Basic?.ReadDuration.TotalMilliseconds,
                                count = frame.Basic?.Entries.Sum(x => x.Count),
                                fullCount = frame.CompletedResult?.DiagnosticObservation?.Entries.Sum(x => x.Count),
                                state = frame.CompletedResult?.State.ToString(),
                                counterStatus = frame.CompletedResult?.Diagnostics.ActivityCounterStatus,
                                counters = frame.CompletedResult?.Diagnostics.ActivityCounters,
                                unavailableCounterNames = frame.CompletedResult?.Diagnostics.ActivityUnavailableCounterNames,
                                counterReadCalls = frame.CompletedResult?.Diagnostics.ActivityCounterReadCalls,
                                counterReadBytes = frame.CompletedResult?.Diagnostics.ActivityCounterReadBytes,
                                counterReadDurationMs = frame.CompletedResult?.Diagnostics.ActivityCounterReadDurationMs,
                                rawcodes = frame.Basic is not null ? frame.Diagnostics.ActivityRawcodes :
                                    frame.CompletedResult?.Diagnostics.ActivityProjectedRawcodes,
                                cards = frame.Basic?.Entries.Select(x => new { x.UnitId, x.Count }).ToArray()
                            });
                        }
                    }
                    var started = clock.Elapsed.TotalMilliseconds;
                    await main.ScanControlledFramesAsync(Frames());
                    var done = clock.Elapsed.TotalMilliseconds;
                    var proof = main.CaptureDiagnosticInventoryUiProof();
                    if (proof.ReferenceCurrent) acceptedCycles++;
                    cycles.Add(new
                    {
                        cycle, started, done, completionConsumerMs = done - fullDeliveredMs,
                        proof.ReferenceCurrent, proof.ObservedCount
                    });
                    // Probe pacing only; this is not the application's timer and is not a test assertion.
                    await Task.Delay(250, timeout.Token);
                }
            }
            catch (Exception ex) { error = ex.ToString(); }
            finally
            {
                timeout.Cancel();
                display?.Dispose();
                main.Close();
                var remainingWindows = app.Windows.Count;
                var success = error is null && acceptedCycles > 0 && nativeWindows == 0 && remainingWindows == 0;
                File.WriteAllText(output, JsonSerializer.Serialize(new
                {
                    success, error, acceptedCycles, nativeWindows, remainingWindows,
                    scope = "Live frames through hidden WPF; handler cost only, not pixels or click latency",
                    appDisplay = display?.Events, events, cycles
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(JsonSerializer.Serialize(new { success, acceptedCycles, nativeWindows, remainingWindows, error }));
                app.Shutdown(success ? 0 : 1);
            }
        }), DispatcherPriority.Normal);
        return app.Run();
    }
}
