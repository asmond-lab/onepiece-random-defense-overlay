namespace OrandOverlay;

public static class BulletGuideMirrorPolicy
{
    internal static bool HasMissionException(IReadOnlyDictionary<string, int> inventory) =>
        new[] { "R50h", "H50h", "O50h", "Q50h" }.Any(code => inventory.GetValueOrDefault("rawcode:" + code) > 0);

    public static CoachDecision? Decide(CoachFrame frame)
    {
        if (frame.Mode != PlayMode.Guide || frame.GuideNumber != 1 || !frame.IsCurrent ||
            frame.Paused || !frame.HasKnownDifficulty || frame.Outcome is "clear" or "fail" ||
            frame.CompletedStoryStage < 10 || frame.GuidePlan is not { OwnedBullet: false, AirCount: < 2 } plan ||
            HasMissionException(frame.Inventory) ||
            frame.Inventory.GetValueOrDefault("rawcode:S80h") != 1 ||
            frame.Inventory.GetValueOrDefault("rawcode:830h") > 0 ||
            frame.Signals.GetValueOrDefault("trait-points") is not >= 1)
            return null;
        var brulees = frame.CombatObservations.Where(unit => unit.Rawcode == "h08S" &&
            unit.Kind == CombatUnitKind.LocalUnit && unit.Life is > 0 &&
            unit.MirrorAbility is { Rawcode: "A114", Level: > 0, CooldownRemaining: 0 }).ToArray();
        if (brulees.Length != 1) return null;
        var brulee = brulees[0];
        var target = frame.CombatObservations.FirstOrDefault(unit => unit.Rawcode == "h038" &&
            unit.LegendMarked && unit.Life is > 0 &&
            (unit.Owner == brulee.Owner && unit.Kind == CombatUnitKind.LocalUnit ||
             unit.Owner == 7 && unit.Kind == CombatUnitKind.RecipeExemplar));
        if (target is null) return null;
        return new(CoachActionKind.Craft, "guide1:mirror-caesar",
            "브륄레를 시저로 변환",
            "브륄레 선택 → 거울 변환 → 확인된 시저 전설 견본 지정",
            "펑크해저드 완료, 내 브륄레의 변환 스킬, 시저의 전설 표식과 특성 1개를 확인했습니다. 변환은 마나를 쓰지 않지만 브륄레와 특성 1개를 소모합니다.",
            "브륄레 감소와 시저 생성을 실제 패에서 확인하세요. 시저는 브륄레가 있던 위치에 생성되며 대상 견본은 소모하지 않습니다.",
            BulletGuideAdvice.Stage(plan))
        {
            TargetUnitId = "rawcode:830h", ConsumedUnitId = "rawcode:S80h",
            GoalLabel = "공략 1 · 더글라스 불릿", OperationGuide = BulletGuideAdvice.Operation(frame),
            PreservedMaterials = "검은수염은 별도로 조합해 불릿 재료로 유지합니다. 변환된 시저에는 공격속도 -5% 표식이 붙습니다."
        };
    }
}
