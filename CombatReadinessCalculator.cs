namespace OrandOverlay;

public enum ReadinessDamageType
{
    Physical,
    Magic
}

public sealed record CombatReadiness(
    ReadinessDamageType DamageType,
    double CurrentStun,
    double RequiredStun,
    double CurrentSlow,
    double RequiredSlow,
    double CurrentArmorReduction,
    double RequiredArmorReduction,
    int CurrentMagicArmorSources,
    int RequiredMagicArmorSources)
{
    public double MissingStun => Math.Max(0, RequiredStun - CurrentStun);
    public double MissingSlow => Math.Max(0, RequiredSlow - CurrentSlow);
    public double MissingArmorReduction =>
        Math.Max(0, RequiredArmorReduction - CurrentArmorReduction);
    public bool MissingMagicArmorSource =>
        CurrentMagicArmorSources < RequiredMagicArmorSources;
    public bool IsReady =>
        MissingStun <= 0 &&
        MissingSlow <= 0 &&
        (DamageType == ReadinessDamageType.Physical
            ? MissingArmorReduction <= 0
            : !MissingMagicArmorSource);
}

public static class CombatReadinessCalculator
{
    public static CombatReadiness Calculate(DataCatalog catalog,
        UnitDefinition goal, IEnumerable<InventoryEntry> inventory)
    {
        var counts = inventory
            .Where(entry => entry.Count > 0)
            .GroupBy(entry => catalog.Unit(entry.UnitId).Id,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        var metrics = new StrategyMetrics();
        foreach (var (unitId, count) in counts)
            metrics += GoalStrategyCalculator.StrategyMetricsFor(
                catalog.Unit(unitId)) * count;
        var magicSourceCount = counts.Keys
            .Select(catalog.Unit)
            .Where(unit => GoalStrategyCalculator
                .StrategyMetricsFor(unit).MagicArmorReduction > 0)
            .Select(unit => unit.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var strategy = GoalStrategyCalculator.StrategyProfileFor(goal) ??
                       new GoalStrategyProfile(0, 0);
        return FromMetrics(goal, strategy, metrics, magicSourceCount);
    }

    internal static CombatReadiness FromMetrics(UnitDefinition goal,
        GoalStrategyProfile strategy, StrategyMetrics metrics,
        int magicSourceCount)
    {
        var magic = GoalStrategyCalculator.IsMagicDamageTier(goal.Tier);
        return new CombatReadiness(
            magic ? ReadinessDamageType.Magic : ReadinessDamageType.Physical,
            metrics.Stun,
            strategy.StunTarget,
            metrics.Slow,
            strategy.SlowTarget,
            metrics.ArmorReduction,
            strategy.ArmorReductionTarget,
            magicSourceCount,
            magic ? Math.Max(1, (int)Math.Ceiling(
                strategy.MagicArmorReductionTarget)) : 0);
    }
}
