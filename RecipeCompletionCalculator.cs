namespace OrandOverlay;

public sealed class ResourceRequirements : IReadOnlyDictionary<string, long>
{
    private readonly IReadOnlyDictionary<string, long> _values;

    internal ResourceRequirements(IReadOnlyDictionary<string, long> values) =>
        _values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, long>(
            new Dictionary<string, long>(values, StringComparer.OrdinalIgnoreCase));

    public long Gold => this["GOLD"];
    public long Lumber => this["LUMBER"];
    public long Point => this["POINT"];
    public long Random => this["RANDOM"];
    public long this[string key] => _values.TryGetValue(key, out var value) ? value : 0;
    public IEnumerable<string> Keys => _values.Keys;
    public IEnumerable<long> Values => _values.Values;
    public int Count => _values.Count;
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool TryGetValue(string key, out long value) => _values.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<string, long>> GetEnumerator() => _values.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class RecipeCompletionAllocation
{
    internal RecipeCompletionAllocation(RecipeAllocationProgress progress,
        IReadOnlyDictionary<string, long> consumedByUnitId,
        IReadOnlyDictionary<string, long> remainingInventory,
        ResourceRequirements resourceRequirements)
    {
        Progress = progress;
        ConsumedByUnitId = new System.Collections.ObjectModel.ReadOnlyDictionary<string, long>(
            new Dictionary<string, long>(consumedByUnitId, StringComparer.OrdinalIgnoreCase));
        RemainingInventory = new System.Collections.ObjectModel.ReadOnlyDictionary<string, long>(
            new Dictionary<string, long>(remainingInventory, StringComparer.OrdinalIgnoreCase));
        ResourceRequirements = resourceRequirements;
    }

    public RecipeAllocationProgress Progress { get; }
    public IReadOnlyDictionary<string, long> ConsumedByUnitId { get; }
    public IReadOnlyDictionary<string, long> RemainingInventory { get; }
    public ResourceRequirements ResourceRequirements { get; }
}

public sealed class RecipeAllocationProgress
{
    internal RecipeAllocationProgress(long requiredLeafCount, long ownedLeafCount,
        IEnumerable<RecipeLeafProgress> leaves)
    {
        RequiredLeafCount = requiredLeafCount;
        OwnedLeafCount = ownedLeafCount;
        Leaves = new System.Collections.ObjectModel.ReadOnlyCollection<RecipeLeafProgress>(
            leaves.ToList());
    }

    public long RequiredLeafCount { get; }
    public long OwnedLeafCount { get; }
    public IReadOnlyList<RecipeLeafProgress> Leaves { get; }

    public double CompletionRatio => RequiredLeafCount <= 0
        ? 1
        : Math.Clamp((double)OwnedLeafCount / RequiredLeafCount, 0, 1);

    public IReadOnlyList<RecipeLeafProgress> MissingLeaves => Leaves
        .Where(x => x.MissingCount > 0)
        .OrderByDescending(x => x.MissingCount)
        .ThenBy(x => x.Name, StringComparer.CurrentCulture)
        .ToList()
        .AsReadOnly();

    internal RecipeProgress ToRecipeProgress() => new()
    {
        RequiredLeafCount = RequiredLeafCount,
        OwnedLeafCount = OwnedLeafCount,
        Leaves = Leaves.ToList()
    };
}

/// <summary>
/// Expands a composition into its lowest non-resource cards and allocates the current inventory
/// top-down. Owning a completed upper/intermediate unit replaces its whole subtree, while the
/// mutable availability map prevents the same card from satisfying two branches.
/// </summary>
public sealed class RecipeCompletionCalculator(Func<string, UnitDefinition> resolveUnit)
{
    private static readonly HashSet<string> ResourcePseudoIds =
        new(["GOLD", "LUMBER", "POINT", "RANDOM"], StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, long>> _leafCache =
        new(StringComparer.OrdinalIgnoreCase);

    public RecipeProgress Calculate(IEnumerable<string> requiredUnitIds,
        IReadOnlyDictionary<string, int> inventory) =>
        CalculateAllocation(requiredUnitIds, inventory).Progress.ToRecipeProgress();

    public RecipeCompletionAllocation CalculateAllocation(IEnumerable<string> requiredUnitIds,
        IReadOnlyDictionary<string, int> inventory)
    {
        var requirements = requiredUnitIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.OrdinalIgnoreCase);
        var availability = inventory
            .ToDictionary(pair => pair.Key, pair => Math.Max(0, (long)pair.Value),
                StringComparer.OrdinalIgnoreCase);
        var originalAvailability = new Dictionary<string, long>(availability,
            StringComparer.OrdinalIgnoreCase);
        var requiredLeaves = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var ownedLeaves = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var resources = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var (unitId, count) in requirements.OrderBy(pair => RecipeWildcards.IsWildcard(pair.Key) ? 1 : 0).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var (leafId, leafCount) in ExpandLeaves(unitId, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                Add(requiredLeaves, leafId, Multiply(leafCount, count));
            foreach (var (resourceId, resourceCount) in ExpandResources(unitId,
                         new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                Add(resources, resourceId, Multiply(resourceCount, count));
        }

        foreach (var (unitId, count) in requirements.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            Allocate(unitId, count, availability, ownedLeaves,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var leaves = requiredLeaves
            .Select(pair =>
            {
                var owned = Math.Min(pair.Value, ownedLeaves.GetValueOrDefault(pair.Key));
                var unit = resolveUnit(pair.Key);
                return new RecipeLeafProgress
                {
                    UnitId = pair.Key,
                    Name = unit.Name,
                    Tier = unit.Tier,
                    Image = unit.Image,
                    RequiredCount = pair.Value,
                    OwnedCount = owned
                };
            })
            .OrderBy(x => x.Name, StringComparer.CurrentCulture)
            .ToList();
        var progress = new RecipeAllocationProgress(
            Sum(leaves.Select(x => x.RequiredCount)),
            Sum(leaves.Select(x => x.OwnedCount)),
            leaves);
        var consumed = originalAvailability
            .Select(pair => new KeyValuePair<string, long>(pair.Key,
                pair.Value - availability.GetValueOrDefault(pair.Key)))
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var remaining = originalAvailability
            .ToDictionary(pair => pair.Key,
                pair => pair.Value - consumed.GetValueOrDefault(pair.Key),
                StringComparer.OrdinalIgnoreCase);
        return new RecipeCompletionAllocation(progress, consumed, remaining,
            new ResourceRequirements(resources));
    }

    private IReadOnlyDictionary<string, long> ExpandResources(string unitId, HashSet<string> visiting)
    {
        if (!visiting.Add(unitId))
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var unit = resolveUnit(unitId);
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (IsResourcePseudo(unit))
            result[CanonicalResourceId(unit)] = 1;
        else
            foreach (var (childId, childCount) in RecipeChildren(unit, includeResources: true))
                foreach (var (resourceId, resourceCount) in ExpandResources(childId, visiting))
                    Add(result, resourceId, Multiply(resourceCount, childCount));
        visiting.Remove(unitId);
        return result;
    }

    private IReadOnlyDictionary<string, long> ExpandLeaves(string unitId, HashSet<string> visiting)
    {
        if (_leafCache.TryGetValue(unitId, out var cached)) return cached;
        var unit = resolveUnit(unitId);
        if (IsResourcePseudo(unit)) return Cache(unitId, new Dictionary<string, long>());
        if (!visiting.Add(unitId))
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { [unitId] = 1 };

        var allChildren = unit.Recipe.Where(pair => pair.Value > 0).ToList();
        var children = RecipeChildren(unit).ToList();
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (allChildren.Count == 0)
            result[unitId] = 1;
        else if (children.Count > 0)
            foreach (var (childId, childCount) in children)
                foreach (var (leafId, leafCount) in ExpandLeaves(childId, visiting))
                    Add(result, leafId, Multiply(leafCount, childCount));
        visiting.Remove(unitId);
        return Cache(unitId, result);
    }

    private void Allocate(string unitId, long demand, IDictionary<string, long> availability,
        IDictionary<string, long> ownedLeaves, HashSet<string> visiting)
    {
        if (demand <= 0) return;
        var unit = resolveUnit(unitId);
        if (IsResourcePseudo(unit)) return;
        if (RecipeWildcards.IsWildcard(unitId))
        {
            foreach (var pair in RecipeWildcards.Candidates(unitId, availability, resolveUnit).ToArray())
            {
                var used = Math.Min(demand, pair.Value);
                availability[pair.Key] -= used;
                Add(ownedLeaves, unitId, used);
                demand -= used;
                if (demand == 0) break;
            }
            return;
        }

        var available = availability.TryGetValue(unitId, out var availableCount) ? availableCount : 0;
        var directlyOwned = Math.Min(demand, available);
        if (directlyOwned > 0)
        {
            availability[unitId] = available - directlyOwned;
            foreach (var (leafId, leafCount) in ExpandLeaves(unitId,
                         new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                Add(ownedLeaves, leafId, Multiply(leafCount, directlyOwned));
            demand -= directlyOwned;
        }
        if (demand == 0 || !visiting.Add(unitId)) return;

        foreach (var (childId, childCount) in RecipeChildren(unit))
            Allocate(childId, Multiply(demand, childCount), availability, ownedLeaves, visiting);
        visiting.Remove(unitId);
    }

    private IEnumerable<KeyValuePair<string, int>> RecipeChildren(UnitDefinition unit,
        bool includeResources = false) => unit.Recipe
        .Where(pair => pair.Value > 0 &&
            (includeResources || !IsResourcePseudo(resolveUnit(pair.Key))))
        .OrderBy(pair => RecipeWildcards.IsWildcard(pair.Key) ? 1 : 0);

    private static string CanonicalResourceId(UnitDefinition unit)
    {
        const string prefix = "rawcode:";
        var rawcode = unit.Rawcodes.FirstOrDefault() ?? unit.Id;
        if (rawcode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            rawcode = rawcode[prefix.Length..];
        return rawcode.ToUpperInvariant();
    }

    private static bool IsResourcePseudo(UnitDefinition unit)
    {
        if (unit.Tier.Equals("자원", StringComparison.OrdinalIgnoreCase)) return true;
        if (unit.Rawcodes.Any(ResourcePseudoIds.Contains)) return true;
        const string prefix = "rawcode:";
        return unit.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               ResourcePseudoIds.Contains(unit.Id[prefix.Length..]);
    }

    private IReadOnlyDictionary<string, long> Cache(string unitId, Dictionary<string, long> value)
    {
        _leafCache[unitId] = value;
        return value;
    }

    private static void Add(IDictionary<string, long> target, string key, long amount) =>
        target[key] = Add(target.TryGetValue(key, out var current) ? current : 0, amount);

    private static long Add(long left, long right) => left >= long.MaxValue - right ? long.MaxValue : left + right;

    private static long Multiply(long left, long right) =>
        left == 0 || right == 0 ? 0 : left > long.MaxValue / right ? long.MaxValue : left * right;

    private static long Sum(IEnumerable<long> values)
    {
        var result = 0L;
        foreach (var value in values) result = Add(result, value);
        return result;
    }
}
