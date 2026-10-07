using System.Collections.Immutable;
namespace OrandOverlay;

public sealed record ShipReservation(long Reserved, long Free, long Missing);
public sealed record ShipReservations(bool IsKnown, ImmutableDictionary<string, ShipReservation> Ships,
    ImmutableArray<string> Roots, string Summary, RecipeCompletionAllocation Allocation, string InputKey)
{
    public ImmutableDictionary<string, long> PreservedCounts => Ships
        .Where(pair => pair.Value.Reserved > 0).ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Reserved);
}

/// <summary>One shared allocation for deduplicated selected, active, guide, committed and protected roots.</summary>
public static class ShipReservationPolicy
{
    public const string Ancient = "rawcode:Y50h", Pirate = "rawcode:060h";
    private static ImmutableArray<string> Roots(CoachFrame frame) => frame.SelectedGoalIds.Concat(
        new[] { frame.GoalId, frame.GuidePlan?.TargetUnitId, frame.CommittedCraftUnitId }.OfType<string>())
        .Concat(frame.GuidePlan?.ProtectedUnitIds ?? []).Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.Ordinal).ToImmutableArray();
    public static string Key(CoachFrame frame) => System.Text.Json.JsonSerializer.Serialize(new {
        frame.MatchGeneration, frame.RecognitionRevision, frame.IsCurrent, HasPlan = frame.GuidePlan is not null,
        Inventory = frame.Inventory.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(), Goals = Roots(frame),
        Protected = (frame.GuidePlan?.ProtectedUnitIds ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray() });
    public static bool Matches(ShipReservations allocation, CoachFrame frame) => allocation.InputKey == Key(frame);
    public static ShipReservations Evaluate(CoachFrame frame, DataCatalog catalog)
    {
        var roots = Roots(frame);
        var protectedOwned = (frame.GuidePlan?.ProtectedUnitIds ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(id => frame.Inventory.GetValueOrDefault(id) > 0)
            .ToDictionary(id => id, id => frame.Inventory[id], StringComparer.OrdinalIgnoreCase);
        // Park protected physical bodies before traversing any other root. Root sort order
        // must never let a parent consume one and then merely promise to rebuild it.
        var available = frame.Inventory.Where(pair => !protectedOwned.ContainsKey(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var pendingRoots = roots.Where(id => !protectedOwned.ContainsKey(id)).ToArray();
        var knownIds = catalog.AllUnits.Select(unit => unit.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool KnownTree(string id, HashSet<string> visiting)
        {
            var unit = catalog.Unit(id);
            if (unit.Tier == "자원") return true;
            if (unit.Recipe.Count == 0 && frame.Inventory.GetValueOrDefault(unit.Id) <= 0 &&
                (TopGradePolicy.IsTopGrade(unit.Tier) || RecipeWildcards.IsSeraphim(unit))) return false;

            if (!(knownIds.Contains(unit.Id) || unit.Rawcodes.Any(catalog.RawcodeCatalog.ContainsKey)) ||
                !visiting.Add(unit.Id)) return false;
            var known = unit.Recipe.All(pair => pair.Value > 0 && KnownTree(pair.Key, visiting));
            visiting.Remove(unit.Id);
            return known;
        }
        var known = frame.IsCurrent && frame.GuidePlan is not null &&
            roots.All(id => KnownTree(id, new(StringComparer.OrdinalIgnoreCase)));
        var allocation = new RecipeCompletionCalculator(catalog.Unit).CalculateAllocation(pendingRoots, available);
        var ships = new[] { Ancient, Pirate }.ToImmutableDictionary(id => id, id => new ShipReservation(
            allocation.ConsumedByUnitId.GetValueOrDefault(id) + protectedOwned.GetValueOrDefault(id), allocation.RemainingInventory.GetValueOrDefault(id),
            allocation.Progress.MissingLeaves.Where(leaf => leaf.UnitId == id).Sum(leaf => leaf.MissingCount)));
        var summary = "현재 조합 목표와 남겨 둘 유닛: " + string.Join(" · ", roots.Select(id => catalog.Unit(id).Name)) + "\n" +
            string.Join(" · ", ships.OrderBy(pair => pair.Key).Select(pair =>
                $"{catalog.Unit(pair.Key).Name} 남겨 둘 수 {pair.Value.Reserved} / 부족 {pair.Value.Missing} / 여유 {pair.Value.Free}"));
        if (!known) summary = "필요한 배 수를 아직 계산할 수 없습니다. 조합식이나 현재 필요한 재료를 확인하지 못했습니다. 배를 남겨 두세요. 여유가 0이거나 배가 필요 없다는 뜻은 아닙니다.\n" +
            "목표: " + string.Join(" · ", roots.Select(id => catalog.Unit(id).Name));
        return new(known, ships, roots, summary, allocation, Key(frame));
    }
}
