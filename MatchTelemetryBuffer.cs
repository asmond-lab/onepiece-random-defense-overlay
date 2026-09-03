namespace OrandOverlay;

/// <summary>한 판의 식별자 없는 집계만 유지하고 판당 한 번 v2 레코드를 만든다.</summary>
public sealed class MatchTelemetryBuffer
{
    private int _lastObservedObjectCount;
    private int _completedTopCount;
    private RecognitionTelemetryCounts _recognition;
    private DamageLane _damageLane = DamageLane.Unknown;
    private string _goalTier = "unknown";
    private RecommendationSurface _surface = RecommendationSurface.TopAndNavigation;
    private RecommendationUrgency _urgency = RecommendationUrgency.None;
    private bool _hasSnapshot;
    private bool _sent;

    public bool HasSnapshot => _hasSnapshot;
    public bool Sent => _sent;

    public void StartMatch()
    {
        if (_hasSnapshot || _sent) return;
        _recognition = default;
        _damageLane = DamageLane.Unknown;
        _goalTier = "unknown";
        _surface = RecommendationSurface.TopAndNavigation;
        _urgency = RecommendationUrgency.None;
    }

    public void ObserveRecognition(RecognitionState state)
    {
        if (_sent) return;
        _recognition = _recognition.Observe(state);
    }

    public void ObserveRecommendation(DamageLane damageLane, string goalTier,
        RecommendationSurface surface, RecommendationUrgency urgency)
    {
        if (_sent) return;
        _damageLane = damageLane;
        _goalTier = goalTier;
        _surface = surface;
        _urgency = urgency;
    }

    /// <summary>개별 패 구성 대신 coarse count만 마지막 정상 관측으로 갱신한다.</summary>
    public void Capture(int observedObjectCount, int completedTopCount)
    {
        if (_sent || observedObjectCount <= 0) return;
        _lastObservedObjectCount = observedObjectCount;
        _completedTopCount = Math.Max(0, completedTopCount);
        _hasSnapshot = true;
    }

    public TelemetryRecord? TryEmit(string appVersion, string mapVersion,
        string difficulty, string outcome)
    {
        if (_sent || !_hasSnapshot) return null;
        _sent = true;
        return MatchTelemetryRecorder.Build(
            appVersion,
            mapVersion,
            difficulty,
            _damageLane,
            _goalTier,
            _surface,
            _urgency,
            _lastObservedObjectCount,
            _completedTopCount,
            _recognition,
            outcome);
    }

    public void Reset()
    {
        _lastObservedObjectCount = 0;
        _completedTopCount = 0;
        _recognition = default;
        _damageLane = DamageLane.Unknown;
        _goalTier = "unknown";
        _surface = RecommendationSurface.TopAndNavigation;
        _urgency = RecommendationUrgency.None;
        _hasSnapshot = false;
        _sent = false;
    }
}
