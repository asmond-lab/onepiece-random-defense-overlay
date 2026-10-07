internal static class SoakDecisionPolicy
{
    internal const double AllowedRecordingGapSeconds = 10;

    internal static SoakDecision Evaluate(
        ProcessMetrics app, ProcessMetrics game, ActivityLogMetrics activity)
    {
        var reasons = new List<string>();
        if (!app.SurvivedInterval) reasons.Add("app-exited-before-duration");
        if (activity.ValidRecords < 2) reasons.Add("fewer-than-two-valid-records");
        if (activity.MalformedCompleteLines > 0) reasons.Add("malformed-complete-line");
        if (activity.MaximumReceiptGapSeconds > AllowedRecordingGapSeconds)
            reasons.Add("recording-gap-exceeded");
        var continuing = activity.ValidRecords >= 2 &&
            activity.MalformedCompleteLines == 0 &&
            activity.MaximumReceiptGapSeconds <= AllowedRecordingGapSeconds;
        var cleanWriter = activity.WriterLossIndicators == 0 &&
            activity.WriterErrorIndicators == 0;
        if (!cleanWriter) reasons.Add("writer-loss-or-error-observed");
        return new(reasons.Count == 0, app.SurvivedInterval, continuing, cleanWriter,
            game.SurvivedInterval,
            "No active-game-performance claim; game availability is separate and the map may be paused.",
            reasons);
    }
}
