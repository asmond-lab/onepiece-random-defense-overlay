namespace OrandOverlay;

public static class BulletGuideTargetPolicy
{
    public static CoachDecision? Decide(CoachFrame frame)
    {
        if (frame.Mode != PlayMode.Guide || frame.GuideNumber != 1 || !frame.IsCurrent ||
            frame.Paused || !frame.HasKnownDifficulty || frame.Outcome is "clear" or "fail" ||
            frame.Round < 50 || frame.GuidePlan is not { OwnedBullet: true } ||
            frame.Inventory.GetValueOrDefault(BulletGuidePolicy.GoalId) <= 0)
            return null;
        var bullets = frame.CombatObservations.Where(unit => unit.Kind == CombatUnitKind.Bullet &&
            unit.Rawcode == "h081" && unit.Position is not null && unit.Life is > 0).ToArray();
        if (bullets.Length != 1) return null;
        var bullet = bullets[0];
        var origin = bullet.Position!.Value;
        var target = frame.CombatObservations
            .Where(unit => unit.Kind == CombatUnitKind.LaneMonster && unit.Owner == 6 &&
                unit.LaneSlot == bullet.Owner && unit.Life is > 0 && unit.ArmorBreakStacks is > 0 &&
                unit.Position is { } position &&
                Math.Pow((double)position.X - origin.X, 2) + Math.Pow((double)position.Y - origin.Y, 2) <= 400 * 400)
            .OrderByDescending(unit => unit.Life)
            .ThenByDescending(unit => unit.ArmorBreakStacks)
            .ThenBy(unit => unit.SampleId).FirstOrDefault();
        if (target?.Position is not { } point) return null;
        return new(CoachActionKind.Maintain, "guide1:line-target:" + target.SampleId,
            "방깎이 적용된 체력 높은 라인 적 공격",
            $"불릿 선택 → 확인된 위치 ({point.X:0}, {point.Y:0})의 라인 적을 공격 대상으로 선택",
            $"불릿 주변 400 안에서 확인한 적 중 현재 체력이 높은 대상입니다. 현재 체력 {target.Life:0} · 누적 방깎 {target.ArmorBreakStacks}. 새로 나온 방깎 없는 적은 후보에서 제외했습니다.",
            "대상 체력과 누적 방깎 변화를 다시 확인합니다. 불릿이 실제로 그 적을 공격하는지, 부대 지정이 됐는지는 직접 확인하세요.",
            BulletGuideAdvice.Stage(frame.GuidePlan))
        {
            TargetUnitId = BulletGuidePolicy.GoalId,
            GoalLabel = "공략 1 · 더글라스 불릿", OperationGuide = BulletGuideAdvice.Operation(frame),
            PreservedMaterials = "보잡과 스턴 유닛의 역할은 유지합니다.",
            UnknownSignals = "현재 공격 대상·부대 지정은 미확인입니다. 후보 목록은 전체 라인 수가 아닙니다."
        };
    }
}
