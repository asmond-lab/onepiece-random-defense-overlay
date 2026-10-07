using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrandOverlay;

internal static class TelemetryCapture
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    internal static int Run(string[] args)
    {
        var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        var fixture = OverlayExecutionContext.Fixture(new AppSettings { Mode = PlayMode.Normal, AutoScanEnabled = true,
            AutoUpdateEnabled = false, ClearDataAutoRefresh = false, ClickThroughOverlay = false });
        var app = new App { Execution = fixture, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent(); TelemetryConsentStartup.ClearStartupUri(app);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try { if (args[0] == "--consent") CaptureConsent(output); else CaptureTelemetry(output, fixture); return 0; }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Console.Error.WriteLine(error); return 1; }
        finally { app.Shutdown(); }
    }

    private static void CaptureConsent(string output)
    {
        var proofs = new List<object>();
        foreach (var size in new[] { (540d, 740d), (375d, 480d) })
        {
            var window = new TelemetryConsentWindow { Left = -5000, Top = 0, Width = size.Item1, Height = size.Item2 };
            window.Show(); Pump(Task.Delay(100)); window.UpdateLayout();
            var scroll = Children(window).OfType<ScrollViewer>().Single();
            var suffix = size.Item1.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Save((FrameworkElement)window.Content, Path.Combine(output, "consent-" + suffix + "-top.png"));
            scroll.ScrollToVerticalOffset(scroll.ScrollableHeight / 2); Pump(Task.Delay(30)); window.UpdateLayout();
            Save((FrameworkElement)window.Content, Path.Combine(output, "consent-" + suffix + "-middle.png"));
            scroll.ScrollToEnd(); Pump(Task.Delay(30)); window.UpdateLayout();
            Save((FrameworkElement)window.Content, Path.Combine(output, "consent-" + suffix + "-bottom.png"));
            foreach (var button in new[] { window.AgreeButton, window.DeclineButton })
            {
                var rect = button.TransformToAncestor((Visual)window.Content).TransformBounds(new Rect(button.RenderSize));
                Check(rect.Bottom <= ((FrameworkElement)window.Content).ActualHeight + 1, "Consent button clipped");
            }
            proofs.Add(new { Width = size.Item1, Height = size.Item2, Policy = TelemetryConsentPolicy.CurrentVersion,
                ButtonsVisible = true, Scrollable = scroll.ScrollableHeight > 0 });
            window.Close();
        }
        foreach (var agree in new[] { false, true })
        {
            var window = new TelemetryConsentWindow { Left = -5000, Top = 0 };
            window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() =>
                (agree ? window.AgreeButton : window.DeclineButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), DispatcherPriority.ApplicationIdle);
            Check(window.ShowDialog() == agree, "Consent choice mismatch");
        }
        File.WriteAllText(Path.Combine(output, "consent-checks.json"), JsonSerializer.Serialize(proofs, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS consent4 renders and accepts/declines without starting production runtime.");
    }

    private static void CaptureTelemetry(string output, OverlayExecutionContext fixture)
    {
        var main = new MainWindow(fixture, fixtureMapVersion: "2.320") { Left = -5000, Top = 0, Width = 1280, Height = 800 };
        main.Show(); Pump(Task.Delay(100));
        var catalog = Field<DataCatalog>(main, "_catalog");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var outbox = new ObservedTelemetryOutbox(() => true, output, http);
        var client = new ObservedTelemetryClient(() => true,
            new ObservedTelemetryMetadata(UpdateService.CurrentBuildVersion, catalog.OfflineBundle!.Fingerprint, Source: "synthetic-validation"), outbox);
        Set(main, "_observedTelemetry", client);
        var produced = new List<ObservedTelemetryEvent>();
        Set(main, "_observedCapture", new ObservationTelemetryCapture(e =>
        { var accepted = client.Record(e); if (accepted) produced.Add(e); return accepted; }, client.ResetSession));
        client.Start();
        var sessions = new List<string> { client.SessionId };
        long revision = 0;
        var context = new string('a',64);
        var entries = new List<InventoryEntry> { new() { UnitId = "rawcode:I10h", Count = 1 } };
        RecognitionResult Ready()
        {
            var now = DateTimeOffset.UtcNow;
            return new() { State = RecognitionState.Ready, Diagnostics = new() { Source = DiagnosticInventoryObservation.SourceName,
                ProcessVersion = Warcraft300Diagnostic.Version, ExecutableSha256 = Warcraft300Diagnostic.Hash },
                DiagnosticObservation = DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                    context, ++revision, 0, now.AddMilliseconds(-10), now, TimeSpan.FromMilliseconds(10), entries, [], 1, context, context) };
        }
        try
        {
            Pump(main.ScanControlledAsync(Ready()));
            Check(main.CaptureDiagnosticInventoryUiProof().ReferenceCurrent, "Initial reference missing");
            var browser = Field<NormalCandidateBrowser>(main, "_diagnosticCandidates");
            browser.Select(browser.Snapshot.Groups.SelectMany(g => g.Candidates).First().Unit.Id);
            Pump(main.ScanControlledAsync(Ready()));
            Pump(Task.Delay(3300));
            Check(!main.CaptureDiagnosticInventoryUiProof().ReferenceCurrent, "Expiry was not exercised");
            Pump(main.ScanControlledAsync(new RecognitionResult { State = RecognitionState.TransientReadError,
                Diagnostics = new() { Source = DiagnosticInventoryObservation.SourceName } }));
            entries[0].Count = 2;
            Pump(main.ScanControlledAsync(Ready()));
            Check(main.CaptureDiagnosticInventoryUiProof().ReferenceCurrent, "Recovery was not exercised");
            var basicNow = DateTimeOffset.UtcNow;
            DiagnosticBasicInventoryObservation Basic() => DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
                Warcraft300Diagnostic.Hash, context, ++revision, 0, DateTimeOffset.UtcNow.AddMilliseconds(-1), DateTimeOffset.UtcNow,
                TimeSpan.FromMilliseconds(1), entries, context, context);
            DiagnosticRecognitionFrame BasicFrame(DiagnosticBasicInventoryObservation value) => DiagnosticRecognitionFrame.ForBasic(value,
                new() { Source = DiagnosticBasicInventoryObservation.SourceName, ProcessVersion = Warcraft300Diagnostic.Version,
                    ExecutableSha256 = Warcraft300Diagnostic.Hash });
            async IAsyncEnumerable<DiagnosticRecognitionFrame> Frames(params DiagnosticRecognitionFrame[] values)
            { foreach (var value in values) { yield return value; await Task.CompletedTask; } }
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(Basic()), DiagnosticRecognitionFrame.ForCompleted(Ready()))));
            var beforeFailure = produced.Count;
            var unavailable = DiagnosticBasicInventoryObservation.Unavailable("2.320", catalog.OfflineBundle!.Fingerprint,
                Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, ++revision, basicNow, basicNow, TimeSpan.Zero, "fixture read failure");
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(unavailable), DiagnosticRecognitionFrame.ForCompleted(
                new RecognitionResult { State = RecognitionState.TransientReadError }))));
            Check(produced.Skip(beforeFailure).Any(e => e.Lane == "presentation" && e.State == "rejected"), "Basic failure lost display-gap event");
            Pump(Task.Delay(50));
            Pump(main.ScanControlledFramesAsync(Frames(BasicFrame(Basic()), DiagnosticRecognitionFrame.ForCompleted(Ready()))));
            Check(produced.Skip(beforeFailure).Any(e => e.Lane == "presentation" && e.State == "fresh" && e.GapMs >= 0), "Basic recovery lost gap duration");
            Save((FrameworkElement)main.Content, Path.Combine(output, "telemetry-recovered-main.png"));
            Pump(main.ScanControlledAsync(new RecognitionResult { State = RecognitionState.Waiting, ConfirmsSessionBoundary = true }));
            context = new string('c',64);
            Pump(main.ScanControlledAsync(Ready()));
            sessions.Add(client.SessionId);
            Pump(client.FlushAsync());
            Pump(client.StopAsync());
            Check(outbox.AcceptedPacketCount > 0, "Server did not acknowledge a packet");
            Check(outbox.LastError is null, "Outbox has upload error");
            File.WriteAllText(Path.Combine(output, "server-receipt.json"), JsonSerializer.Serialize(new {
                Source = "synthetic-validation", Version = UpdateService.CurrentBuildVersion, Sessions = sessions,
                outbox.AcceptedPacketCount, outbox.LastAcknowledgedAt, FixtureRuntime = fixture.RuntimeEnabled,
                GameRead = false, ExpiryAndRecovery = true, BasicFailureAndRecovery = true, SessionReset = sessions.Distinct().Count() == 2
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS actual WPF observation hooks -> durable client -> public server receipt (synthetic-validation).");
        }
        finally { Pump(client.DisposeAsync().AsTask()); main.Close(); Pump(Task.Delay(100)); }
    }

    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Private)!.GetValue(value)!;
    private static void Set(object value, string name, object item) => value.GetType().GetField(name, Private)!.SetValue(value, item);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root,i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    private static void Save(FrameworkElement element, string filename)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var rect = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            context.DrawRectangle(Window.GetWindow(element)?.Background ?? Brushes.Black, null, rect);
            context.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.Fill }, null, rect);
        }
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(filename); encoder.Save(stream);
    }
}
