namespace OrandOverlay;

public static class BulletGuideRayleighPolicy
{
    public static CoachDecision? Decide(CoachFrame frame)
    {
        if (NasjuroWispAdvicePolicy.Evaluate(frame).SuppressGenericRayleigh) return null;
        if (frame.Mode != PlayMode.Guide || frame.GuideNumber != 1 || !frame.IsCurrent ||
            frame.Paused || !frame.HasKnownDifficulty || frame.Outcome is "clear" or "fail" ||
            frame.CompletedStoryStage < 10 || frame.GuidePlan is not { OwnedBullet: false, AirCount: < 2 } plan ||
            frame.RewardWisps.GetValueOrDefault("e01A") <= 0 ||
            frame.Inventory.GetValueOrDefault("rawcode:X50h") > 0 ||
            frame.Inventory.GetValueOrDefault("rawcode:S80h") > 0 ||
            frame.Inventory.GetValueOrDefault("rawcode:830h") > 0)
            return null;
        return new(CoachActionKind.Reward, "guide1:select-rayleigh-ship",
            "초월위습으로 레일리와 해적선 확보",
            "보유 초월위습 1개 선택 → 레일리 견본 앞 선택 구역으로 이동",
            "현재 초월위습 보유를 확인했습니다. 이 구역은 위습 1개를 소모해 레일리 희귀 1기와 해적선 1기를 만듭니다. 금·목재·특성 추가 비용은 없습니다.",
            "초월위습 감소와 레일리·해적선 생성을 실제 패에서 확인한 뒤 브륄레 재료를 다시 계산합니다.",
            BulletGuideAdvice.Stage(plan))
        {
            TargetUnitId = "rawcode:X50h", RewardWispId = "e01A",
            GoalLabel = "공략 1 · 더글라스 불릿", OperationGuide = BulletGuideAdvice.Operation(frame),
            PreservedMaterials = "레일리는 브륄레 재료로 보존합니다. 선택은 이동 구역 방식이며 별도 스킬이나 조합 명령어가 아닙니다."
        };
    }
}
