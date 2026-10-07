using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using OrandOverlay;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is 0) throw new ArgumentException("Evidence directory is required.");
        var output = Path.GetFullPath(args[0]);
        if (args.Length == 2 && args[1] == "--basic-cadence") return BasicCadenceCapture.Run(output);
        if (args.Length == 2 && args[1] == "--unit-list-stability") return UnitListStabilityCapture.Run(output);
        var native = args.Skip(1).Contains("--native", StringComparer.Ordinal);
        var liveWisp = args.Skip(1).Contains("--live-wisp", StringComparer.Ordinal);
        if (native && liveWisp) throw new ArgumentException("Choose one capture scenario.");
        if (Directory.Exists(output)) throw new IOException("Evidence directory already exists.");
        var settings = new AppSettings
        {
            Mode = PlayMode.Normal, AutoScanEnabled = true, AutoUpdateEnabled = false,
            TelemetryEnabled = false, OverlayDisplayMode = OverlayDisplayMode.Hidden
        };
        var log = new LocalActivityLog(output);
        var fixture = OverlayExecutionContext.FixtureWithActivityLog(settings, log);
        var app = new App { Execution = fixture, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        TelemetryConsentStartup.ClearStartupUri(app);
        var main = new MainWindow(fixture, fixtureMapVersion: "2.321") { ShowActivated = false };
        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var nativeWindows = 0;
        NativeGuiEvidence? nativeEvidence = null;
        LiveWispReplayEvidence? liveWispEvidence = null;
        foreach (Window window in app.Windows) window.SourceInitialized += (_, _) => nativeWindows++;
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            string? error = null;
            var success = false;
            try
            {
                if (liveWisp)
                {
                    foreach (var frames in LiveWispReplayFixture.CreateScans(catalog))
                        await main.ScanControlledFramesAsync(frames);
                }
                else if (native)
                {
                    foreach (var frames in FullActivityFixture.CreateScans(catalog))
                        await main.ScanControlledFramesAsync(frames);
                }
                else
                {
                    for (var revision = 1; revision <= 2; revision++)
                    {
                        var now = DateTimeOffset.UtcNow;
                        var basic = DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
                            Warcraft300Diagnostic.Hash, new string('A', 64), revision, 0,
                            now.AddMilliseconds(-10), now, TimeSpan.FromMilliseconds(10),
                            [new() { UnitId = "rawcode:I10h", Count = revision }],
                            new string('B', 64), new string('A', 64));
                        var diagnostics = new RecognitionDiagnostics
                        {
                            Source = DiagnosticBasicInventoryObservation.SourceName,
                            ProcessVersion = Warcraft300Diagnostic.Version,
                            ExecutableSha256 = Warcraft300Diagnostic.Hash
                        };
                        async IAsyncEnumerable<DiagnosticRecognitionFrame> Frames()
                        {
                            yield return DiagnosticRecognitionFrame.ForBasic(basic, diagnostics);
                            await Task.CompletedTask;
                            yield return DiagnosticRecognitionFrame.ForCompleted(new()
                            {
                                State = RecognitionState.TransientReadError,
                                Diagnostics = new() { Source = DiagnosticInventoryObservation.SourceName, Detail = "fixture-full-unavailable" }
                            });
                        }
                        await main.ScanControlledFramesAsync(Frames());
                    }
                }
                main.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, main));
                if (native) nativeEvidence = await NativeGuiCapture.RunAsync(main, output);
                await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var rows = new List<JsonElement>();
                if (log.CurrentPath is { } path)
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    while (reader.ReadLine() is { } line)
                    {
                        using var document = JsonDocument.Parse(line);
                        rows.Add(document.RootElement.Clone());
                    }
                }
                var kinds = rows.Select(row => row.GetProperty("kind").GetString()).ToArray();
                if (liveWisp)
                {
                    liveWispEvidence = LiveWispReplayFixture.Evaluate(rows);
                    success = liveWispEvidence.Success && log.LastError is null;
                }
                else
                {
                    success = kinds.Contains("memory.read") && kinds.Contains("ui.presentation") &&
                        kinds.Contains("ui.control") && kinds.Contains("scan.completed") && log.LastError is null;
                    if (native)
                    {
                        var wisp = rows.FirstOrDefault(row =>
                            row.GetProperty("kind").GetString() == "game.wisp");
                        var craft = rows.FirstOrDefault(row =>
                            row.GetProperty("kind").GetString() == "game.craft");
                        var gamble = rows.FirstOrDefault(row =>
                            row.GetProperty("kind").GetString() == "game.gamble");
                        success &= nativeEvidence is { Minimized: true, Restored: true, ScreenshotWritten: true } &&
                            kinds.Contains("ui.window-state") &&
                            wisp.ValueKind == JsonValueKind.Object &&
                            !wisp.GetProperty("data").GetProperty("actionConfirmed").GetBoolean() &&
                            !wisp.GetProperty("data").GetProperty("outputsAttributed").GetBoolean() &&
                            craft.ValueKind == JsonValueKind.Object &&
                            !craft.GetProperty("data").GetProperty("actionConfirmed").GetBoolean() &&
                            gamble.ValueKind == JsonValueKind.Object &&
                            !gamble.GetProperty("data").GetProperty("actionConfirmed").GetBoolean() &&
                            !gamble.GetProperty("data").GetProperty("outputsAttributed").GetBoolean();
                    }
                }
                if (!success) error = "Required activity events missing: " + string.Join(",", kinds);
            }
            catch (Exception exception) { error = exception.ToString(); }
            finally
            {
                main.Close();
                await log.DisposeAsync();
                var remainingWindows = app.Windows.Count;
                success &= nativeWindows == (native ? 1 : 0) && remainingWindows == 0;
                string? reportPath = null;
                if (liveWisp)
                {
                    reportPath = Path.Combine(output, "live-wisp-replay.json");
                    File.WriteAllText(reportPath, JsonSerializer.Serialize(new
                    {
                        success, processId = Environment.ProcessId, nativeWindows, remainingWindows,
                        error, log.CurrentPath, evidence = liveWispEvidence,
                        cleanup = new { ownedWindowsClosed = remainingWindows == 0, processExitsWithTool = true }
                    }, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }));
                }
                else if (native)
                {
                    reportPath = Path.Combine(output, "native-actions.json");
                    File.WriteAllText(reportPath, JsonSerializer.Serialize(new
                    {
                        success, processId = Environment.ProcessId, nativeWindows, remainingWindows,
                        error, log.CurrentPath, evidence = nativeEvidence,
                        cleanup = new { ownedWindowsClosed = remainingWindows == 0, processExitsWithTool = true }
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    success, nativeWindows, remainingWindows, error, log.CurrentPath,
                    reportPath, screenshotPath = nativeEvidence?.ScreenshotPath
                }));
                app.Shutdown(success ? 0 : 1);
            }
        }), DispatcherPriority.Normal);
        return app.Run();
    }
}
