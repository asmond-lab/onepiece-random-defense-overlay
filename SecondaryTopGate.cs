namespace OrandOverlay;

internal sealed record SecondaryTopGateResult(
    List<Recommendation> Recommendations,
    int DeferredCount,
    string? DeferredReason);

internal static class SecondaryTopGate
{
    public static SecondaryTopGateResult Apply(
        IReadOnlyList<Recommendation> source,
        string primaryGoalId,
        GoalCarryMode carryMode,
        CombatReadiness readiness,
        int take,
        Func<string, bool> isTopGrade)
    {
        var deduplicated = source
            .GroupBy(item => item.Route.GoalUnitId,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var secondary = deduplicated
            .Where(item =>
                !item.Route.GoalUnitId.Equals(primaryGoalId,
                    StringComparison.OrdinalIgnoreCase) &&
                isTopGrade(item.Route.GoalUnitId))
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.RecipeProgress.CompletionRatio)
            .ThenBy(item => item.RecipeProgress.MissingLeaves
                .Sum(leaf => leaf.MissingCount))
            .ThenBy(item => item.Route.GoalUnitId, StringComparer.Ordinal)
            .ToList();
        if (secondary.Count == 0)
            return new SecondaryTopGateResult(deduplicated, 0, null);

        var canUseSecondary = readiness.IsReady &&
                              carryMode is GoalCarryMode.MultiAllowed or
                                  GoalCarryMode.MultiRequired;
        if (!canUseSecondary)
        {
            var active = deduplicated
                .Where(item => !secondary.Contains(item))
                .Take(take)
                .ToList();
            return new SecondaryTopGateResult(active, secondary.Count,
                readiness.IsReady
                    ? "1상위 우선 — 추가 상위 자동 추천 안 함"
                    : "55라 준비 미달 — 2상위 보류");
        }

        if (carryMode == GoalCarryMode.MultiAllowed)
            return new SecondaryTopGateResult(
                deduplicated.Take(take).ToList(), 0, null);

        var activeWithoutSecondary = deduplicated
            .Where(item => !secondary.Contains(item))
            .ToList();
        if (activeWithoutSecondary.Count >= take)
            return new SecondaryTopGateResult(
                activeWithoutSecondary.Take(take).ToList(),
                secondary.Count,
                "필수 지원 슬롯 우선 — 2상위 보류");
        activeWithoutSecondary.Add(secondary[0]);
        return new SecondaryTopGateResult(
            activeWithoutSecondary.Take(take).ToList(),
            Math.Max(0, secondary.Count - 1),
            secondary.Count > 1 ? "나머지 2상위 후보 보류" : null);
    }
}
