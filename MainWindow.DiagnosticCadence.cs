namespace OrandOverlay;

public partial class MainWindow
{
    private DiagnosticInventoryCadence _diagnosticCadence = new();
    private (int Scan, long Match, IInventoryRecognizer Recognizer, string Map, string Fingerprint)? _cadenceContext;

    internal void ConfigureControlledCadence(IInventoryRecognizer recognizer, Func<TimeSpan> elapsed)
    {
        RequireControlledCadence();
        if (_scanInProgress) throw new InvalidOperationException("Cannot replace a running cadence clock.");
        _recognizer = recognizer;
        _diagnosticCadence = new(elapsed);
        _cadenceContext = null;
    }

    internal Task ScanControlledCadenceAsync()
    {
        RequireControlledCadence();
        return ScanCoreAsync(null, scheduled: true);
    }

    private void RequireControlledCadence()
    {
        Dispatcher.VerifyAccess();
        if (_runtimeEffects || !_controlledRecognitionAllowed)
            throw new InvalidOperationException("Controlled cadence requires a non-runtime window and an existing absolute sandbox directory.");
    }

    private DiagnosticReadLane? BeginDiagnosticCadence(IInventoryRecognizer recognizer)
    {
        var context = (_scanGeneration, _adaptivePlanning.MatchGeneration, recognizer,
            _catalog.MapVersion, _catalog.SelectedDatasetFingerprint);
        if (_cadenceContext != context)
        {
            _diagnosticCadence.Reset();
            _cadenceContext = context;
        }
        return _diagnosticCadence.TryBegin();
    }

    private void EnsureCurrentDiagnosticRead(IInventoryRecognizer recognizer, int generation,
        long matchGeneration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Dispatcher.VerifyAccess();
        if (_diagnosticClosed || _coachPaused || AutoScanCheck.IsChecked != true ||
            !IsCurrentRecognition(generation, _scanGeneration, matchGeneration,
                _adaptivePlanning.MatchGeneration, ReferenceEquals(recognizer, _recognizer)))
            throw new OperationCanceledException("Recognition frame no longer belongs to the active scan.", token);
    }

    private async Task ReadBasicInventoryAsync(IInventoryRecognizer recognizer, int generation,
        long matchGeneration, CancellationToken token)
    {
        var read = await ((IModernBasicInventorySource)recognizer).RecognizeBasicAsync(_settings, token);
        EnsureCurrentDiagnosticRead(recognizer, generation, matchGeneration, token);
        if (read.Sample is { } sample)
        {
            RecordActivity("frame.received", new
            {
                Lane = "basic", ScanGeneration = generation, ScanMatchGeneration = matchGeneration,
                sample.Observation.StartedAt, sample.Observation.CompletedAt
            }, sample.Observation.SourceRevision);
            TryHandleModernBasicInventoryObservation(sample.Observation, sample.Diagnostics);
        }
        else if (read.Failure is { } failure)
        {
            ObserveApplicationUpdateSafety(failure);
            UpdateCompatibilityInfo(failure);
            // No full read occurred. Keep lane histories and completion pairing independent.
            CaptureObservedRead(false, "basic", failure.Diagnostics.Detail, TimeSpan.Zero, null, failure.State);
            var failedAt = DateTimeOffset.UtcNow;
            var unavailable = DiagnosticBasicInventoryObservation.Unavailable(_catalog.MapVersion,
                _catalog.SelectedDatasetFingerprint, failure.Diagnostics.ProcessVersion ?? "",
                failure.Diagnostics.ExecutableSha256 ?? "", 0, failedAt, failedAt, TimeSpan.Zero,
                "Basic preflight failed: " + failure.State);
            CaptureActivityRead("basic", unavailable, failure.Diagnostics, false, unavailable.Reason);
            _diagnosticStatus = DiagnosticReferencePresentationPolicy.RejectionStatus(failure, failure.Diagnostics.Detail);
            if (failure.ConfirmsSessionBoundary || failure.State is RecognitionState.Unsupported or
                RecognitionState.UnverifiedProfile or RecognitionState.ConfigurationError)
            {
                _diagnosticOverlaySession = false;
                InvalidateDiagnosticInventoryObservation(failure.ConfirmsSessionBoundary);
            }
            else RenderDiagnosticInventoryReference(); // Retain only independently fresh references.
        }
        else throw new InvalidOperationException("Basic read produced neither a sample nor a failure.");
    }
}
