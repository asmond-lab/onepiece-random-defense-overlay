namespace OrandOverlay;

public static class RecommendationInventoryPolicy
{
    public static IReadOnlyList<InventoryEntry> Build(
        IEnumerable<InventoryEntry> current,
        bool preserveCompleted,
        CompletedTopUnitTracker completed,
        bool includeGreenBloodBuff)
    {
        IReadOnlyList<InventoryEntry> result = preserveCompleted
            ? completed.Apply(current)
            : current.ToList();
        if (!includeGreenBloodBuff ||
            result.Any(entry => entry.UnitId == "greenblood_buff" && entry.Count > 0))
            return result;
        return result.Concat(
        [
            new InventoryEntry { UnitId = "greenblood_buff", Count = 1, Confidence = 1 }
        ]).ToList();
    }
}
