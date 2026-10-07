namespace OrandOverlay;

public partial class MainWindow
{
    private void TryHandleModernBasicInventoryObservation(DiagnosticBasicInventoryObservation value, RecognitionDiagnostics diagnostics)
    {
        Dispatcher.VerifyAccess();
        if (!UsesMap2320 || _diagnosticClosed) return;
        _diagnosticLaneActive = true;
        BindDiagnosticRecognizer();
        var basicReason = "unavailable";
        var accepted = !_coachPaused && AutoScanCheck.IsChecked == true &&
            _diagnosticReferences.TryAcceptBasic(value, diagnostics, DateTimeOffset.UtcNow,
                _catalog.MapVersion, _catalog.SelectedDatasetFingerprint, out basicReason);
        CaptureObservedRead(accepted, "basic", basicReason + " " + value.Reason, value.ReadDuration, value.SourceRevision);
        CaptureActivityRead("basic", value, diagnostics, accepted, basicReason);
        if (!accepted)
        {
            _diagnosticStatus = _coachPaused ? "인식 일시정지" : AutoScanCheck.IsChecked != true
                ? "자동 인식 꺼짐" : "유닛 읽기 실패 · 마지막 인식 유지";
            if (_coachPaused || AutoScanCheck.IsChecked != true)
                InvalidateDiagnosticInventoryObservation();
            else
            {
                if (_diagnosticReferences.Current(DateTimeOffset.UtcNow, _catalog.MapVersion,
                        _catalog.SelectedDatasetFingerprint) is null)
                {
                    CaptureObservedLoss(_diagnosticInventory);
                    _diagnosticInventory = null;
                }
                RenderDiagnosticInventoryReference();
            }
            return;
        }
        AcceptDiagnosticPresentation();
    }

    private bool TryHandleModernInventoryObservation(RecognitionResult result)
    {
        Dispatcher.VerifyAccess();
        if (!UsesMap2320)
        {
            if (_diagnosticLaneActive) { InvalidateDiagnosticInventoryObservation(); _diagnosticLaneActive = false; }
            return false;
        }
        _diagnosticLaneActive = true;
        if (_diagnosticClosed) return true;
        BindDiagnosticRecognizer();
        var reason = "Missing or unavailable typed diagnostic observation";
        var allowed = !_coachPaused && AutoScanCheck.IsChecked == true &&
            _diagnosticReferences.TryAcceptFull(result, DateTimeOffset.UtcNow, _catalog.MapVersion,
                _catalog.SelectedDatasetFingerprint, out reason);
        CaptureObservedRead(allowed, "full", reason + " " + result.DiagnosticObservation?.Reason,
            result.DiagnosticObservation?.ReadDuration ?? TimeSpan.Zero, result.DiagnosticObservation?.SourceRevision, result.State);
        if (result.DiagnosticObservation is { } activityValue)
            CaptureActivityRead("full", activityValue, result.Diagnostics, allowed, reason);
        else
        {
            RecordActivity("memory.read", new
            {
                Lane = "full", Accepted = false, State = result.State.ToString(),
                Reason = reason, result.Diagnostics.Detail
            });
            _activityCapture?.Gap(reason, _adaptivePlanning.MatchGeneration);
            _gameActivityCapture?.Reset();
        }
        if (result.ConfirmsSessionBoundary)
        {
            _observedCapture?.PresentationState("game-unavailable", "unavailable", DateTimeOffset.UtcNow);
            _observedCapture?.Reset();
        }
        if (!allowed)
        {
            _diagnosticStatus = _coachPaused ? "인식 일시정지" : AutoScanCheck.IsChecked != true
                ? "자동 인식 꺼짐" : DiagnosticReferencePresentationPolicy.RejectionStatus(result, reason);
            if (result.ConfirmsSessionBoundary || result.State is RecognitionState.Unsupported or
                RecognitionState.UnverifiedProfile or RecognitionState.ConfigurationError)
            {
                _diagnosticOverlaySession = false;
                InvalidateDiagnosticInventoryObservation(result.ConfirmsSessionBoundary);
            }
            else if (_coachPaused || AutoScanCheck.IsChecked != true)
                InvalidateDiagnosticInventoryObservation();
            else
            {
                if (_diagnosticReferences.Current(DateTimeOffset.UtcNow, _catalog.MapVersion,
                        _catalog.SelectedDatasetFingerprint) is null)
                {
                    CaptureObservedLoss(_diagnosticInventory);
                    _diagnosticInventory = null;
                }
                RenderDiagnosticInventoryReference();
            }
            return true; // Includes untyped/spoofed Ready: only a separately fresh basic reference can remain.
        }
        AcceptDiagnosticPresentation();
        return true;
    }

    private void BindDiagnosticRecognizer()
    {
        if (ReferenceEquals(_diagnosticRecognizer, _recognizer)) return;
        _diagnosticReferences = new();
        _diagnosticInventory = null;
        _diagnosticCandidates?.Reset();
        _diagnosticCandidateKey = "";
        _diagnosticRecognizer = _recognizer;
    }

    private void AcceptDiagnosticPresentation()
    {
        _diagnosticScanGeneration = _scanGeneration;
        _diagnosticMatchGeneration = _adaptivePlanning.MatchGeneration;
        _diagnosticOverlaySession = true;
        _diagnosticExpiry ??= CreateDiagnosticExpiryTimer();
        _diagnosticExpiry.Start();
        RenderDiagnosticInventoryReference();
    }
}
