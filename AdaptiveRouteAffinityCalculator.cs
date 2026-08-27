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
        var profile = input.ClearStats?.ResolveProfile(goal.Rawcodes);
        var roots = profile is not null
            ? FromProfile(input.Units, profile, excluded)
            : FromCommunity(input.Units, goal, excluded);
        var confidence = profile is not null ? (profile.CoreRawcodes.Count > 0 ? 10000 : 9000)
            : roots.Count > 0 ? 8000 : 6000;
        if (roots.Count == 0)
            roots = optionalIds.Where(id => !excluded.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => (Id: id, Weight: 5000)).ToList();
        if (roots.Count == 0) return (0, confidence);
        var available = AdaptiveRouteEvaluator.ToInventory(inventory);
        long weighted = 0;
        long weightTotal = 0;
        foreach (var root in roots)
        {
            if (!input.Units.ContainsKey(root.Id)) continue;
            var allocation = calculator.CalculateAllocation([root.Id], available);
            weighted += (long)root.Weight * RatioBp(allocation.Progress.OwnedLeafCount,
                allocation.Progress.RequiredLeafCount);
            weightTotal += root.Weight;
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
            Core = unit.Rawcodes.Any(profile.CoreRawcodes.Contains)
        })
        .Where(value => value.Share > 0 && (profile.CoreRawcodes.Count == 0 || value.Core))
        .OrderByDescending(value => value.Share)
        .ThenBy(value => value.Id, StringComparer.Ordinal)
        .Take(3)
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
            .Take(3)
            .Select(value => (value.Id,
                AdaptiveRouteEvaluator.RoundAway(10000L * value.Priority, maximum)))
            .ToList();
    }

    private static int RatioBp(long owned, long required) => required <= 0
        ? 10000
        : Math.Clamp(AdaptiveRouteEvaluator.RoundAway(checked(10000L * owned), required),
            0, 10000);
}
