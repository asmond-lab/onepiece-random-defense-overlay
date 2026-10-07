namespace OrandOverlay;

// A dormant growth card is an already acquired SPECIAL, not a future reward or a new tier.
// This projector consumes a fresh diagnostic association; it never discovers ownership or
// carries a previous observation forward. Actual craft execution can reserve GrowingIds.
internal static class GrowthMaterialInventory
{
    internal sealed record Projection(IReadOnlyDictionary<uint, int> Rawcodes,
        IReadOnlyList<uint> GrowingRawcodes, int AddedObjects);

    internal static Projection Project(Warcraft300Diagnostic.Inventory inventory,
        Warcraft300Diagnostic.View associationView, ulong? pointer, uint? rawcode,
        Warcraft300HandleStamp? allocation, Func<uint, bool> isGrowthUnit)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(isGrowthUnit);
        if (inventory.CurrentView != associationView)
            throw new InvalidDataException("Growth association belongs to another view/map.");
        var counts = new Dictionary<uint, int>(inventory.Rawcodes);
        if (pointer is null)
        {
            if (rawcode is not null || allocation is not null)
                throw new InvalidDataException("Incomplete absent-growth observation.");
            return new(counts, [], 0);
        }
        if (rawcode is null || allocation is null || !isGrowthUnit(rawcode.Value))
            throw new InvalidDataException("Growth material identity is unverified.");
        var matches = inventory.Units.Where(unit => unit.Address == pointer.Value).ToArray();
        if (matches.Length != 1 || matches[0].Rawcode != rawcode.Value ||
            matches[0].Allocation != allocation.Value)
            throw new InvalidDataException("Growth material not in the same stable WorldFrame generation.");
        var unit = matches[0];
        if (unit.Owner == inventory.CurrentView.Slot)
            return new(counts, [], 0); // Same physical unit already counted through ordinary ownership.
        if (unit.Owner != 27)
            throw new InvalidDataException("Dormant growth must have source-neutral ownership.");
        counts[rawcode.Value] = checked(counts.GetValueOrDefault(rawcode.Value) + 1);
        return new(counts, [rawcode.Value], 1);
    }

    internal static IReadOnlyList<string> ProtectForCraft(IEnumerable<string>? protectedIds,
        IEnumerable<string> growingIds) => (protectedIds ?? [])
        .Concat(growingIds)
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
