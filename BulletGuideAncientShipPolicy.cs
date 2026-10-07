namespace OrandOverlay;

/// <summary>gbw J57025-57043: A0KB consumes h05Y and 8 lumber, creates h060.
/// A0KC (28% Rayleigh) and A0OE (two selection wisps) are separate, not automatic alternatives.</summary>
public static class BulletGuideAncientShipPolicy
{
    public static CoachDecision? Decide(CoachFrame frame, DataCatalog catalog)
    {
        const string ancient = "rawcode:Y50h", pirate = "rawcode:060h";
        if (frame.Mode != PlayMode.Guide || frame.GuideNumber != 1 || !frame.IsCurrent ||
            frame.Paused || !frame.HasKnownDifficulty || frame.Outcome is "clear" or "fail" ||
            frame.GuidePlan is not { TargetUnitId: { } target } plan ||
            frame.Signals.GetValueOrDefault("lumber") is not >= 8 ||
            frame.Inventory.GetValueOrDefault(ancient) <= 0 || plan.ProtectedUnitIds.Contains(ancient)) return null;
        var calculator = new RecipeCompletionCalculator(catalog.Unit);
        if (!calculator.CalculateAllocation([target],frame.Inventory).Progress.MissingLeaves
            .Any(leaf => leaf.UnitId == pirate && leaf.MissingCount > 0)) return null;
        var reservation = frame.ShipReservations is { } shared && ShipReservationPolicy.Matches(shared, frame)
            ? shared : ShipReservationPolicy.Evaluate(frame, catalog);
        if (!reservation.IsKnown || reservation.Ships[ancient].Free <= 0) return null;
        var allocation = reservation.Allocation;
        // Full expansion includes historical crafting costs. Subtract only inventory actually
        // allocated to these roots, so completed intermediate subtrees are not charged twice.
        var paidLumber = allocation.ConsumedByUnitId.Sum(pair => checked(pair.Value *
            calculator.CalculateAllocation([pair.Key], frame.Inventory).ResourceRequirements.Lumber));
        var reservedLumber = Math.Max(0, allocation.ResourceRequirements.Lumber - paidLumber);
        if (frame.Signals["lumber"]!.Value - 8 < reservedLumber) return null;
        if (!frame.CombatObservations.Any(unit => unit.Rawcode == "h05Y" &&
            unit.Kind == CombatUnitKind.LocalUnit && unit.Life is > 0 &&
            unit.AncientShipAbility is { Rawcode: "A0KB", Level: 1, CooldownRemaining: 0 })) return null;
        return new(CoachActionKind.Craft, "guide1:ancient-to-pirate", "고대의 배 1개를 해적선으로 변환 검토",
            "고대의 배 1개 선택 → 해적선 획득(목재 8) 항목이 사용 가능하다고 직접 확인한 경우에만 사용",
            "현재 목표의 해적선 부족과 여유 고대선·목재를 확인했습니다. 이 선택은 고대선 1개·목재 8개를 소모해 해적선 1개를 확정 생성합니다. 레일리 28% 도박과 다릅니다.",
            "고대선 감소·목재 차감·해적선 생성을 실제 관측한 뒤 조합을 다시 계산합니다. 결과를 미리 재고에 더하지 않습니다.",
            BulletGuideAdvice.Stage(plan))
        { TargetUnitId = pirate, ConsumedUnitId = ancient, GoalLabel = "공략 1 · 더글라스 불릿",
            UnknownSignals = "스킬과 쿨다운은 확인했습니다. 지금 버튼을 누를 수 있는지, 다른 스킬을 사용 중인지, 대상을 어떻게 고르는지는 게임에서 직접 확인하세요. 단축키는 안내하지 않습니다.",
            OperationGuide = BulletGuideAdvice.Operation(frame),
            PreservedMaterials = "토키와 선택·진행 목표의 고대선 및 목재 예약을 보존합니다. 선택위습 2개 전환은 별도 대안이며 자동 우선 선택하지 않습니다." };
    }
}
