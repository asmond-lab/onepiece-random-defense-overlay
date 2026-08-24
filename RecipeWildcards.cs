namespace OrandOverlay;

public static class RecipeWildcards
{
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
