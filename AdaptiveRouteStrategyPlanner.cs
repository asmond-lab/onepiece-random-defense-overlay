using System.Collections.Immutable;

namespace OrandOverlay;

internal static class AdaptiveRouteStrategyPlanner
{
    internal static ImmutableArray<string> Synthesize(UnitDefinition goal,
        ImmutableArray<string> poolIds, IReadOnlyDictionary<string, UnitDefinition> units)
    {
        var profile = GoalStrategyCalculator.StrategyProfileFor(goal);
        if (profile is null) return [];
        var selected = new List<string>();
        var projected = GoalStrategyCalculator.StrategyMetricsFor(goal);
        foreach (var id in poolIds.Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!units.TryGetValue(id, out var unit) ||
                !GoalStrategyCalculator.IsCompatibleSupportDamageType(goal, unit)) continue;
            var next = projected + GoalStrategyCalculator.StrategyMetricsFor(unit);
            if (StrategyBp(next, profile.Value) <= StrategyBp(projected, profile.Value)) continue;
            selected.Add(id);
            projected = next;
            if (StrategyBp(projected, profile.Value) == 10000) break;
        }

        var achievedBp = StrategyBp(projected, profile.Value);
        for (var index = selected.Count - 1; index >= 0; index--)
        {
            var without = selected.Where((_, candidateIndex) => candidateIndex != index)
                .Aggregate(GoalStrategyCalculator.StrategyMetricsFor(goal),
                    (metrics, id) => metrics + GoalStrategyCalculator.StrategyMetricsFor(units[id]));
            if (StrategyBp(without, profile.Value) != achievedBp) continue;
            selected.RemoveAt(index);
        }
        return selected.ToImmutableArray();
    }

    internal static int Score(UnitDefinition goal, IEnumerable<string> packageIds,
        IReadOnlyDictionary<string, UnitDefinition> units)
    {
        var profile = GoalStrategyCalculator.StrategyProfileFor(goal);
        if (profile is null) return 10000;
        var projected = packageIds.Aggregate(GoalStrategyCalculator.StrategyMetricsFor(goal),
            (metrics, id) => units.TryGetValue(id, out var unit)
                ? metrics + GoalStrategyCalculator.StrategyMetricsFor(unit)
                : metrics);
        return StrategyBp(projected, profile.Value);
    }

    private static int StrategyBp(StrategyMetrics metrics, GoalStrategyProfile profile)
    {
        var targets = new List<int>();
        Add(targets, metrics.BossControl, profile.BossControlTarget);
        Add(targets, metrics.BerserkBossControl, profile.BerserkBossControlTarget);
        Add(targets, metrics.Slow, profile.SlowTarget);
        Add(targets, Math.Min(metrics.Stun, profile.StunCap), profile.StunTarget);
        Add(targets, metrics.ArmorReduction, profile.ArmorReductionTarget);
        Add(targets, metrics.ArmorBreak, profile.ArmorBreakTarget);
        Add(targets, metrics.AirMovement, profile.AirMovementTarget);
        Add(targets, metrics.MagicArmorReduction, profile.MagicArmorReductionTarget);
        Add(targets, metrics.SingleDamage, profile.SingleDamageTarget);
        Add(targets, metrics.FinisherDamage, profile.FinisherDamageTarget);
        return targets.Count == 0 ? 10000 : AdaptiveRouteEvaluator.RoundAway(
            targets.Sum(value => (long)value), targets.Count);
    }

    private static void Add(ICollection<int> targets, double projected, double required)
    {
        if (required <= 0) return;
        targets.Add(Math.Min(10000, checked((int)Math.Round(
            10000m * (decimal)projected / (decimal)required,
            MidpointRounding.AwayFromZero))));
    }
}
