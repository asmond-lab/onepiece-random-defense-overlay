namespace OrandOverlay;

internal static class PlannerInputExplanation
{
    public static string Blocker(string value) => value switch
    {
        "AwaitingFirstRare" => "첫 희귀함 획득 전: 자동 첫 전설 경로를 아직 정하지 않습니다.",
        "UnknownStoryStage" => "스토리 단계 미확인: 다음 보상과 자동 추천 시점을 계산할 수 없습니다.",
        "MatchGenerationMismatch" => "이전 판 정보가 섞여 있어 이번 추천에는 쓰지 않습니다.",
        "UnspentSpecialUncommonWisps" => "특별함·안흔함 위습 사용 전: 지급 유닛을 반영한 뒤 자동 경로를 비교합니다.",
        "UnknownSpecialUncommonWisps" => "특별함·안흔함 위습 수 미확인: 보상 반영 완료를 판단할 수 없습니다.",
        "UnknownLegendResources" => "전설 조합 자원 미확인: 골드·목재 충족 여부를 확정할 수 없습니다.",
        "NoLegendCandidate" => "첫 전설 후보 없음: 현재 패에서 자동 경로를 정하지 못했습니다.",
        "LegendFallback" => "첫 전설 대체 경로 사용: 기존 후보의 자원 조건을 충족하지 못했습니다.",
        "AwaitingLegendOutput" => "첫 전설 조합 결과 대기: 완성 유닛 인식 후 자동 단계를 진행합니다.",
        "AwaitingMarineford" => "마린포드 보상 대기: 희귀 보상을 반영한 뒤 자동 상위 경로를 비교합니다.",
        "UnknownRareWisps" => "희귀 위습 수 미확인: 보상 사용 완료와 자동 상위 추천 시점을 확정할 수 없습니다.",
        "UnspentRareWisps" => "희귀 위습 사용 전: 지급 유닛을 반영한 뒤 자동 상위 경로를 비교합니다.",
        "AwaitingRound20" or "BeforeRound20" => "20라운드 전: 자동 상위·항법 판단 시점을 기다립니다. 현재 목표 조합은 계속 안내합니다.",
        "UnknownFirstLegendHistory" => "첫 전설이 무엇이었는지 몰라 어떤 조합이 잘 맞는지 아직 판단할 수 없습니다.",
        "RouteNotRobust" => "첫 전설이 무엇이냐에 따라 유리한 상위가 달라져 목표를 아직 정하지 못했습니다.",
        "NoRouteCandidate" => "상위 경로 후보 없음: 자동 목표 비교 결과가 아직 없습니다.",
        "NavigationNotRobust" => "어느 항법이 더 유리한지 아직 확실하지 않아 추천을 정하지 않았습니다.",
        "ManualGoalOverride" => "직접 정한 목표를 유지합니다. 조합 안내는 계속됩니다.",
        "ManualNavigationOverride" or "ManualOverride" => "직접 고른 항법을 자동으로 바꾸지 않습니다.",
        "TransientSnapshot" or "InputNotReady" => "현재 패와 게임 정보를 다시 확인하는 중입니다. 마지막 조합 안내를 유지합니다.",
        "ArithmeticLimitExceeded" => "비교할 경우가 너무 많아 항법 추천을 확정하지 못했습니다.",
        "NoEligibleOptions" => "지금 비교할 수 있는 항법이 없어 추천을 보류합니다.",
        "InsufficientConfidence" => "추천을 정할 만큼 정보가 충분하지 않습니다.",
        "NonDominantIntervals" => "항법들의 예상 결과가 비슷해 어느 쪽이 더 유리한지 확실하지 않습니다.",
        _ => "아직 추천을 정할 수 없으며, 이유를 자세히 설명할 수 없는 상태입니다."
    };

    public static string Signal(string value) => value switch
    {
        "enemy-top-count" => "상대 상위 유닛 수를 몰라 항법의 예상 보상과 회복 효과를 충분히 비교하지 못했습니다.",
        "runtime-signals" => "현재 게임 정보를 읽지 못해 이 추천이 지금도 맞는지 확인할 수 없습니다.",
        _ when value.StartsWith("best-help:unknown:", StringComparison.Ordinal) =>
            "최고의 도움 스킬을 아직 확인하지 못해 최대출력·역발상이 얼마나 도움이 되는지 충분히 계산하지 못했습니다.",
        _ when value.StartsWith("allied:", StringComparison.Ordinal) =>
            "연합세력 항법을 비교하려면 자원과 보상 조건을 더 확인해야 합니다.",
        _ when value.StartsWith("path:", StringComparison.Ordinal) =>
            "패왕의 길 항법을 비교하려면 보상과 유닛 조건을 더 확인해야 합니다.",
        _ when value.StartsWith("gambler:", StringComparison.Ordinal) =>
            "도박광 추천은 도박 결과와 라인을 버틸 수 있는지에 따라 달라집니다.",
        _ when value.StartsWith("best-help:", StringComparison.Ordinal) =>
            "최고의 도움 항법을 비교하려면 스킬과 자원을 더 확인해야 합니다.",
        _ when value.StartsWith("random:", StringComparison.Ordinal) =>
            "랜덤 항법은 무작위 보상으로 얼마나 도움이 될지 충분히 비교하지 못했습니다.",
        _ => "이 정보가 항법 추천에 어떤 영향을 주는지 아직 자세히 설명할 수 없습니다."
    };
}
