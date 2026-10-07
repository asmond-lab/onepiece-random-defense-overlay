namespace OrandOverlay;

public static class AutoStartAdvisor
{
    public sealed record Advice(UnitDefinition Goal, UnitDefinition Rare, long Samples);

    public static Advice? RecommendGoal(DataCatalog catalog, ClearBuildStats? stats,
        IEnumerable<string> ownedUnitIds)
    {
        var owned = ownedUnitIds.ToList();
        var counts = owned.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var rares = owned
            .Select(catalog.Unit)
            .Where(unit => BaseTier(unit.Tier) == "희귀함")
            .DistinctBy(unit => unit.Id)
            .ToList();
        if (rares.Count == 0) return null;
        var tops = catalog.AllUnits
            .Where(unit => IsTopUnitTier(unit.Tier))
            .Where(unit => unit.Recipe.Count > 0 &&
                           AdaptiveRouteEvaluator.ClassifyDamage(unit) != DamageLane.Unknown)
            .DistinctBy(unit => unit.Id)
            .ToList();
        var calculator = new RecipeCompletionCalculator(catalog.Unit);
        return tops.Select(top => (Top: top, Rare: rares.FirstOrDefault(rare =>
                RequiresUnit(catalog, top, rare.Id))))
            .Where(candidate => candidate.Rare is not null)
            .Select(candidate => (candidate.Top, candidate.Rare,
                Progress: calculator.Calculate([candidate.Top.Id], counts),
                Samples: LearnedSelection.GoalSampleCount(stats, candidate.Top)))
            .OrderByDescending(candidate => candidate.Progress.CompletionRatio)
            .ThenBy(candidate => candidate.Progress.MissingLeaves.Sum(leaf => leaf.MissingCount))
            .ThenBy(candidate => candidate.Progress.RequiredLeafCount)
            .ThenByDescending(candidate => candidate.Samples)
            .ThenBy(candidate => candidate.Top.Id, StringComparer.Ordinal)
            .Select(candidate => new Advice(candidate.Top, candidate.Rare!, candidate.Samples))
            .FirstOrDefault();
    }

    /// <summary>목표의 레시피 트리에 해당 유닛이 재료로 포함되는지(재귀).</summary>
    public static bool RequiresUnit(DataCatalog catalog, UnitDefinition root, string unitId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Search(root);

        bool Search(UnitDefinition unit)
        {
            if (!visited.Add(unit.Id)) return false;
            foreach (var childId in unit.Recipe.Keys)
            {
                var child = catalog.Unit(childId);
                if (child.Id.Equals(unitId, StringComparison.OrdinalIgnoreCase)) return true;
                if (Search(child)) return true;
            }
            return false;
        }
    }

    private static bool IsTopUnitTier(string tier) => BaseTier(tier)
        is "신비함" or "초월" or "불멸" or "영원" or "제한됨";

    private static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();
}
