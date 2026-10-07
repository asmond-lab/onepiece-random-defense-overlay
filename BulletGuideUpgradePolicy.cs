namespace OrandOverlay;

public static class BulletGuideUpgradePolicy
{
    public static string MilestoneSummary(CoachFrame frame)
    {
        if (!frame.IsCurrent || frame.Round < 50 ||
            frame.Inventory.GetValueOrDefault(BulletGuidePolicy.GoalId) <= 0) return "";
        const string source = "원문 목표: 50라 방깎30 + 공속1~15 · 60라까지 각각 30스택";
        if (!frame.GuideRuntime.IsCurrent || frame.GuideRuntime.ExactCounts is not { } exact)
            return source + " · 정확 스택 미확인 · 50라 최소 목표 달성 여부 미확인";
        var minimum = exact.Armor == 30 && exact.Speed >= 1
            ? "50라 최소 목표 달성. " : "50라 최소 목표 미달. ";
        var maximum = exact.Armor == 30 && exact.Speed == 30 ? "60라 풀강 목표 달성"
            : frame.Round < 60 ? "다음 목표: 60라까지 방깎 → 공속 각각 30스택"
            : "60라 풀강 목표 미달 · 방깎 → 공속 각각 30스택 목표";
        return source + $"\n현재 관측: 방깎 {exact.Armor}/30 · 공속 {exact.Speed}/30. " + minimum + maximum;
    }

    public static CoachDecision? Decide(CoachFrame frame, DataCatalog catalog)
    {
        if (!frame.IsCurrent || frame.Paused || frame.Outcome is "clear" or "fail" || frame.Round < 50 ||
            frame.GuidePlan?.Support is not { IsReady: true } ||
            frame.Inventory.GetValueOrDefault(BulletGuidePolicy.GoalId) <= 0 || !frame.GuideRuntime.IsCurrent)
            return null;
        var track = frame.GuideRuntime.ArmorTier is 1 or 2 ? "armor"
            : frame.GuideRuntime.ArmorTier == 3 && frame.GuideRuntime.SpeedTier is 1 or 2 ? "speed" : null;
        if (track is null) return null; // Attack investment is user-managed, not gated by starting armor.
        var roots = frame.SelectedGoalIds.Concat(frame.GuidePlan.ProtectedUnitIds).Concat(new[]
            { frame.GoalId, frame.GuidePlan.TargetUnitId, frame.CommittedCraftUnitId }.OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var available = new RecipeCompletionCalculator(catalog.Unit)
            .CalculateAllocation(roots, frame.Inventory).RemainingInventory;
        var commons = available.Where(pair => pair.Value > 0 &&
            catalog.Unit(pair.Key).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains)).ToArray();
        if (commons.Length == 0) return null;
        var common = commons.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First();
        var label = track == "armor" ? "방어력감소" : "공격속도";
        var exact = frame.GuideRuntime.ExactCounts;
        // Source milestones are not a stop at speed 15 or an interpolated per-round target.
        var currentCount = exact is null ? "정확 스택 미확인 · 50라 최소 목표 달성 여부 미확인. " :
            $"현재 {(track == "armor" ? exact.Armor : exact.Speed)}/30스택. " +
            $"현재 관측: 방깎 {exact.Armor}/30 · 공속 {exact.Speed}/30. " +
            (exact.Armor == 30 && exact.Speed >= 1 ? "50라 최소 목표 달성. " : "50라 최소 목표 미달. ");
        if (track == "speed")
            currentCount += "공속15에서 강제 중단하지 않고 공속30 선행 가능(원문). ";
        var milestone = MilestoneSummary(frame);
        return new(CoachActionKind.Upgrade, "guide1:upgrade:" + track,
            $"불릿 {label} 강화 한 번",
            $"불릿 선택 → {label} 강화 아이템 확인 → 여유 {catalog.Unit(common.Key).Name} 1기를 대상으로 한 번 사용",
            currentCount + "게임에서 내 불릿의 강화 등급과 남는 흔함을 확인했습니다. 자신의 흔함 1기를 소모하며 40%로 1, 60%로 2스택 상승합니다. 최대 30스택입니다.",
            exact is null ? "흔함 감소를 확인하고 다시 계산합니다. 등급 2는 15~29스택이며 정확한 수량으로 표시하지 않습니다."
                : "흔함 감소와 강화 횟수 증가를 확인한 뒤 다시 계산합니다.",
            milestone)
        {
            TargetUnitId = BulletGuidePolicy.GoalId, ConsumedUnitId = common.Key,
            GoalLabel = "공략 1 · 더글라스 불릿", OperationGuide = BulletGuideAdvice.Operation(frame),
            PreservedMaterials = "50라에 준비한 흔함은 불릿 강화에 한 기씩 사용합니다. 지원 유닛은 남기세요.",
            UnknownSignals = exact is null
                ? "정확한 강화 수치는 미확인입니다. 대상에게 오라 효과가 적용됐는지도 확인이 필요합니다."
                : "대상에게 오라 효과가 적용됐는지는 확인이 필요합니다."
        };
    }
}
