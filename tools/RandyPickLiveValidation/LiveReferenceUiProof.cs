using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrandOverlay;

// Optional validation-host presentation of the EXACT native producer result. No native access here.
internal sealed class LiveReferenceUiProof : IAsyncDisposable
{
    private const string Caption = "ACTUAL READ-ONLY GAME OBSERVATION | UNVERIFIED OWNERSHIP/LIFE | VALIDATION HOST";
    private static readonly TimeSpan PrepareBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CaptureBudget = TimeSpan.FromSeconds(8);
    private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(210));
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;
    private App? _app;
    private MainWindow? _main;
    private OverlayWindow? _overlay;
    private Exception? _shutdownFailure;
    private int _disposed;
    private int _shutdownRequested;
    private bool _windowsClosed;

    private LiveReferenceUiProof(AppSettings readSettings)
    {
        // Separate cloned fixture settings: never mutate native reader settings or its time/profile gates.
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(readSettings))!;
        settings.Mode = PlayMode.Normal;
        settings.AutoScanEnabled = true;
        settings.AutoUpdateEnabled = false;
        settings.TelemetryEnabled = false;
        settings.TelemetryConsentAccepted = false;
        settings.BeginnerCoachEnabled = false;
        settings.ClearDataAutoRefresh = false;
        settings.OverlayDisplayMode = OverlayDisplayMode.Full;
        settings.LastVisibleOverlayDisplayMode = OverlayDisplayMode.Full;
        settings.ClickThroughOverlay = false;
        _thread = new Thread(() => ThreadMain(settings)) { IsBackground = true, Name = "Actual-read validation WPF host" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    internal static async Task<LiveReferenceUiProof> PrepareAsync(AppSettings settings)
    {
        var host = new LiveReferenceUiProof(settings);
        host._thread.Start();
        try
        {
            await host._ready.Task.WaitAsync(PrepareBudget, host._lifetime.Token);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private void ThreadMain(AppSettings settings)
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(_dispatcher));
            using var cancellation = _lifetime.Token.Register(RequestShutdown);
            _lifetime.Token.ThrowIfCancellationRequested();
            var fixture = OverlayExecutionContext.Fixture(settings);
            if (fixture.RuntimeEnabled || fixture.LiveMemoryEnabled || fixture.HasCurrentConsent)
                throw new InvalidOperationException("UI host isolation failed.");
            _app = new App { Execution = fixture, ShutdownMode = ShutdownMode.OnExplicitShutdown };
            _app.InitializeComponent();
            TelemetryConsentStartup.ClearStartupUri(_app);
            if (_app.StartupUri is not null) throw new InvalidOperationException("Startup URI not cleared.");
            _main = new MainWindow(fixture, fixtureMapVersion: "2.320")
                { Width = 1280, Height = 800, ShowActivated = false, ShowInTaskbar = false };
            _overlay = (OverlayWindow)(typeof(MainWindow).GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException()).GetValue(_main)!;
            ConfigureWindow(_main, -10000);
            ConfigureWindow(_overlay, -12000);
            ConfigureWindow(_overlay.Stats, -14000);
            _overlay.Width = 600; _overlay.Height = 800;
            var layout = OverlayLayoutPolicy.StatsLayout(OverlayDisplayMode.Full);
            _overlay.Stats.Width = layout.Width; _overlay.Stats.Height = layout.Height;
            _main.Show();
            // Prewarm real HWNDs and empty visuals, without minting/injecting any observation.
            _overlay.Show(); _overlay.Stats.Show();
            Layout();
            _main.CaptureDiagnosticInventoryUiProof();
            Layout(); // The proof seam replaces controls, so layout must follow it.
            _dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try { _lifetime.Token.ThrowIfCancellationRequested(); Layout(); _ready.TrySetResult(); }
                catch (Exception error) { _ready.TrySetException(error); RequestShutdown(); }
            }));
            // No App.Run: no normal startup, mutex, consent UI, network or native reader.
            Dispatcher.Run();
        }
        catch (Exception error) { _ready.TrySetException(error); _shutdownFailure = error; }
        finally
        {
            CloseWindows();
            _ready.TrySetException(new OperationCanceledException("UI host stopped before preparation."));
            _stopped.TrySetResult();
        }
    }

    private static void ConfigureWindow(Window window, double left)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Topmost = false;
        window.Left = left; window.Top = -10000;
        // Production presentation may reapply saved/default placement. Keep only our APP windows offscreen.
        window.LocationChanged += (_, _) =>
        {
            if (window.Left != left) window.Left = left;
            if (window.Top != -10000) window.Top = -10000;
        };
    }

    internal async Task<bool> CaptureAsync(RecognitionResult actualResult, string output, int sample)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        cancellation.CancelAfter(CaptureBudget);
        try
        {
            var operation = _dispatcher!.InvokeAsync(() => CaptureOnDispatcherAsync(actualResult, output, sample, cancellation.Token),
                DispatcherPriority.Send, cancellation.Token);
            return await operation.Task.Unwrap().WaitAsync(CaptureBudget, cancellation.Token);
        }
        catch (Exception error)
        {
            cancellation.Cancel();
            RequestShutdown();
            // Separate filename: never race the dispatcher writer or print private exception messages.
            WriteJson(Path.Combine(output, $"sample-{sample}-ui-unavailable.json"), new {
                FixtureHostedActualRead = true, SyntheticFixture = false, CaptureValid = false,
                NormalInstalledApp300ActivationEnabled = false, ErrorType = error.GetType().Name, Caption
            });
            return false;
        }
    }

    private async Task<bool> CaptureOnDispatcherAsync(RecognitionResult result, string output, int sample, CancellationToken token)
    {
        _dispatcher!.VerifyAccess();
        DiagnosticInventoryUiProof? before = null, after = null;
        var started = DateTimeOffset.UtcNow;
        string? failureType = null;
        var files = new List<string>();
        bool valid = false;
        try
        {
            token.ThrowIfCancellationRequested();
            AssertFresh(result);
            // Never clone/recreate/retimestamp an observation, never call a recognition service on STA.
            await _main!.ScanControlledAsync(result);
            token.ThrowIfCancellationRequested();
            before = Proof();
            AssertProof(before, result);
            files.Add($"sample-{sample}-actual-main.png");
            Save((FrameworkElement)_main.Content, Path.Combine(output, files[^1]), token);
            AssertFresh(result);
            files.Add($"sample-{sample}-actual-stats.png");
            Save((FrameworkElement)_overlay!.Stats.Content, Path.Combine(output, files[^1]), token);
            // Includes PNG encoding and file writes, not just the initial render timestamp.
            after = Proof();
            AssertProof(after, result);
            token.ThrowIfCancellationRequested();
            valid = true;
        }
        catch (Exception error) { failureType = error.GetType().Name; }
        var finished = DateTimeOffset.UtcNow;
        if (!valid)
        {
            // Never leave an expired/partial PNG named as valid evidence.
            foreach (var name in files.ToArray())
            {
                var file = Path.Combine(output, name);
                if (!File.Exists(file)) { files.Remove(name); continue; }
                var invalidName = Path.GetFileNameWithoutExtension(name) + "-INVALID.png";
                File.Move(file, Path.Combine(output, invalidName));
                files[files.IndexOf(name)] = invalidName;
            }
        }
        var o = result.DiagnosticObservation;
        WriteJson(Path.Combine(output, $"sample-{sample}-ui-proof.json"), new {
            FixtureHostedActualRead = true, SyntheticFixture = false, Caption,
            CaptureValid = valid, CaptureInvalid = !valid, ErrorType = failureType,
            Surface = "Actual MainWindow content and StatsOverlayWindow content via ScanControlledAsync",
            Host = "Isolated STA WPF fixture; not the normal installed application",
            NormalInstalledApp300ActivationEnabled = false, ProductionSessionAllows = false,
            RuntimeEnabled = false, LiveMemoryEnabledInUiHost = false, TelemetryEnabled = false,
            AutoUpdateEnabled = false, AutoScanEnabled = true, Mode = "Normal Full",
            OriginalResultUnmodified = true, Source = o?.Source, TypedAvailability = o?.Availability.ToString(),
            SourceRevision = o?.SourceRevision, ObservationStartedAt = o?.StartedAt, ObservationCompletedAt = o?.CompletedAt,
            TypedObservedCount = o?.Entries.Sum(e => e.Count), FreshnessBudgetSeconds = 3,
            CaptureStartedAt = started, CaptureFinishedAt = finished, Before = before, After = after,
            LocalOwnership = "Unknown", Alive = "Unknown", GameplayReady = false, CanCoach = false,
            Files = files, StartupUriCleared = _app!.StartupUri is null
        });
        return valid;
    }

    private DiagnosticInventoryUiProof Proof()
    {
        var proof = _main!.CaptureDiagnosticInventoryUiProof();
        Layout(); // REQUIRED AFTER proof rerenders: never capture stale/replaced control geometry.
        return proof;
    }
    private void Layout() { _main!.UpdateLayout(); _overlay!.UpdateLayout(); _overlay.Stats.UpdateLayout(); }
    private static void AssertFresh(RecognitionResult result)
    {
        var o = result.DiagnosticObservation;
        if (o is null || o.Availability != DiagnosticInventoryAvailability.Ready ||
            !DiagnosticInventoryConsumerPolicy.CanPresent(o, DateTimeOffset.UtcNow, o.SelectedMapVersion,
                o.DatasetFingerprint, o.ContextId, o.SourceRevision, o.ViewSlot ?? -1))
            throw new InvalidOperationException("Actual observation unavailable or expired; no timestamp refresh allowed.");
    }
    private void AssertProof(DiagnosticInventoryUiProof proof, RecognitionResult result)
    {
        AssertFresh(result);
        if (!proof.ReferenceCurrent || proof.ObservedCount != result.DiagnosticObservation!.Entries.Sum(e => e.Count) ||
            proof.ObservedCount <= 0 || proof.AutomaticCount != 0 || proof.CoachCurrent || proof.LiveSessionActive ||
            proof.Outcome != "unknown" || !proof.RecommendationVisible || !proof.StatsVisible || !proof.NormalBrowserVisible ||
            !_main!.IsVisible || !_overlay!.IsVisible || !_overlay.Stats.IsVisible ||
            PresentationSource.FromVisual(_main) is null || PresentationSource.FromVisual(_overlay.Stats) is null)
            throw new InvalidOperationException("Current visible read-only UI proof failed.");
    }

    private static void Save(FrameworkElement content, string file, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        content.UpdateLayout();
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096) throw new InvalidOperationException("Invalid render bounds.");
        var actual = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        actual.Render(content);
        var text = new FormattedText(Caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), width < 450 ? 10 : 13, Brushes.Gold, 1)
            { MaxTextWidth = Math.Max(1, width - 16) };
        var captionHeight = Math.Max(44, (int)Math.Ceiling(text.Height) + 12);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height + captionHeight));
            dc.DrawText(text, new Point(8, 4));
            dc.DrawImage(actual, new Rect(0, captionHeight, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height + captionHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        token.ThrowIfCancellationRequested();
    }
    private static void WriteJson(string file, object value)
    {
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }
    private void CloseWindows()
    {
        if (_windowsClosed) return;
        _windowsClosed = true;
        try { _main?.Close(); } catch (Exception error) { _shutdownFailure ??= error; }
        try { _overlay?.Stats.CloseForApplication(); } catch (Exception error) { _shutdownFailure ??= error; }
        try { _overlay?.CloseForApplication(); } catch (Exception error) { _shutdownFailure ??= error; }
        try { _app?.Shutdown(); } catch (Exception error) { _shutdownFailure ??= error; }
    }
    private void RequestShutdown()
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _shutdownRequested, 1) != 0) return;
        try
        {
            dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                // Close windows/timers on their owner BEFORE terminating the dispatcher.
                try { CloseWindows(); }
                finally { if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
            }));
        }
        catch (InvalidOperationException) { }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        RequestShutdown();
        try
        {
            await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (_shutdownFailure is not null) throw new InvalidOperationException("UI host lifecycle failed.");
        }
        finally
        {
            // Always attempt a bounded join, even when preparation, capture or shutdown failed.
            var joined = _thread.Join(TimeSpan.FromSeconds(2));
            if (joined) _lifetime.Dispose();
            if (!joined) throw new TimeoutException("UI thread join exceeded budget; validation failed.");
        }
    }
}
