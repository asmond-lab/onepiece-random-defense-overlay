using System.ComponentModel;

namespace OrandOverlay;

internal sealed class WarcraftRouteQuestReader
{
    private readonly Func<ulong, int, byte[]>? _testRead;
    private readonly Func<IEnumerable<(ulong Base, byte[] Buffer)>>? _testChunks;
    private Dictionary<string, ulong>? _nodes;
    private DateTime _retryAfterUtc;
    private string _lastFailure = "항로개척 자동 확인 준비 중";

    internal WarcraftRouteQuestReader() { }
    internal WarcraftRouteQuestReader(Func<ulong, int, byte[]> read,
        Func<IEnumerable<(ulong Base, byte[] Buffer)>> chunks) => (_testRead, _testChunks) = (read, chunks);

    internal void Reset()
    {
        _nodes = null;
        _retryAfterUtc = DateTime.MinValue;
    }

    internal RouteQuestSnapshot Read(ReadOnlyProcessMemory memory, string version, string mapScriptSha256,
        byte? verifiedLocalPlayerSlot, CancellationToken token = default) => ReadCore(memory.ReadAvailable,
            () => memory.ReadChunks(memory.ReadablePrivateRegions(), 4 * 1024 * 1024, 64),
            version, mapScriptSha256, verifiedLocalPlayerSlot, token);

    internal RouteQuestSnapshot Read(string version, string mapScriptSha256,
        byte? verifiedLocalPlayerSlot, CancellationToken token = default) => ReadCore(
            _testRead ?? throw new InvalidOperationException("메모리 읽기 연결 없음"),
            _testChunks ?? throw new InvalidOperationException("메모리 영역 연결 없음"),
            version, mapScriptSha256, verifiedLocalPlayerSlot, token);

    private RouteQuestSnapshot ReadCore(Func<ulong, int, byte[]> read,
        Func<IEnumerable<(ulong Base, byte[] Buffer)>> chunks, string version, string mapScriptSha256,
        byte? localSlot, CancellationToken token)
    {
        if (version != "2.0.4.23745" || mapScriptSha256 != RouteQuestCatalog.MapScriptSha256 || localSlot is null or > 3)
            return RouteQuestSnapshot.Unavailable("항로개척 자동 확인: 게임 버전·맵·내 플레이어 검증 필요");
        if (_nodes is null && DateTime.UtcNow < _retryAfterUtc)
            return RouteQuestSnapshot.Unavailable(_lastFailure);
        try
        {
            var memory = new RouteQuestMemory(read);
            _nodes ??= QuestGlobalDiscovery.Find(memory, chunks, token);
            var first = ReadSnapshot(memory, _nodes, localSlot.Value, token);
            var second = ReadSnapshot(memory, _nodes, localSlot.Value, token);
            if (!first.Assigned.SequenceEqual(second.Assigned))
                throw new InvalidDataException("퀘스트 상태 변경 중");
            return second;
        }
        catch (Exception exception) when (exception is InvalidDataException or Win32Exception or OverflowException)
        {
            _nodes = null;
            _retryAfterUtc = DateTime.UtcNow.AddSeconds(10);
            _lastFailure = $"항로개척 미확인 · {exception.Message} · 보상 계산 제외";
            return RouteQuestSnapshot.Unavailable(_lastFailure);
        }
    }

    private static RouteQuestSnapshot ReadSnapshot(RouteQuestMemory memory, Dictionary<string, ulong> nodes,
        byte localSlot, CancellationToken token)
    {
        var remaining = memory.Scalar(nodes["Sh"], "Sh");
        var pool = memory.Array(nodes["Mh"], "Mh", 9, 17);
        if (remaining is < 5 or > 14 || pool is null || pool.Length != 17)
            throw new InvalidDataException("퀘스트 무작위 배정 풀 검증 실패");
        var allAssigned = new List<(int Owner, AssignedRouteQuest Quest)>();
        foreach (var binding in RouteQuestMemory.Bindings)
        {
            token.ThrowIfCancellationRequested();
            var slots = memory.Array(nodes[binding.Index], binding.Index, 9, 4);
            var active = memory.Array(nodes[binding.Active], binding.Active, 13, 4);
            if (slots is null)
            {
                if (active is not null) throw new InvalidDataException("퀘스트 활성·배정 배열 불일치");
                continue;
            }
            // The map draws each quest once globally. Its index array is written only for that owner.
            var owner = slots.Length - 1;
            if (active is null || active.Length != slots.Length || slots.Take(owner).Any(x => x != 0) ||
                active.Take(owner).Any(x => x != 0) || slots[owner] is < 0 or > 2 || active[owner] is < 0 or > 1)
                throw new InvalidDataException("퀘스트 배정 소유자·배열 길이 검증 실패");
            allAssigned.Add((owner, new AssignedRouteQuest(slots[owner], binding.Id, active[owner] == 0)));
        }
        var remainingIds = pool.Take(remaining).Select(RouteQuestMemory.Rawcode).ToArray();
        if (allAssigned.Count != 17 - remaining || allAssigned.GroupBy(x => x.Owner).Any(group =>
                group.Count() != 3 || !group.Select(x => x.Quest.Slot).Order().SequenceEqual(new[] { 0, 1, 2 })) ||
            remainingIds.Distinct(StringComparer.Ordinal).Count() != remaining ||
            remainingIds.Any(id => RouteQuestCatalog.Find(id) is null || allAssigned.Any(x => x.Quest.QuestId == id)))
            throw new InvalidDataException("퀘스트 배정 3슬롯·잔여 풀 교차 검증 실패");
        var snapshot = RouteQuestSnapshot.FromVerifiedSlots(allAssigned.Where(x => x.Owner == localSlot).Select(x => x.Quest));
        if (!snapshot.IsVerified) throw new InvalidDataException("내 항로개척 3개 배정 확인 전");
        return snapshot;
    }
}
