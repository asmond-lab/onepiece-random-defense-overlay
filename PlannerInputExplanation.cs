namespace OrandOverlay;

internal static class PlannerInputExplanation
{
    public static string Blocker(string value) => value switch
    {
        "AwaitingFirstRare" => "첫 희귀함 획득 전: 자동 첫 전설 경로를 아직 정하지 않습니다.",
        "UnknownStoryStage" => "스토리 단계 미확인: 다음 보상과 자동 추천 시점을 계산할 수 없습니다.",
        "MatchGenerationMismatch" => "게임 회차 불일치: 이전 게임의 자동 판단을 적용하지 않습니다.",
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
        "UnknownFirstLegendHistory" => "첫 전설 이력 미확인: 초반 경로 적합성을 확정할 수 없습니다.",
        "RouteNotRobust" => "상위 경로 불확실: 첫 전설 후보에 따라 유리한 자동 목표가 달라집니다.",
        "NoRouteCandidate" => "상위 경로 후보 없음: 자동 목표 비교 결과가 아직 없습니다.",
        "NavigationNotRobust" => "항법 경로 불확실: 자동 항법 추천을 고정하지 않습니다.",
        "ManualGoalOverride" => "수동 목표 선택: 자동 목표 변경은 하지 않으며 조합 안내는 유지합니다.",
        "ManualNavigationOverride" or "ManualOverride" => "수동 항법 선택: 자동 항법 변경은 하지 않습니다.",
        "TransientSnapshot" or "InputNotReady" => "실시간 패·게임 신호 미확인: 자동 재판단은 대기하며 마지막 조합 안내를 유지합니다.",
        "ArithmeticLimitExceeded" => "항법 계산 범위 초과: 안전한 자동 비교 결과를 제공할 수 없습니다.",
        "NoEligibleOptions" => "평가 가능한 항법 후보 없음: 자동 항법을 선택하지 않습니다.",
        "InsufficientConfidence" => "항법 근거 신뢰도 부족: 자동 항법 추천을 확정하지 않습니다.",
        "NonDominantIntervals" => "항법 후보의 예상 성능 범위가 겹침: 확실히 유리한 자동 항법이 없습니다.",
        _ => "자동 판단 조건 미분류: 해당 조건의 영향 설명을 제공할 수 없습니다."
    };

    public static string Signal(string value) => value switch
    {
        "enemy-top-count" => "상대 상위 유닛 수 미확인: 항법 보상·회복 기대값 비교가 제한됩니다.",
        "runtime-signals" => "실시간 게임 신호 미확인: 자동 판단의 최신성을 확인할 수 없습니다.",
        _ when value.StartsWith("best-help:unknown:", StringComparison.Ordinal) =>
            "최고의 도움 스킬 정보 미확인: 최대출력·역발상의 스킬 기여도 계산이 제한됩니다.",
        _ when value.StartsWith("allied:", StringComparison.Ordinal) =>
            "연합세력 평가 조건: 자원·보상 조건에 따라 해당 항법의 평가가 제한됩니다.",
        _ when value.StartsWith("path:", StringComparison.Ordinal) =>
            "패왕의 길 평가 조건: 보상·유닛 조건에 따라 해당 항법의 평가가 제한됩니다.",
        _ when value.StartsWith("gambler:", StringComparison.Ordinal) =>
            "도박광 평가 조건: 도박 결과와 라인 안전성에 따라 추천이 제한됩니다.",
        _ when value.StartsWith("best-help:", StringComparison.Ordinal) =>
            "최고의 도움 평가 조건: 스킬·자원 근거에 따라 해당 항법의 평가가 제한됩니다.",
        _ when value.StartsWith("random:", StringComparison.Ordinal) =>
            "랜덤 항법 평가 조건: 무작위 보상의 기대값 비교가 제한됩니다.",
        _ => "항법 입력 설명 미등록: 해당 신호의 구체적인 영향을 아직 설명할 수 없습니다."
    };
}
