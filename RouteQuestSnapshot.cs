using System.Collections.Immutable;

namespace OrandOverlay;

public sealed record AssignedRouteQuest(int Slot, string QuestId, bool Completed);

public sealed record RouteQuestSnapshot
{
    public HighGambleObservation HighGamble { get; init; } = HighGambleObservation.Unknown;
    public ImmutableArray<AssignedRouteQuest> Assigned { get; private init; } = [];
    public string Detail { get; private init; } = "항로개척 배정·완료 상태 미확인 · 보상 계산 제외";
    public bool IsVerified => Assigned.Length == 3;
    public static RouteQuestSnapshot Unknown { get; } = new();

    public static RouteQuestSnapshot FromVerifiedSlots(IEnumerable<AssignedRouteQuest> slots)
    {
        var entries = slots.OrderBy(x => x.Slot).ToImmutableArray();
        if (entries.Length != 3 || !entries.Select(x => x.Slot).SequenceEqual([0, 1, 2]) ||
            entries.Select(x => x.QuestId).Distinct(StringComparer.Ordinal).Count() != 3 ||
            entries.Any(x => RouteQuestCatalog.Find(x.QuestId) is null))
            return Unavailable("내 항로개척 정보를 정확히 확인하지 못했습니다. 보상은 아직 계산에 넣지 않습니다.");
        return new() { Assigned = entries, Detail = "항로개척 3개 자동 확인" };
    }

    public static RouteQuestSnapshot Unavailable(string reason) => new() { Detail = reason };

    public RouteQuestStatus Status(string questId)
    {
        if (!IsVerified) return RouteQuestStatus.Unknown;
        var entry = Assigned.FirstOrDefault(x => x.QuestId == questId);
        return entry is null ? RouteQuestStatus.Inactive : entry.Completed
            ? RouteQuestStatus.Completed : RouteQuestStatus.Active;
    }

    public string Describe() => !IsVerified ? Detail : string.Join("\n", Assigned.Select(entry =>
    {
        var quest = RouteQuestCatalog.Find(entry.QuestId)!;
        return $"{quest.Name} · {(entry.Completed ? "완료 · 추가 보상 없음" : "진행 중")}\n" +
            $"{quest.Condition} · 보상 {quest.Reward}";
    }));
}
