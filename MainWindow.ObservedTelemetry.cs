using System.Windows;
using System.Windows.Threading;

namespace OrandOverlay;

public partial class MainWindow
{
    private ObservedTelemetryClient? _observedTelemetry;
    private ObservationTelemetryCapture? _observedCapture;
    private DispatcherTimer? _observedStatusTimer;

    private void InitializeObservedTelemetry()
    {
        _observedTelemetry = _execution.CreateObservedTelemetry(_catalog);
        if (_observedTelemetry is not { } client) return;
        _observedCapture = new(client.Record, client.ResetSession);
        client.Start();
        _observedStatusTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            (_, _) => UpdateObservedTelemetryStatus(), Dispatcher);
        _observedStatusTimer.Start();
        UpdateObservedTelemetryStatus();
    }

    private void CaptureObservedPresentation(IDiagnosticInventoryReference? previous)
    {
        if (_observedCapture is not { } capture) return;
        var now = DateTimeOffset.UtcNow;
        if (_diagnosticInventory is { } value)
            capture.Observe(value, _diagnosticMatchGeneration, _diagnosticCandidates, now,
                new[] { NormalBrowserView.DisplayedRecommendationId, _overlay.NormalView.DisplayedRecommendationId }.OfType<string>());
        else if (_coachPaused) capture.PresentationState("paused", "none", now);
        else if (AutoScanCheck.IsChecked != true) capture.PresentationState("auto-off", "none", now);
        else if (previous is not null)
        {
            var expired = now - previous.StartedAt >= DiagnosticInventoryObservation.FreshnessBudget;
            capture.PresentationState(expired ? "expired" : "rejected", expired ? "freshness" : "binding", now,
                age: ObservationTelemetryCapture.Milliseconds(now - previous.StartedAt));
        }
        UpdateObservedTelemetryStatus();
    }

    private void CaptureObservedLoss(IDiagnosticInventoryReference? previous)
    {
        if (previous is null) return;
        var now = DateTimeOffset.UtcNow;
        var expired = now - previous.StartedAt >= DiagnosticInventoryObservation.FreshnessBudget;
        _observedCapture?.PresentationState(expired ? "expired" : "rejected", expired ? "freshness" : "binding", now,
            age: ObservationTelemetryCapture.Milliseconds(now - previous.StartedAt));
    }

    private void CaptureObservedRead(bool accepted, string lane, string reason, TimeSpan duration, long? revision,
        RecognitionState? state = null)
    {
        _observedCapture?.Read(accepted, lane, accepted ? "none" : ObservationTelemetryCapture.Reason(reason, state),
            duration, revision, DateTimeOffset.UtcNow);
    }

    private void UpdateObservedTelemetryStatus()
    {
        if (TryShowLocalActivityRecordingWarning()) return;
        if (_observedTelemetry is not { } client) return;
        GameplayRecordingStatus.Text = ObservedTelemetryFooter.Format(client.IsBackpressured, client.LastError is not null);
        GameplayRecordingStatus.Foreground = client.IsBackpressured || client.LastError is not null
            ? OverlayTheme.PlanWarning : OverlayTheme.PlanSecondary;
        GameplayRecordingStatus.Visibility = Visibility.Visible;
    }

    private async Task StopObservedTelemetryAsync()
    {
        _observedStatusTimer?.Stop();
        if (_observedTelemetry is not { } client) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { await client.StopAsync(timeout.Token); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
    }
}
