namespace OrandOverlay;

// Dispatcher-owned: at most one follow-up, after the preceding native read has finished.
internal sealed class DiagnosticScanContinuation
{
    private bool _pending;

    internal bool TryQueue(RecognitionResult result, TimeSpan elapsed, bool nativeRead,
        Action<Action> enqueue, Func<bool> canContinue, Action scan)
    {
        if (_pending || !nativeRead || elapsed < MainWindow.RecognitionInterval ||
            result.Diagnostics.Source != DiagnosticInventoryObservation.SourceName ||
            result.State is not (RecognitionState.Ready or RecognitionState.TransientReadError) ||
            !canContinue()) return false;
        _pending = true;
        try
        {
            enqueue(() =>
            {
                _pending = false;
                if (canContinue()) scan();
            });
        }
        catch
        {
            _pending = false;
            throw;
        }
        return true;
    }
}
