using System.Collections.Immutable;

namespace OrandOverlay;

internal static class AdaptiveRouteAffinityCalculator
{
    internal static (int Bp, int EvidenceConfidenceBp) Score(
        AdaptiveRouteEvaluationInput input, UnitDefinition goal,
        ImmutableArray<string> optionalIds, ImmutableArray<string> excludedIds,
        IReadOnlyDictionary<string, long> inventory,
        RecipeCompletionCalculator calculator)
    {
        var excluded = excludedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var confidence = optionalIds.Any(id => !excluded.Contains(id) && !input.Units.ContainsKey(id))
            ? 6000 : 10000;
        if (!inventory.Values.Any(count => count > 0)) return (0, confidence);
        var profile = input.ClearStats?.ResolveProfile(goal.Rawcodes);
        var historical = profile is not null
            ? FromProfile(input.Units, profile, excluded)
            : FromCommunity(input.Units, goal, excluded);
        var supportIds = input.Candidates
            .Where(candidate => candidate.GoalUnitId.Equals(goal.Id, StringComparison.OrdinalIgnoreCase))
            .SelectMany(candidate => candidate.StrategySupportPoolIds)
            .Where(id => input.Units.TryGetValue(id, out var unit) &&
                         GoalStrategyCalculator.IsCompatibleSupportDamageType(goal, unit) &&
                         (GoalStrategyCalculator.StrategyMetricsFor(unit).HasAny ||
                          GoalStrategyCalculator.AbilityTotal(unit, "공격력 증가", "공격속도 증가") > 0));
        var available = AdaptiveRouteEvaluator.ToInventory(inventory);
        var historyWeights = historical.ToDictionary(item => item.Id, item => item.Weight,
            StringComparer.OrdinalIgnoreCase);
        var roots = optionalIds.Concat(supportIds).Concat(historical.Select(root => root.Id))
            .Where(id => !excluded.Contains(id) && !id.Equals(goal.Id, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(input.Units.ContainsKey)
            .Where(id => input.Units[id].Recipe.Count > 0 || inventory.GetValueOrDefault(id) > 0)
            .Select(id =>
            {
                var allocation = calculator.CalculateAllocation([id], available);
                return (Id: id, Progress: RatioBp(allocation.Progress.OwnedLeafCount,
                    allocation.Progress.RequiredLeafCount));
            })
            .Where(root => root.Progress > 0)
            .OrderByDescending(root => root.Progress)
            .ThenByDescending(root => historyWeights.GetValueOrDefault(root.Id))
            .ThenBy(root => root.Id, StringComparer.Ordinal)
            .Take(3)
            .ToList();
        if (roots.Count == 0) return (0, confidence);
        long weighted = 0;
        long weightTotal = 0;
        foreach (var root in roots)
        {
            if (!input.Units.ContainsKey(root.Id)) continue;
            var allocation = calculator.CalculateAllocation([root.Id], available);
            weighted += RatioBp(allocation.Progress.OwnedLeafCount,
                allocation.Progress.RequiredLeafCount);
            weightTotal++;
            available = AdaptiveRouteEvaluator.ToInventory(allocation.RemainingInventory);
        }
        return (weightTotal == 0 ? 0 : AdaptiveRouteEvaluator.RoundAway(weighted, weightTotal),
            confidence);
    }

    private static List<(string Id, int Weight)> FromProfile(
        IReadOnlyDictionary<string, UnitDefinition> units, GoalClearProfile profile,
        IReadOnlySet<string> excluded) => units.Values
        .Where(unit => !excluded.Contains(unit.Id))
        .Select(unit => new
        {
            unit.Id,
            Share = unit.Rawcodes.Select(code => profile.SupportShare.GetValueOrDefault(code))
                .DefaultIfEmpty().Max(),
        })
        .Where(value => value.Share > 0)
        .OrderByDescending(value => value.Share)
        .ThenBy(value => value.Id, StringComparer.Ordinal)
        .Select(value => (value.Id, Math.Max(1, AdaptiveRouteEvaluator.RoundAway(
            (long)Math.Round(value.Share * 10000, MidpointRounding.AwayFromZero), 1))))
        .ToList();

    private static List<(string Id, int Weight)> FromCommunity(
        IReadOnlyDictionary<string, UnitDefinition> units, UnitDefinition goal,
        IReadOnlySet<string> excluded)
    {
        var priorities = RecommendationCommunityPriorities.ForGoal(goal);
        if (priorities is null || priorities.Count == 0) return [];
        var maximum = priorities.Values.Max();
        return units.Values.Where(unit => !excluded.Contains(unit.Id)).Select(unit => new
            {
                unit.Id,
                Priority = unit.Rawcodes.Select(code => priorities.GetValueOrDefault(code))
                    .DefaultIfEmpty().Max()
            })
            .Where(value => value.Priority > 0)
            .OrderByDescending(value => value.Priority)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .Select(value => (value.Id,
                AdaptiveRouteEvaluator.RoundAway(10000L * value.Priority, maximum)))
            .ToList();
    }

    private static int RatioBp(long owned, long required) => required <= 0
        ? 10000
        : Math.Clamp(AdaptiveRouteEvaluator.RoundAway(checked(10000L * owned), required),
            0, 10000);
}
