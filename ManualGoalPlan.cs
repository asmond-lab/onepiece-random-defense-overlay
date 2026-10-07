using System.Collections.Immutable;

namespace OrandOverlay;

public sealed record ManualGoalProgress(string GoalId, RecipeProgress Progress, bool Owned);

public sealed record ManualGoalPlan(
    ImmutableArray<string> GoalIds,
    string ActiveGoalId,
    RecipeCompletionAllocation Allocation,
    IReadOnlyList<ManualGoalProgress> Progress,
    IReadOnlyDictionary<string, int> RecipeInventory,
    ImmutableHashSet<string> ProtectedGoalIds,
    bool NavigationConflict)
{
    public bool CanCraftNewTop { get; init; } = true;
    public static ManualGoalPlan Create(DataCatalog catalog, IEnumerable<string> selected,
        IEnumerable<InventoryEntry> inventory, string? confirmedNavigation, string difficulty = "unknown")
    {
        var goals = selected.ToImmutableArray();
        if (goals.Length is < 1 or > 2 ||
            goals.Distinct(StringComparer.OrdinalIgnoreCase).Count() != goals.Length ||
            goals.Any(id => !TopGradePolicy.IsTopGrade(catalog.Unit(id).Tier)))
            throw new ArgumentException("Select one or two distinct top units.", nameof(selected));
        if (goals.Length == 2 && catalog.Unit(goals[0]).Rawcodes
                .Intersect(catalog.Unit(goals[1]).Rawcodes, StringComparer.Ordinal).Any())
            throw new ArgumentException("Shared forms cannot be separate goals.", nameof(selected));
        var counts = inventory.Where(entry => entry.Count > 0)
            .GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        var calculator = new RecipeCompletionCalculator(catalog.Unit);
        var allocation = calculator.CalculateAllocation(goals, counts);
        var protectedGoals = goals.Where(id => counts.GetValueOrDefault(id) > 0)
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var recipeInventory = new Dictionary<string, int>(counts, StringComparer.OrdinalIgnoreCase);
        foreach (var id in protectedGoals) recipeInventory[id]--;
        var remaining = recipeInventory;
        var progress = new List<ManualGoalProgress>();
        foreach (var id in goals)
        {
            var owned = protectedGoals.Contains(id);
            var part = calculator.CalculateAllocation([id], owned ? counts : remaining);
            progress.Add(new ManualGoalProgress(id, part.Progress.ToRecipeProgress(),
                owned));
            if (!owned)
                remaining = part.RemainingInventory.ToDictionary(pair => pair.Key,
                    pair => checked((int)pair.Value), StringComparer.OrdinalIgnoreCase);
        }
        var conflict = confirmedNavigation is not null &&
                       NavigationProfiles.Find(confirmedNavigation).TopUnitLimit < goals.Length;
        var active = goals[0];
        if (goals.Length == 2 && protectedGoals.Contains(goals[0]) && !conflict)
        {
            var firstReady = CombatReadinessCalculator.Calculate(catalog, catalog.Unit(goals[0]),
                counts.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }), difficulty);
            if (!protectedGoals.Contains(goals[1]) || firstReady.IsReady) active = goals[1];
        }
        return new ManualGoalPlan(goals, active, allocation, progress, recipeInventory,
            protectedGoals, conflict)
        {
            CanCraftNewTop = confirmedNavigation is null ||
                counts.Where(pair => TopGradePolicy.IsTopGrade(catalog.Unit(pair.Key).Tier))
                    .Sum(pair => pair.Value) < NavigationProfiles.Find(confirmedNavigation).TopUnitLimit
        };
    }
}
