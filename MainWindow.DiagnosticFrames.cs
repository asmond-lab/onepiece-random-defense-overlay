namespace OrandOverlay;

public partial class MainWindow
{
    internal Task ScanControlledFramesAsync(IAsyncEnumerable<DiagnosticRecognitionFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (_runtimeEffects || !_controlledRecognitionAllowed)
            throw new InvalidOperationException("Controlled recognition requires a non-runtime window and an existing absolute sandbox directory.");
        return ScanCoreAsync(null, frames);
    }

    private async Task<RecognitionResult> ReadRecognitionFramesAsync(IInventoryRecognizer recognizer,
        IAsyncEnumerable<DiagnosticRecognitionFrame>? controlledFrames, int generation, long matchGeneration,
        CancellationToken cancellationToken)
    {
        if (controlledFrames is null && (!UsesMap2320 || recognizer is not IModernInventoryFrameSource))
            return await recognizer.RecognizeAsync(_settings, cancellationToken);
        var frames = controlledFrames ?? ((IModernInventoryFrameSource)recognizer).RecognizeFramesAsync(_settings, cancellationToken);
        RecognitionResult? completed = null;
        var sawBasic = false;
        await foreach (var frame in frames.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dispatcher.VerifyAccess();
            RecordActivity("frame.received", new
            {
                Lane = frame.Basic is null ? "full" : "basic", ScanGeneration = generation,
                ScanMatchGeneration = matchGeneration,
                StartedAt = frame.Basic?.StartedAt ?? frame.CompletedResult?.DiagnosticObservation?.StartedAt,
                CompletedAt = frame.Basic?.CompletedAt ?? frame.CompletedResult?.DiagnosticObservation?.CompletedAt
            }, frame.Basic?.SourceRevision ?? frame.CompletedResult?.DiagnosticObservation?.SourceRevision);
            if (_diagnosticClosed || _coachPaused || AutoScanCheck.IsChecked != true ||
                !IsCurrentRecognition(generation, _scanGeneration, matchGeneration,
                    _adaptivePlanning.MatchGeneration, ReferenceEquals(recognizer, _recognizer)))
                throw new OperationCanceledException("Recognition frame no longer belongs to the active scan.", cancellationToken);
            if (completed is not null) throw new InvalidOperationException("Recognition stream continued after its final result.");
            if (frame.Basic is { } basic)
            {
                sawBasic = true;
                TryHandleModernBasicInventoryObservation(basic, frame.Diagnostics);
            }
            else if (frame.CompletedResult is { } result)
            {
                // A process/module read can fail before yielding a basic frame. Let the
                // completion consumer retain its independently fresh basic reference;
                // it still enforces expiry and confirmed session/hard boundaries.
                if (!sawBasic && result.CompletionBasicSample is null && _diagnosticReferences.HasBasicFrames &&
                    result.State != RecognitionState.TransientReadError)
                    InvalidateDiagnosticInventoryObservation();
                completed = result;
            }
            else throw new InvalidOperationException("Recognition stream yielded an empty frame.");
        }
        return completed ?? throw new InvalidOperationException("Recognition stream ended without its final result.");
    }
}
