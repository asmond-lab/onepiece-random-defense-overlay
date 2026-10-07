namespace OrandOverlay;

public sealed record LoginClearRewards(int ClearCount, int Round10Traits, int Round10RandomWisps,
    int Round10Lumber, int Round31RandomWisps)
{
    public const int EarlyRound = 10;
    public const int LateRound = 31;

    public static LoginClearRewards? ForCount(int? clearCount) =>
        clearCount is not { } count || count < 0 ? null : new(count,
            (count >= 5 ? 1 : 0) + (count >= 15 ? 1 : 0) + (count >= 25 ? 1 : 0),
            count >= 10 ? 1 : 0, count >= 20 ? 1 : 0,
            count >= 40 ? 3 : count >= 35 ? 2 : count >= 30 ? 1 : 0);

    public string Describe(int round) =>
        $"게임에 불러온 클리어 기록 {ClearCount}회\n" +
        $"10라 · 특포 {Round10Traits} / 랜덤위습 {Round10RandomWisps} / 목재 {Round10Lumber}" +
        (round < EarlyRound ? " · 예정\n" : " · 지급 구간 도달\n") +
        $"31라 · 랜덤위습 총 {Round31RandomWisps}개" +
        (round < LateRound ? " · 예정\n" : " · 지급 구간 도달\n") +
        "보상을 받는 조건입니다. 실제로 받았는지와 남은 수량은 현재 패와 자원에서 확인합니다.";
}
