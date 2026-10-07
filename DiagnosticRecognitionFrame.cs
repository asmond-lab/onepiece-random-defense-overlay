namespace OrandOverlay;

internal interface IModernInventoryFrameSource
{
    IAsyncEnumerable<DiagnosticRecognitionFrame> RecognizeFramesAsync(AppSettings settings, CancellationToken token);
}

// Opt-in only: a basic call never implies that growth/full recognition completed.
internal interface IModernBasicInventorySource : IModernInventoryFrameSource
{
    Task<DiagnosticBasicInventoryRead> RecognizeBasicAsync(AppSettings settings, CancellationToken token);
}

internal sealed record DiagnosticBasicInventoryRead
{
    public DiagnosticBasicInventorySample? Sample { get; }
    public RecognitionResult? Failure { get; }

    private DiagnosticBasicInventoryRead(DiagnosticBasicInventorySample? sample, RecognitionResult? failure)
    {
        Sample = sample; Failure = failure;
    }

    internal static DiagnosticBasicInventoryRead ForSample(DiagnosticBasicInventorySample sample) => new(sample, null);
    internal static DiagnosticBasicInventoryRead ForFailure(RecognitionResult failure)
    {
        if (failure.State == RecognitionState.Ready)
            throw new ArgumentException("A basic-only read cannot return a successful full result.", nameof(failure));
        return new(null, failure);
    }
}

internal sealed record DiagnosticRecognitionFrame
{
    public RecognitionResult? CompletedResult { get; }
    public DiagnosticBasicInventoryObservation? Basic { get; }
    public RecognitionDiagnostics Diagnostics { get; }

    private DiagnosticRecognitionFrame(RecognitionResult? completedResult,
        DiagnosticBasicInventoryObservation? basic, RecognitionDiagnostics diagnostics)
    {
        CompletedResult = completedResult; Basic = basic; Diagnostics = diagnostics;
    }

    public static DiagnosticRecognitionFrame ForBasic(DiagnosticBasicInventoryObservation basic, RecognitionDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(basic); ArgumentNullException.ThrowIfNull(diagnostics);
        return new(null, basic, diagnostics);
    }

    public static DiagnosticRecognitionFrame ForCompleted(RecognitionResult completedResult)
    {
        ArgumentNullException.ThrowIfNull(completedResult);
        return new(completedResult, null, completedResult.Diagnostics);
    }
}
