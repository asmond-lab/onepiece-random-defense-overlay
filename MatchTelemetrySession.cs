namespace OrandOverlay;

/// <summary>
/// 매치 텔레메트리 세션 상태(스냅샷 버퍼·세션 시작 시각·마지막 상위 추천)를 소유하고
/// 판 종료 레코드 생성·전송을 담당한다. MainWindow에서 동작 보존으로 추출 —
/// 라이브 세션 여부 등 게이트 판단은 호출부(MainWindow)가 유지한다.
/// </summary>
internal sealed class MatchTelemetrySession(TelemetryUploader uploader)
{
    private readonly MatchTelemetryBuffer _buffer = new();
    private DateTimeOffset _sessionStart;
    private IReadOnlyList<string> _lastTop = [];

    /// <summary>새 매치 세션 시작 시각을 기록한다.</summary>
    public void MarkSessionStart() => _sessionStart = DateTimeOffset.UtcNow;

    /// <summary>이번 스캔의 상위 추천 5종을 기록한다(판 종료 레코드의 TopRecommendations).</summary>
    public void ObserveTopRecommendations(IEnumerable<string> top) => _lastTop = top.ToList();

    /// <summary>패가 있는 스캔만 스냅샷에 남긴다. 전멸·대기 스캔은 마지막 패를 덮지 않는다.</summary>
    public void Capture(IReadOnlyList<InventoryEntry> hand,
        IReadOnlyCollection<string> completedTops, int observedUnitCount)
    {
        try
        {
            _buffer.Capture(hand, completedTops, _lastTop, _sessionStart, observedUnitCount);
        }
        catch { /* fail-silent */ }
    }

    /// <summary>판 종료 시 세션 상태로 레코드를 만들어 큐에 넣고 플러시한다.</summary>
    public void Send(string anonId, string appVersion, string mapVersion,
        string warcraftVersion, string goalUnitId, string navigationMode,
        string goroseiMode, string buildVariant, string difficulty,
        string outcome, string outcomeSource)
    {
        try
        {
            var record = _buffer.TryEmit(
                anonId, appVersion, mapVersion,
                warcraftVersion, goalUnitId, navigationMode, goroseiMode,
                buildVariant, difficulty,
                DateTimeOffset.UtcNow, outcome, outcomeSource);
            if (record is null) return;
            _ = uploader.EnqueueAndFlushAsync(record);
        }
        catch { /* fail-silent */ }
    }

    /// <summary>세션 경계에서 상태를 원점으로 되돌린다.</summary>
    public void Reset()
    {
        _buffer.Reset();
        _sessionStart = default;
        _lastTop = [];
    }
}
