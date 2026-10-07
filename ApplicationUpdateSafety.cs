namespace OrandOverlay;

internal static class ApplicationUpdateSafety
{
    internal static readonly TimeSpan MaximumIdleAge = TimeSpan.FromSeconds(10);

    internal static bool IsPositiveIdle(RecognitionResult result) =>
        result.State == RecognitionState.Waiting && result.ConfirmsSessionBoundary;

    internal static bool ShouldDefer(bool uiReady, bool closing, bool liveSession, bool scanInProgress,
        bool liveMemoryEnabled, bool observationPending, long idleObservedUtcTicks, DateTime nowUtc)
    {
        if (!uiReady || closing || liveSession || scanInProgress) return true;
        if (!liveMemoryEnabled) return false;
        if (observationPending || idleObservedUtcTicks <= 0 || idleObservedUtcTicks > DateTime.MaxValue.Ticks)
            return true;
        var age = nowUtc.Ticks - idleObservedUtcTicks;
        return age < 0 || age > MaximumIdleAge.Ticks;
    }
}
