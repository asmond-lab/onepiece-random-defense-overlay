namespace OrandOverlay;

/// <summary>Pin-joined w3u A0B8 owners; KIG J21868-21886, not the A0BA special sale.</summary>
public static class BulletGuideUncommonSalePolicy
{
    internal static bool IsSaleUnit(string code) => code is
        "h00O" or "h00N" or "h00M" or "h00L" or "h00K" or "h00J" or "h00I" or
        "h00G" or "h00F" or "h00E" or "h00D" or "h00C" or "h00A";

    public static CoachDecision? Decide(CoachFrame frame, DataCatalog catalog)
    {
        if (frame.Mode != PlayMode.Guide || frame.GuideNumber != 1 || !frame.IsCurrent ||
            frame.Paused || !frame.HasKnownDifficulty || frame.Outcome is "clear" or "fail" ||
            frame.Round < 50 || frame.GuidePlan is not { OwnedBullet: true } plan ||
            frame.Inventory.GetValueOrDefault(BulletGuidePolicy.GoalId) <= 0) return null;
        var roots = frame.SelectedGoalIds.Concat(new[] { frame.GoalId, plan.TargetUnitId,
            frame.CommittedCraftUnitId }.OfType<string>()).Concat(plan.ProtectedUnitIds).Distinct(StringComparer.OrdinalIgnoreCase);
        var remaining = new RecipeCompletionCalculator(catalog.Unit)
            .CalculateAllocation(roots, frame.Inventory).RemainingInventory;
        foreach (var unit in frame.CombatObservations.OrderBy(u => u.SampleId))
        {
            if (!IsSaleUnit(unit.Rawcode) || unit.Kind != CombatUnitKind.LocalUnit || unit.Life is not > 0 ||
                unit.UncommonSaleAbility is not { Rawcode: "A0B8", Level: 1, CooldownRemaining: 0 }) continue;
            var id = "rawcode:" + RawcodeAliases.Canonical(new string(unit.Rawcode.Reverse().ToArray()));
            if (remaining.GetValueOrDefault(id) <= 0 || plan.ProtectedUnitIds.Contains(id) ||
                BulletGuidePolicy.RetainedCodes.Contains(id[8..])) continue;
            var name = catalog.Unit(id).Name;
            return new(CoachActionKind.Economy, "guide1:sell-uncommon:" + id,
                $"{name} 여유분 1개 판매 검토", $"{name} 1개 선택 → 게임의 ‘판매 :: 안흔함’이 사용 가능하다고 직접 확인한 경우에만 사용",
                "현재 목표·진행 조합·보존 기물에 배정되지 않은 한 기입니다. 랜덤위습 1개는 50%, 목재 1개는 별도 20% 확률이며 보장 보상이 아닙니다.",
                "유닛 한 기가 줄고 실제 보상을 받았는지 확인한 뒤 다시 계산합니다. 받지 않은 위습·목재는 보유량에 더하지 않습니다.",
                BulletGuideAdvice.Stage(plan))
            { TargetUnitId = id, ConsumedUnitId = id, GoalLabel = "공략 1 · 더글라스 불릿",
                UnknownSignals = "스킬과 쿨다운은 확인했습니다. 지금 버튼을 누를 수 있는지, 다른 스킬을 사용 중인지, 대상을 어떻게 고르는지는 게임에서 직접 확인하세요. 단축키는 안내하지 않습니다.",
                OperationGuide = BulletGuideAdvice.Operation(frame) };
        }
        return null;
    }
}
