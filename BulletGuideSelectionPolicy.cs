using System.Collections.Immutable;

namespace OrandOverlay;

public static class BulletGuideSelectionPolicy
{
    public static CoachDecision? Decide(CoachFrame frame, DataCatalog catalog)
    {
        if (frame.Mode != PlayMode.Guide || frame.GuideNumber != 1 || !frame.IsCurrent ||
            frame.Paused || frame.Round <= 0 || !frame.HasKnownDifficulty || frame.Outcome is "clear" or "fail" ||
            frame.GuidePlan?.TargetUnitId is not { } target) return null;
        var plan = frame.GuidePlan;
        var held = plan.SelectionBatch;
        if (plan.PendingSelectionOutputs > 0)
            return new(CoachActionKind.Waiting, "guide1:selection-pending", "선택 결과 확인 중",
                "추가 선택은 보류하세요. " + (held is null ? "" : List(held, catalog)),
                $"선택위습 감소 {plan.PendingSelectionOutputs}개에 해당하는 결과가 아직 확인되지 않았습니다.",
                "새 정상 패에서 선택한 유닛의 증가를 확인하면 남은 목록을 갱신합니다.", BulletGuideAdvice.Stage(plan))
            { SelectionBatch = held, TargetUnitId = target, CraftDeferredForReward = true };
        var owned = frame.RewardWisps.GetValueOrDefault("e018");
        var urgent = plan.Stage == BulletGuideStage.FastUniqueRare && frame.Round is >= 0 and < 8 ||
            plan.Stage == BulletGuideStage.FirstLegend && frame.Round >= 10 ||
            plan.Stage == BulletGuideStage.BulletMaterials && frame.Round is > 0 and < 50;
        if (!urgent || owned <= 0 || catalog.Unit(target).Recipe.Count == 0 ||
            plan.AwaitingRewardHand || FirstLegendRewardGate.RewardIds.Any(id => id != "e018" &&
                frame.RewardWisps.GetValueOrDefault(id) > 0)) return null;
        var available = BulletGuideReservations.Available(catalog, frame.Inventory,
            frame.GuidePlan.ProtectedUnitIds.Concat(frame.SelectedGoalIds)
                .Concat(new[] { frame.CommittedCraftUnitId }.OfType<string>()
                    .Where(id => !BulletGuideReservations.IsRecipeStep(catalog, target, id))), target);
        var missing = new RecipeCompletionCalculator(catalog.Unit).Calculate([target], available).MissingLeaves;
        if (missing.Count == 0 || missing.Any(leaf =>
                !catalog.Unit(leaf.UnitId).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains))) return null;
        // Acquired intermediates can replace common subtrees without individual common gains.
        // Keep the held identities/order, but never purchase beyond today's physical deficit.
        var batch = held is not null ? held with { Items = held.Items.Select(item => item with
            { RemainingCount = Math.Min(item.RemainingCount, checked((int)(missing.FirstOrDefault(leaf =>
                leaf.UnitId == item.UnitId)?.MissingCount ?? 0))) }).ToImmutableArray() }
            : new SelectionWispBatch(target, missing.OrderByDescending(leaf => leaf.MissingCount)
            .ThenBy(leaf => leaf.UnitId, StringComparer.Ordinal)
            .Select(leaf => new SelectionWispItem(leaf.UnitId, checked((int)leaf.MissingCount))).ToImmutableArray());
        if (batch.RemainingCount == 0 || batch.RemainingCount > owned ||
            missing.Any(leaf => batch.Items.Where(item => item.UnitId == leaf.UnitId)
                .Sum(item => item.RemainingCount) < leaf.MissingCount) ||
            !CanComplete(frame, catalog, available, batch)) return null;
        return new(CoachActionKind.Reward, "guide1:select-common:" + target,
            $"선택위습 {batch.RemainingCount}개로 필요한 흔함 확보",
            "보유 선택위습 선택 → " + List(batch, catalog) + $" · 완료 후 위습 {owned - batch.RemainingCount}개 보존",
            $"진행 목표 {RecommendationPresentation.CoachUnitName(catalog.Unit(target).Name, catalog.Unit(target).Tier)} · " +
            (plan.Stage == BulletGuideStage.FastUniqueRare ? "8라 시작 전 희귀 조합" :
                plan.Stage == BulletGuideStage.BulletMaterials ? "50라 전 불릿 재료 전설 준비" : "12라 전 첫 전설") +
            $"에 필요한 최소 {batch.RemainingCount}개만 사용합니다. 현재 보유 {owned}개이며 미지급 보상은 제외합니다.",
            "목록은 실제 유닛 증가로 줄어듭니다. 위습만 줄고 결과가 보이지 않으면 추가 선택을 멈추세요.",
            BulletGuideAdvice.Stage(plan))
        {
            TargetUnitId = batch.Items.First(item => item.RemainingCount > 0).UnitId, RewardWispId = "e018",
            SelectionBatch = batch, SelectionWispsAfterBatch = owned - batch.RemainingCount, IsUrgent = true,
            GoalLabel = "공략 1 · 더글라스 불릿", OperationGuide = BulletGuideAdvice.Operation(frame),
            PreservedMaterials = "이미 확보한 조합 재료는 유지합니다."
        };
    }

    private static string List(SelectionWispBatch batch, DataCatalog catalog) => string.Join(" → ",
        batch.Items.Where(item => item.RemainingCount > 0)
            .Select(item => $"{catalog.Unit(item.UnitId).Name} {item.RemainingCount}개"));

    // Dry-run actual crafts and only the costs of unowned intermediates. Never publish this projected hand.
    private static bool CanComplete(CoachFrame frame, DataCatalog catalog, Dictionary<string, int> available,
        SelectionWispBatch batch)
    {
        var projected = frame.Inventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var item in batch.Items)
        {
            available[item.UnitId] = checked(available.GetValueOrDefault(item.UnitId) + item.RemainingCount);
            projected[item.UnitId] = checked(projected.GetValueOrDefault(item.UnitId) + item.RemainingCount);
        }
        var resources = frame.Signals.ToDictionary(pair => pair.Key, pair => pair.Value);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        return Ensure(batch.TargetUnitId, 1);

        bool Ensure(string id, int count)
        {
            if (available.GetValueOrDefault(id) >= count) return true;
            var unit = catalog.Unit(id);
            if (unit.Recipe.Count == 0 || !visiting.Add(id)) return false;
            while (available.GetValueOrDefault(id) < count)
            {
                foreach (var ingredient in unit.Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원"))
                    if (!Ensure(ingredient.Key, ingredient.Value)) return false;
                foreach (var cost in unit.Recipe.Where(pair => catalog.Unit(pair.Key).Tier == "자원"))
                {
                    var signal = catalog.Unit(cost.Key).Rawcodes.FirstOrDefault() switch
                    { "GOLD" => "gold", "LUMBER" => "lumber", "POINT" => "trait-points", _ => null };
                    if (signal is null || resources.GetValueOrDefault(signal) is not { } amount || amount < cost.Value)
                        return false;
                    resources[signal] = amount - cost.Value;
                }
                if (!BulletGuideCraftSafety.Allows(catalog, id, projected, frame.Round, frame.ConfirmedNavigation,
                        frame.GuidePlan!.Support?.ArmorTarget ?? 100, frame.GuidePlan.QueenConversionConfirmed,
                        frame.GuidePlan)) return false;
                foreach (var ingredient in unit.Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원"))
                {
                    available[ingredient.Key] -= ingredient.Value;
                    projected[ingredient.Key] -= ingredient.Value;
                }
                available[id] = available.GetValueOrDefault(id) + 1;
                projected[id] = projected.GetValueOrDefault(id) + 1;
            }
            visiting.Remove(id);
            return true;
        }
    }
}
