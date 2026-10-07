namespace OrandOverlay;

public static class RecipeWildcards
{
    public const string AnyNika = "rawcode:T80H";

    public static bool IsNikaAlternative(UnitDefinition unit) =>
        unit.Rawcodes.Any(code => code is "990H" or "2B0H") && unit.Id != AnyNika;

    public static bool IsWildcard(string id) => id == AnyNika || id == AnySeraphim;

    public static IEnumerable<KeyValuePair<string, long>> Candidates(string wildcard,
        IEnumerable<KeyValuePair<string, long>> inventory, Func<string, UnitDefinition> resolve) =>
        inventory.Where(pair => pair.Value > 0 && !IsWildcard(pair.Key) &&
            (wildcard == AnyNika ? IsNikaAlternative(resolve(pair.Key)) : IsSeraphim(resolve(pair.Key))))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>One shared immediate-material ledger. Wildcard inventory is never evidence.</summary>
    public static IReadOnlyDictionary<string, long>? AllocateDirect(UnitDefinition unit,
        IReadOnlyDictionary<string, int> inventory, Func<string, UnitDefinition> resolve)
    {
        var available = inventory.ToDictionary(p => p.Key, p => Math.Max(0, (long)p.Value), StringComparer.OrdinalIgnoreCase);
        var consumed = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in unit.Recipe.Where(p => p.Value > 0).OrderBy(p => IsWildcard(p.Key) ? 1 : 0))
        {
            var child = resolve(pair.Key);
            if (child.Tier == "자원" || child.Rawcodes.Any(c => c is "LUMBER" or "GOLD" or "POINT" or "RANDOM")) continue;
            long missing = pair.Value;
            var candidates = IsWildcard(pair.Key) ? Candidates(pair.Key, available, resolve).ToArray() :
                new[] { new KeyValuePair<string, long>(pair.Key, available.GetValueOrDefault(pair.Key)) };
            foreach (var candidate in candidates)
            {
                var take = Math.Min(missing, candidate.Value);
                available[candidate.Key] = candidate.Value - take;
                consumed[candidate.Key] = consumed.GetValueOrDefault(candidate.Key) + take;
                missing -= take;
                if (missing == 0) break;
            }
            if (missing > 0) return null;
        }
        return consumed.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Exact materials have already been debited from available; one distinct observed target is selected.</summary>
    public static CraftTargetInstance? SelectObservedA800(IReadOnlyDictionary<string, int> available,
        IReadOnlyList<CraftTargetInstance> instances) => instances
        .Where(instance => instance.HasA800 && !string.IsNullOrWhiteSpace(instance.InstanceId) &&
            !string.IsNullOrWhiteSpace(instance.AppRawcode) &&
            instances.Count(other => other.InstanceId == instance.InstanceId) == 1 &&
            available.GetValueOrDefault(instance.AppRawcode) > 0)
        .OrderBy(instance => instance.InstanceId, StringComparer.Ordinal)
        .FirstOrDefault();

    public const string AnySeraphim = "seraphim_any";

    public static bool IsSeraphim(UnitDefinition unit) =>
        unit.Tier.Split('[', 2)[0].Trim() == "세라핌";

    public static void AddSyntheticCounts(
        IDictionary<string, int> inventory,
        Func<string, UnitDefinition> resolveUnit)
    {
        var count = inventory
            .Where(pair => pair.Value > 0 && IsSeraphim(resolveUnit(pair.Key)))
            .Sum(pair => pair.Value);
        if (count > 0) inventory[AnySeraphim] = count;
    }
}
