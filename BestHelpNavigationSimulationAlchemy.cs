using System.Collections.Immutable;

namespace OrandOverlay;

public sealed partial class BestHelpNavigationSimulation
{
    public BestHelpAlchemyResult SimulateAlchemy(
        BestHelpAlchemyInput input,
        BestHelpRecipeOverrides recipeOverrides)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(recipeOverrides);
        if (input.CurrentMana < 0 || input.HorizonSeconds < 0 ||
            input.Inventory.Any(pair => pair.Value < 0) ||
            input.MissingRouteLeaves.Any(pair => pair.Value < 0))
            return InvalidAlchemy();

        var manaCost = _profile.Alchemy.ManaCost;
        var availableMana = checked(input.CurrentMana * ManaScale +
            input.HorizonSeconds * BestHelpRegen());
        var maxUses = availableMana / checked(manaCost * ManaScale);
        var candidates = new List<AlchemyCandidate>();
        foreach (var owned in input.Inventory.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
        {
            if (owned.Value == 0 || input.ReservedRouteRoots.Contains(owned.Key) ||
                input.ReservedGrowthUnits.Contains(owned.Key) ||
                input.ReservedUniqueUnits.Contains(owned.Key) ||
                input.MissingRouteLeaves.GetValueOrDefault(owned.Key) > 0 ||
                !input.UnitsByRawcode.TryGetValue(owned.Key, out var unit) ||
                !IsAllowedAlchemyTier(unit.Tier) ||
                !recipeOverrides.DirectUnitChildren.TryGetValue(owned.Key, out var children) ||
                children.Count == 0)
                continue;
            candidates.Add(new(owned.Key, owned.Value, children));
        }

        var best = SearchAlchemy(candidates, input.MissingRouteLeaves, maxUses);
        return new()
        {
            Disposition = BestHelpSimulationDisposition.Confirmed,
            Dismantles = best.Dismantles,
            RouteLeavesGained = best.Gains,
            ManaSpent = checked(best.Dismantles.Length * manaCost)
        };
    }

    private AlchemyPlan SearchAlchemy(IReadOnlyList<AlchemyCandidate> candidates,
        IReadOnlyDictionary<string, int> deficits, int maxUses)
    {
        var best = AlchemyPlan.Empty;
        var selected = new List<BestHelpDismantle>();
        var gains = new Dictionary<string, int>(StringComparer.Ordinal);

        void Visit(int index, int remainingUses)
        {
            if (index == candidates.Count || remainingUses == 0)
            {
                var plan = new AlchemyPlan(selected.ToImmutableArray(),
                    gains.ToDictionary(pair => pair.Key, pair => pair.Value,
                        StringComparer.Ordinal));
                if (IsBetterAlchemy(plan, best, deficits)) best = plan;
                return;
            }

            var candidate = candidates[index];
            var upper = Math.Min(candidate.OwnedCount, remainingUses);
            for (var count = 0; count <= upper; count++)
            {
                for (var copy = 0; copy < count; copy++)
                {
                    selected.Add(new(candidate.UnitRawcode, candidate.Children));
                    foreach (var child in candidate.Children)
                        gains[child.Key] = checked(gains.GetValueOrDefault(child.Key) + child.Value);
                }
                Visit(index + 1, remainingUses - count);
                for (var copy = 0; copy < count; copy++)
                {
                    selected.RemoveAt(selected.Count - 1);
                    foreach (var child in candidate.Children)
                    {
                        gains[child.Key] -= child.Value;
                        if (gains[child.Key] == 0) gains.Remove(child.Key);
                    }
                }
            }
        }

        Visit(0, maxUses);
        return best;
    }

    private bool IsAllowedAlchemyTier(string tier)
    {
        var canonical = tier switch
        {
            "특별함" => "special",
            "희귀함" => "rare",
            _ => tier.ToLowerInvariant()
        };
        return _profile.Alchemy.AllowedTiers.Contains(canonical, StringComparer.Ordinal);
    }

    private static bool IsBetterAlchemy(AlchemyPlan candidate, AlchemyPlan current,
        IReadOnlyDictionary<string, int> deficits)
    {
        var candidateGain = Covered(candidate.Gains, deficits);
        var currentGain = Covered(current.Gains, deficits);
        if (candidateGain != currentGain) return candidateGain > currentGain;
        if (candidate.Dismantles.Length != current.Dismantles.Length)
            return candidate.Dismantles.Length < current.Dismantles.Length;
        return string.CompareOrdinal(
            string.Join(',', candidate.Dismantles.Select(value => value.UnitRawcode)),
            string.Join(',', current.Dismantles.Select(value => value.UnitRawcode))) < 0;
    }

    private static int Covered(IReadOnlyDictionary<string, int> gains,
        IReadOnlyDictionary<string, int> deficits) => checked(deficits.Sum(pair =>
        Math.Min(pair.Value, gains.GetValueOrDefault(pair.Key))));

    private static BestHelpAlchemyResult InvalidAlchemy() => new()
    {
        Disposition = BestHelpSimulationDisposition.InvalidInput,
        Dismantles = [],
        RouteLeavesGained = new Dictionary<string, int>(StringComparer.Ordinal)
    };

    private sealed record AlchemyCandidate(string UnitRawcode, int OwnedCount,
        IReadOnlyDictionary<string, int> Children);
    private sealed record AlchemyPlan(ImmutableArray<BestHelpDismantle> Dismantles,
        IReadOnlyDictionary<string, int> Gains)
    {
        public static AlchemyPlan Empty { get; } = new([], new Dictionary<string, int>(
            StringComparer.Ordinal));
    }
}
