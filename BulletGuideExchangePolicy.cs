namespace OrandOverlay;

public static class BulletGuideExchangePolicy
{
    public static CoachDecision? Decide(CoachFrame frame)
    {
        if (frame.Mode != PlayMode.Guide || frame.GuideNumber != 1 || !frame.IsCurrent ||
            frame.Paused || !frame.HasKnownDifficulty || frame.Outcome is "clear" or "fail" ||
            frame.Round < 10 || frame.GuidePlan is not { OwnedBullet: false } plan ||
            frame.Inventory.GetValueOrDefault(BulletGuidePolicy.GoalId) > 0 ||
            frame.RewardWisps.GetValueOrDefault("e018") > 0 ||
            frame.HelperState is not { Mana: >= 80 } helper ||
            frame.Signals.GetValueOrDefault("trait-points") is not { } traits ||
            !helper.Abilities.Any(ability => ability.Rawcode == "A082" &&
                ability.Level > 0 && ability.CooldownRemaining == 0))
            return null;
        var reserveMirror = plan.AirCount < 2 &&
            (plan.Stage == BulletGuideStage.BruleePreparation ||
             frame.Inventory.GetValueOrDefault("rawcode:S80h") > 0);
        if (traits <= (reserveMirror ? 1 : 0)) return null;
        return new(CoachActionKind.Economy, "guide1:exchange-trait-wisp",
            "특성 1개를 선택위습으로 교환",
            "도움소 선택 → 선택위습 제조 1회",
            $"현재 특성 {traits}개·도움소 마나 {helper.Mana:0.#}·남은 쿨다운 0을 확인했습니다. 제조 비용은 특성 1개와 마나 80입니다.",
            "특성 감소와 실제 선택위습 생성을 확인한 뒤 다음 행동을 계산합니다.",
            BulletGuideAdvice.Stage(plan))
        {
            GoalLabel = "공략 1 · 더글라스 불릿",
            OperationGuide = BulletGuideAdvice.Operation(frame),
            PreservedMaterials = reserveMirror
                ? "공중 이동 준비에 필요한 브륄레 변환용 특성 1개는 남깁니다."
                : "현재 조합 재료는 소모하지 않습니다. 생성 전 위습을 보유량에 더하지 않습니다."
        };
    }
}
