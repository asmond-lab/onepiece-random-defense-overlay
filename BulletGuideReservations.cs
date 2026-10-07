namespace OrandOverlay;

/// <summary>One joint allocation for support roots; never count the same piece twice.</summary>
internal static class BulletGuideReservations
{
    internal static bool IsRecipeStep(DataCatalog catalog, string? root, string step)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return root is not null && Visit(root);
        bool Visit(string id)
        {
            if (!visited.Add(id)) return false;
            if (string.Equals(id, step, StringComparison.OrdinalIgnoreCase)) return true;
            return catalog.Unit(id).Recipe.Any(pair => pair.Value > 0 &&
                catalog.Unit(pair.Key).Tier != "자원" && Visit(pair.Key));
        }
    }

    internal static Dictionary<string, int> Available(DataCatalog catalog,
        IReadOnlyDictionary<string, int> inventory, IEnumerable<string> roots, string? activeTarget)
    {
        var remaining = inventory.ToDictionary(pair => pair.Key, pair => Math.Max(0, pair.Value),
            StringComparer.OrdinalIgnoreCase);
        var activeOwned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var calculator = new RecipeCompletionCalculator(catalog.Unit);
        var orderedRoots = roots.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        // Existing root bodies already belong to those roots, not to an earlier
        // root's descendant recipe. Park them jointly before allocating progress.
        var ownedBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in orderedRoots)
            if (remaining.GetValueOrDefault(root) > 0)
            {
                remaining[root]--;
                ownedBodies.Add(root);
                if (string.Equals(root, activeTarget, StringComparison.OrdinalIgnoreCase))
                    activeOwned[root] = 1;
            }
        // Missing roots retain candidate-independent ownership of shared progress.
        foreach (var root in orderedRoots.Where(id => !ownedBodies.Contains(id)))
        {
            var allocation = calculator.CalculateAllocation([root], remaining);
            if (string.Equals(root, activeTarget, StringComparison.OrdinalIgnoreCase))
                foreach (var pair in allocation.ConsumedByUnitId)
                    activeOwned[pair.Key] = checked((int)pair.Value);
            remaining = allocation.RemainingInventory.ToDictionary(pair => pair.Key,
                pair => checked((int)pair.Value), StringComparer.OrdinalIgnoreCase);
        }
        foreach (var pair in activeOwned)
            remaining[pair.Key] = checked(remaining.GetValueOrDefault(pair.Key) + pair.Value);
        return remaining;
    }
}
