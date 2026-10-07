namespace OrandOverlay;

/// <summary>Guide role advice, not measured active support or a craft command.</summary>
public static class BulletGuideBlackMariaPolicy
{
    public const string UnitId = "rawcode:U40h";

    public static string Summary(CoachFrame frame)
    {
        if (!frame.IsCurrent || !frame.HasKnownDifficulty || frame.Mode != PlayMode.Guide ||
            frame.GuideNumber != 1 || frame.Paused || frame.Outcome is "clear" or "fail" ||
            frame.Inventory.GetValueOrDefault(UnitId) <= 0) return "";
        var selected = BlackMariaObservation.Selected(frame);
        var recommended = RecommendedMode(frame);
        var role = recommended is not null && selected == recommended
            ? BlackMariaObservation.Label(recommended) + " 부족 · 이미 " + BlackMariaObservation.Label(selected) +
                " 선택 관측: 재선택 지시 없이 다음 행동 진행"
            : RoleAdvice(frame);
        return "왜곡 블랙마리아 · " + role + ". 선택 관측: " + BlackMariaObservation.Label(selected) +
            ". 선택만으로 실제 효과가 발동하거나 유지된다고 보지 않습니다. 지원 수치에 미리 더하지 않습니다.";
    }

    private static string RoleAdvice(CoachFrame frame) => frame.GuidePlan?.Support switch
    {
        null => "지원 미확인: 스턴/이감 단일 추천 보류",
        { StunPairReady: false, SlowPotential: < 82 } => "스턴·이감 모두 부족: 필요한 역할을 비교하세요. 고정 우선순위 없음",
        { StunPairReady: false } => "스턴 부족 → 스턴 선택 권고",
        { SlowPotential: < 82 } => "이감 부족 → 이감 선택 권고",
        _ => "보유 조합 기준 지원 목표 충족: 바꿀 필요 없음"
    };

    public static CoachDecision? Consider(CoachFrame frame, DataCatalog catalog)
    {
        if (!frame.IsCurrent || !frame.HasKnownDifficulty || frame.Mode != PlayMode.Guide ||
            frame.GuideNumber != 1 || frame.Paused || frame.Outcome is "clear" or "fail" ||
            frame.GuidePlan is not { } plan || frame.Inventory.GetValueOrDefault(UnitId) > 0) return null;
        var roots = frame.SelectedGoalIds.Concat(plan.ProtectedUnitIds)
            .Concat(new[] { frame.GoalId, plan.TargetUnitId, frame.CommittedCraftUnitId }.OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var remaining = new RecipeCompletionCalculator(catalog.Unit)
            .CalculateAllocation(roots, frame.Inventory).RemainingInventory;
        if (remaining.GetValueOrDefault("rawcode:320h") < 2 && remaining.GetValueOrDefault("rawcode:X10h") < 2) return null;
        var reason = "원문: 카쿠나 루피 중복 시 고려. 사용자 보충: 같은 희귀 카쿠 2기 이상 또는 희귀 루피 기어서드 2기 이상. 다른 목표에 쓸 재료를 뺀 현재 여유 패로 확인했습니다.";
        return new(CoachActionKind.Maintain, "guide1:blackmaria:consider", "왜곡 블랙마리아 채용 고려",
            RoleAdvice(frame) + ". 즉시 조합 명령이 아닙니다. 조합식과 재료를 확인하고, 조합 후에도 필요한 지원 유닛이 남는지 확인하세요.",
            reason, "재료·지원 변화 후 다시 판단합니다.", BulletGuideAdvice.Stage(plan))
        {
            TargetUnitId = UnitId, GoalLabel = "공략 1 · 더글라스 불릿",
            OperationGuide = BulletGuideAdvice.Operation(frame) + "\n왜곡 블랙마리아 채용 고려 · " + RoleAdvice(frame) + "\n" + reason,
            UnknownSignals = "희귀위습 획득 경로 미확인. 재고를 획득 이력으로 단정하지 않습니다. 실제 선택·효과 미관측.",
            PreservedMaterials = "다른 선택·진행·보호 목표에 배정된 재료는 제외했습니다. 재료를 소비하지 않습니다."
        };
    }

    private static BlackMariaMode? RecommendedMode(CoachFrame frame) => frame.GuidePlan?.Support switch
    {
        { StunPairReady: false, SlowPotential: >= 82 } => BlackMariaMode.Stun,
        { StunPairReady: true, SlowPotential: < 82 } => BlackMariaMode.Slow,
        _ => null
    };

    public static CoachDecision? Decide(CoachFrame frame)
    {
        var summary = Summary(frame);
        if (summary.Length == 0 || frame.GuidePlan is null) return null;
        var mode = RecommendedMode(frame);
        if (mode is null || BlackMariaObservation.Selected(frame) == mode) return null;
        return new(CoachActionKind.Maintain, "guide1:blackmaria:" + mode,
            "왜곡 블랙마리아 " + BlackMariaObservation.Label(mode) + " 역할 확인",
            summary + " 게임에서 필요한 선택을 확인하세요. 자동 전환이나 클릭 횟수는 지시하지 않습니다.",
            "원문은 스턴/이감을 권합니다. 현재 부족한 역할은 보유 조합 기준으로 판단하며 실제 전투 보장이 아닙니다.",
            "새 선택 관측을 확인하되 실제 효과 종료·발동은 별도로 확인하세요.", BulletGuideAdvice.Stage(frame.GuidePlan))
        {
            TargetUnitId = UnitId, GoalLabel = "공략 1 · 더글라스 불릿",
            OperationGuide = BulletGuideAdvice.Operation(frame),
            UnknownSignals = "효과가 발동했는지, 얼마나 유지되는지, 대상에게 적용됐는지는 미확인입니다. 화상은 관측하되 이 공략의 기본 추천 후보가 아닙니다.",
            PreservedMaterials = "현재 패와 다른 목표 예약은 그대로 보존합니다. 조합·판매 명령이 아닙니다."
        };
    }
}
