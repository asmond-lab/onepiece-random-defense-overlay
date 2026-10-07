using System.Collections.Immutable;

namespace OrandOverlay;

public sealed record RouteQuestDefinition(string Id, string Name, string Condition,
    string Reward, int CommonSelectionWisps = 0, string? CraftTier = null);

public static class RouteQuestCatalog
{
    // Original 2.314 map: SFN/oPN draw three entries from Q000..Q016 without replacement.
    public const string MapScriptSha256 = "0bccc47907a9505f38efaf6bbf20228a728eabdfaec3209cca7df2269bfc2028";
    public static ImmutableArray<RouteQuestDefinition> All { get; } =
    [
        new("Q000", "고고학자", "유니크 아이템 2개 이상 획득", "흔함선택위습 2개 · 랜덤위습 1개", 2),
        new("Q001", "오늘만산다", "15라운드 전 선택위습 11회 이상 사용", "흔함선택위습 3개", 3),
        new("Q002", "목재수집", "목재 도박 9회 이상", "목재 3개"),
        new("Q003", "도박중독", "하급·중급·고급 도박 각각 4회 시도", "흔함선택위습 2개", 2),
        new("Q004", "도박실패(하급)", "하급 도박 4회 실패", "안흔함위습 1개"),
        new("Q005", "도박실패(중급)", "중급 도박 4회 실패", "와일드카드 1개"),
        new("Q006", "도박실패(고급)", "고급 도박 4회 실패", "희귀위습 1개"),
        new("Q007", "MVP 선정", "스토리 기여도 1등 4회", "흔함선택위습 1개", 1),
        new("Q008", "유닛조합(초월)", "초월 유닛 1개 이상 조합", "흔함선택위습 2개", 2, "초월"),
        new("Q009", "유닛조합(불멸)", "불멸 유닛 1개 이상 조합", "흔함선택위습 2개", 2, "불멸"),
        new("Q010", "유닛조합(영원)", "영원 유닛 1개 이상 조합", "흔함선택위습 2개", 2, "영원"),
        new("Q011", "유닛조합(제한)", "제한 유닛 1개 이상 조합", "흔함선택위습 1개", 1, "제한됨"),
        new("Q012", "유닛조합(왜곡)", "왜곡 유닛 1개 이상 조합", "흔함선택위습 1개", 1, "왜곡됨"),
        new("Q013", "유닛조합(아이템)", "아이템이 필요한 유닛 1개 이상 조합", "흔함선택위습 3개", 3),
        new("Q014", "콰트로", "최상위 유닛 4개 이상 조합", "로얄로더 2개"),
        new("Q015", "트리플", "최상위 유닛 3개 이상 조합", "특성포인트 1개"),
        new("Q016", "리사이클", "흔함 유닛 9개 이상 판매", "흔함선택위습 2개", 2)
    ];

    public static RouteQuestDefinition? Find(string id) => All.FirstOrDefault(x => x.Id == id);
}
