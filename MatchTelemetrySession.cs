namespace OrandOverlay;

/// <summary>매치 동안 식별자 없는 관측·추천 집계를 모아 다음 실행에서 전송할 큐에 넣는다.</summary>
internal sealed class MatchTelemetrySession(TelemetryUploader uploader)
{
    private readonly MatchTelemetryBuffer _buffer = new();

    public void MarkSessionStart()
    {
        _buffer.StartMatch();
    }

    public void ObserveRecognition(RecognitionState state) =>
        _buffer.ObserveRecognition(state);

    public void ObserveRecommendation(DamageLane damageLane, string goalTier,
        RecommendationSurface surface, RecommendationUrgency urgency) =>
        _buffer.ObserveRecommendation(damageLane, goalTier, surface, urgency);

    public void Capture(int observedObjectCount, int completedTopCount)
    {
        try { _buffer.Capture(observedObjectCount, completedTopCount); }
        catch { /* 추천 경로를 방해하지 않는다. */ }
    }

    public void Send(string appVersion, string mapVersion, string difficulty,
        string outcome)
    {
        try
        {
            var record = _buffer.TryEmit(
                appVersion, mapVersion, difficulty, outcome);
            if (record is not null) uploader.Enqueue(record);
        }
        catch { /* 추천 경로를 방해하지 않는다. */ }
    }

    public void Reset() => _buffer.Reset();
}
