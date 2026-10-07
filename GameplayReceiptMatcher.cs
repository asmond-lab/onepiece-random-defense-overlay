using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OrandOverlay;

public enum GameplayReceiptMatchOutcome
{
    NotMatched,
    IsMatched,
    Ambiguous,
    BoundExceeded
}

/// <summary>A direct, observed craft output and its exact inventory consumption.</summary>
public sealed record GameplayCraftReceipt(
    string UnitId,
    int Count,
    ImmutableDictionary<string, int> Consumed);

/// <summary>Observed outputs attributable to one supported selection-wisp tier.</summary>
public sealed record GameplaySelectionReceipt(
    string WispId,
    int Count,
    ImmutableDictionary<string, int> Outputs);

/// <summary>Pure receipt matching result. The caller decides whether and how to emit schema-3 events.</summary>
public sealed record GameplayReceiptMatchResult(
    GameplayReceiptMatchOutcome Outcome,
    ImmutableArray<GameplayCraftReceipt> Crafts,
    ImmutableArray<GameplaySelectionReceipt> Selections)
{
    public bool IsMatched => Outcome == GameplayReceiptMatchOutcome.IsMatched;
    public bool IsAmbiguous => Outcome == GameplayReceiptMatchOutcome.Ambiguous;
    public bool IsBoundExceeded => Outcome == GameplayReceiptMatchOutcome.BoundExceeded;
}

/// <summary>
/// Matches only exact whole-inventory evidence. It does not use recommendations, recursively expand
/// recipes, or invent selected-then-consumed intermediate units.
/// </summary>
public static class GameplayReceiptMatcher
{
    public const int MaxAssignmentBranches = 4096;
    public const int MaxDistinctGains = 64;
    public const long MaxChangedUnitTotal = 1_000_000;
    public const int MaxInventoryEntries = 512;

    private static readonly ImmutableDictionary<string, string> SelectionTiers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["e018"] = "흔함",
            ["e017"] = "안흔함",
            ["e016"] = "특별함",
            ["e019"] = "희귀함"
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly GameplayReceiptMatchResult NoMatch = new(
        GameplayReceiptMatchOutcome.NotMatched, [], []);
    private static readonly GameplayReceiptMatchResult BoundsExceeded = new(
        GameplayReceiptMatchOutcome.BoundExceeded, [], []);
    private static readonly GameplayReceiptMatchResult Ambiguous = new(
        GameplayReceiptMatchOutcome.Ambiguous, [], []);

    public static GameplayReceiptMatchResult Match(
        GameplayTelemetryObservation before,
        GameplayTelemetryObservation after,
        DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(catalog);

        if (before.Inventory is null || after.Inventory is null ||
            before.RewardWisps is null || after.RewardWisps is null ||
            before.Inventory.Count > MaxInventoryEntries || after.Inventory.Count > MaxInventoryEntries)
            return BoundsExceeded;
        if (before.Inventory.Values.Any(value => value < 0) || after.Inventory.Values.Any(value => value < 0) ||
            before.RewardWisps.Values.Any(value => value < 0) || after.RewardWisps.Values.Any(value => value < 0))
            return NoMatch;

        var gains = new Dictionary<string, int>(StringComparer.Ordinal);
        var losses = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in before.Inventory.Keys.Union(after.Inventory.Keys, StringComparer.Ordinal))
        {
            var delta = (long)after.Inventory.GetValueOrDefault(id) - before.Inventory.GetValueOrDefault(id);
            if (delta > 0) gains[id] = checked((int)delta);
            else if (delta < 0) losses[id] = checked((int)-delta);
        }

        if (gains.Count == 0) return NoMatch;
        if (gains.Count > MaxDistinctGains) return BoundsExceeded;
        long changedTotal = 0;
        try
        {
            foreach (var value in gains.Values.Concat(losses.Values))
                changedTotal = checked(changedTotal + value);
        }
        catch (OverflowException)
        {
            return BoundsExceeded;
        }
        if (changedTotal > MaxChangedUnitTotal) return BoundsExceeded;
        if (gains.Keys.Concat(losses.Keys).Distinct(StringComparer.Ordinal).Any(id => !IsKnownUnit(catalog, id)))
            return NoMatch;

        var spentWisps = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in before.RewardWisps.Keys.Union(after.RewardWisps.Keys, StringComparer.Ordinal))
        {
            var delta = (long)after.RewardWisps.GetValueOrDefault(id) - before.RewardWisps.GetValueOrDefault(id);
            if (delta == 0) continue;
            // Random, transcendence, unknown types, increases, and deceptive underflow never produce receipts.
            if (!SelectionTiers.ContainsKey(id) || delta >= 0) return NoMatch;
            spentWisps[id] = checked((int)-delta);
        }

        if (spentWisps.Count == 0)
            return losses.Count == 0 ? NoMatch : MatchCraftOnly(gains, losses, catalog);
        if (losses.Count == 0)
            return MatchSelectionOnly(gains, spentWisps, catalog);
        return MatchCombined(gains, losses, spentWisps, catalog);
    }

    private static GameplayReceiptMatchResult MatchCraftOnly(
        IReadOnlyDictionary<string, int> gains,
        IReadOnlyDictionary<string, int> losses,
        DataCatalog catalog)
    {
        var required = new Dictionary<string, long>(StringComparer.Ordinal);
        var recipes = new Dictionary<string, ImmutableArray<RecipeTerm>>(StringComparer.Ordinal);
        try
        {
            foreach (var gain in gains.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!TryDirectRecipe(catalog, gain.Key, out var recipe)) return NoMatch;
                recipes[gain.Key] = recipe;
                foreach (var term in recipe)
                    required[term.Id] = checked(required.GetValueOrDefault(term.Id) + checked((long)term.Count * gain.Value));
            }
        }
        catch (OverflowException)
        {
            return BoundsExceeded;
        }

        if (!ExactLosses(required, losses)) return NoMatch;
        try
        {
            var crafts = gains.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(gain => new GameplayCraftReceipt(
                    gain.Key,
                    gain.Value,
                    recipes[gain.Key].ToImmutableDictionary(
                        term => term.Id,
                        term => checked(term.Count * gain.Value),
                        StringComparer.Ordinal)))
                .ToImmutableArray();
            return Matched(crafts, []);
        }
        catch (OverflowException)
        {
            return BoundsExceeded;
        }
    }

    private static GameplayReceiptMatchResult MatchSelectionOnly(
        IReadOnlyDictionary<string, int> gains,
        IReadOnlyDictionary<string, int> spentWisps,
        DataCatalog catalog)
    {
        var byTier = gains.GroupBy(
                gain => TopGradePolicy.BaseTier(catalog.Unit(gain.Key).Tier),
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var expectedTiers = spentWisps.Values.Sum(value => (long)value);
        if (expectedTiers != gains.Values.Sum(value => (long)value)) return NoMatch;

        var selections = ImmutableArray.CreateBuilder<GameplaySelectionReceipt>();
        foreach (var spent in spentWisps.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var tier = SelectionTiers[spent.Key];
            if (!byTier.TryGetValue(tier, out var outputs) ||
                outputs.Sum(pair => (long)pair.Value) != spent.Value)
                return NoMatch;
            selections.Add(new GameplaySelectionReceipt(
                spent.Key,
                spent.Value,
                outputs.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
            byTier.Remove(tier);
        }
        if (byTier.Count != 0 || selections.Any(selection => selection.Outputs.Count == 0)) return NoMatch;
        return Matched([], selections.ToImmutable());
    }

    private static GameplayReceiptMatchResult MatchCombined(
        IReadOnlyDictionary<string, int> gains,
        IReadOnlyDictionary<string, int> losses,
        IReadOnlyDictionary<string, int> spentWisps,
        DataCatalog catalog)
    {
        var search = new AssignmentSearch(gains, losses, spentWisps, catalog);
        search.Run();
        if (search.IsBoundExceeded) return BoundsExceeded;
        if (search.SolutionCount > 1) return Ambiguous;
        if (search.SolutionCount == 0 || search.CraftCounts is null) return NoMatch;

        try
        {
            var crafts = ImmutableArray.CreateBuilder<GameplayCraftReceipt>();
            var outputBuilders = spentWisps.Keys.ToDictionary(
                id => id,
                _ => ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal),
                StringComparer.Ordinal);
            foreach (var plan in search.Plans)
            {
                var crafted = search.CraftCounts[plan.Index];
                var selected = checked(plan.GainCount - crafted);
                if (crafted > 0)
                {
                    crafts.Add(new GameplayCraftReceipt(
                        plan.Id,
                        crafted,
                        plan.Recipe.ToImmutableDictionary(
                            term => term.Id,
                            term => checked(term.Count * crafted),
                            StringComparer.Ordinal)));
                }
                if (selected > 0)
                {
                    if (plan.WispId is null) return NoMatch;
                    outputBuilders[plan.WispId][plan.Id] = selected;
                }
            }

            var selections = spentWisps.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(spent => new GameplaySelectionReceipt(
                    spent.Key,
                    spent.Value,
                    outputBuilders[spent.Key].ToImmutable()))
                .ToImmutableArray();
            if (selections.Any(selection => selection.Outputs.Count == 0 ||
                selection.Outputs.Values.Sum(value => (long)value) != selection.Count))
                return NoMatch;
            return Matched(
                crafts.OrderBy(craft => craft.UnitId, StringComparer.Ordinal).ToImmutableArray(),
                selections);
        }
        catch (OverflowException)
        {
            return BoundsExceeded;
        }
    }

    private static bool TryDirectRecipe(DataCatalog catalog, string outputId, out ImmutableArray<RecipeTerm> recipe)
    {
        var builder = ImmutableArray.CreateBuilder<RecipeTerm>();
        foreach (var pair in catalog.Unit(outputId).Recipe.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (pair.Value <= 0 || !IsKnownUnit(catalog, pair.Key) ||
                pair.Key.Equals(outputId, StringComparison.Ordinal))
            {
                recipe = [];
                return false;
            }
            if (TopGradePolicy.BaseTier(catalog.Unit(pair.Key).Tier) == "자원") continue;
            builder.Add(new RecipeTerm(pair.Key, pair.Value));
        }
        recipe = builder.ToImmutable();
        return recipe.Length > 0;
    }

    private static bool IsKnownUnit(DataCatalog catalog, string id) =>
        catalog.UnitsById.ContainsKey(id) ||
        id.StartsWith("rawcode:", StringComparison.Ordinal) && catalog.RawcodeCatalog.ContainsKey(id[8..]);

    private static bool ExactLosses(
        IReadOnlyDictionary<string, long> required,
        IReadOnlyDictionary<string, int> losses) =>
        required.Count == losses.Count &&
        required.All(pair => losses.TryGetValue(pair.Key, out var observed) && pair.Value == observed);

    private static GameplayReceiptMatchResult Matched(
        ImmutableArray<GameplayCraftReceipt> crafts,
        ImmutableArray<GameplaySelectionReceipt> selections) =>
        new(GameplayReceiptMatchOutcome.IsMatched, crafts, selections);

    private readonly record struct RecipeTerm(string Id, int Count);

    private sealed class GainPlan
    {
        public required int Index { get; init; }
        public required string Id { get; init; }
        public required int GainCount { get; init; }
        public required ImmutableArray<RecipeTerm> Recipe { get; init; }
        public string? WispId { get; init; }
        public int WispIndex { get; init; } = -1;
        public ImmutableArray<IndexedRecipeTerm> IndexedRecipe { get; init; } = [];
        public bool CanCraft { get; init; }
    }

    private readonly record struct IndexedRecipeTerm(int LossIndex, int Count);

    private sealed class AssignmentSearch
    {
        private readonly int[] _lossTargets;
        private readonly long[] _usedLosses;
        private readonly int[] _selectionTargets;
        private readonly long[] _selected;
        private readonly List<GainPlan> _variables = [];
        private readonly int[] _workingCraftCounts;
        private long[,] _remainingSelectionCapacity = new long[0, 0];
        private int _branches;

        public AssignmentSearch(
            IReadOnlyDictionary<string, int> gains,
            IReadOnlyDictionary<string, int> losses,
            IReadOnlyDictionary<string, int> spentWisps,
            DataCatalog catalog)
        {
            var lossIds = losses.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var lossIndexes = lossIds.Select((id, index) => (id, index))
                .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
            _lossTargets = lossIds.Select(id => losses[id]).ToArray();
            _usedLosses = new long[_lossTargets.Length];

            var wisps = spentWisps.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var wispIndexes = wisps.Select((id, index) => (id, index))
                .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
            var wispByTier = wisps.ToDictionary(id => SelectionTiers[id], id => id, StringComparer.Ordinal);
            _selectionTargets = wisps.Select(id => spentWisps[id]).ToArray();
            _selected = new long[_selectionTargets.Length];

            var orderedGains = gains.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            _workingCraftCounts = new int[orderedGains.Length];
            var plans = ImmutableArray.CreateBuilder<GainPlan>(orderedGains.Length);
            for (var index = 0; index < orderedGains.Length; index++)
            {
                var gain = orderedGains[index];
                var hasRecipe = TryDirectRecipe(catalog, gain.Key, out var recipe);
                var allRecipeLossesObserved = hasRecipe && recipe.All(term => lossIndexes.ContainsKey(term.Id));
                var tier = TopGradePolicy.BaseTier(catalog.Unit(gain.Key).Tier);
                var wispId = wispByTier.GetValueOrDefault(tier);
                var plan = new GainPlan
                {
                    Index = index,
                    Id = gain.Key,
                    GainCount = gain.Value,
                    Recipe = recipe,
                    WispId = wispId,
                    WispIndex = wispId is null ? -1 : wispIndexes[wispId],
                    IndexedRecipe = allRecipeLossesObserved
                        ? recipe.Select(term => new IndexedRecipeTerm(lossIndexes[term.Id], term.Count)).ToImmutableArray()
                        : [],
                    CanCraft = allRecipeLossesObserved
                };
                plans.Add(plan);
            }
            Plans = plans.ToImmutable();
        }

        public ImmutableArray<GainPlan> Plans { get; }
        public int SolutionCount { get; private set; }
        public int[]? CraftCounts { get; private set; }
        public bool IsBoundExceeded { get; private set; }

        public void Run()
        {
            foreach (var plan in Plans)
            {
                if (plan.WispIndex < 0)
                {
                    if (!plan.CanCraft || !TryApplyCraft(plan, plan.GainCount)) return;
                    _workingCraftCounts[plan.Index] = plan.GainCount;
                }
                else if (!plan.CanCraft)
                {
                    _selected[plan.WispIndex] += plan.GainCount;
                    if (_selected[plan.WispIndex] > _selectionTargets[plan.WispIndex]) return;
                }
                else
                {
                    _variables.Add(plan);
                }
            }

            _remainingSelectionCapacity = new long[_variables.Count + 1, _selectionTargets.Length];
            for (var position = _variables.Count - 1; position >= 0; position--)
            {
                for (var wisp = 0; wisp < _selectionTargets.Length; wisp++)
                    _remainingSelectionCapacity[position, wisp] = _remainingSelectionCapacity[position + 1, wisp];
                var plan = _variables[position];
                _remainingSelectionCapacity[position, plan.WispIndex] += plan.GainCount;
            }
            for (var wisp = 0; wisp < _selectionTargets.Length; wisp++)
                if (_selected[wisp] > _selectionTargets[wisp] ||
                    _selected[wisp] + _remainingSelectionCapacity[0, wisp] < _selectionTargets[wisp])
                    return;

            Visit(0);
        }

        private void Visit(int position)
        {
            if (IsBoundExceeded || SolutionCount > 1) return;
            if (position == _variables.Count)
            {
                if (!_selected.Select((value, index) => value == _selectionTargets[index]).All(value => value) ||
                    !_usedLosses.Select((value, index) => value == _lossTargets[index]).All(value => value))
                    return;
                SolutionCount++;
                if (SolutionCount == 1) CraftCounts = (int[])_workingCraftCounts.Clone();
                return;
            }

            var plan = _variables[position];
            var wisp = plan.WispIndex;
            var neededSelection = _selectionTargets[wisp] - _selected[wisp];
            var futureCapacity = _remainingSelectionCapacity[position + 1, wisp];
            var minimumSelected = Math.Max(0, neededSelection - futureCapacity);
            var maximumSelected = Math.Min(plan.GainCount, neededSelection);
            for (var selectedFromGain = minimumSelected; selectedFromGain <= maximumSelected; selectedFromGain++)
            {
                if (_branches >= MaxAssignmentBranches)
                {
                    IsBoundExceeded = true;
                    return;
                }
                _branches++;
                var crafted = checked(plan.GainCount - (int)selectedFromGain);
                if (!TryApplyCraft(plan, crafted))
                {
                    if (IsBoundExceeded) return;
                    continue;
                }
                _workingCraftCounts[plan.Index] = crafted;
                _selected[wisp] += selectedFromGain;
                Visit(position + 1);
                _selected[wisp] -= selectedFromGain;
                UndoCraft(plan, crafted);
                _workingCraftCounts[plan.Index] = 0;
                if (IsBoundExceeded || SolutionCount > 1) return;
            }
        }

        private bool TryApplyCraft(GainPlan plan, int count)
        {
            if (count == 0) return true;
            try
            {
                foreach (var term in plan.IndexedRecipe)
                {
                    var contribution = checked((long)term.Count * count);
                    if (checked(_usedLosses[term.LossIndex] + contribution) > _lossTargets[term.LossIndex])
                        return false;
                }
                foreach (var term in plan.IndexedRecipe)
                    _usedLosses[term.LossIndex] = checked(
                        _usedLosses[term.LossIndex] + checked((long)term.Count * count));
                return true;
            }
            catch (OverflowException)
            {
                IsBoundExceeded = true;
                return false;
            }
        }

        private void UndoCraft(GainPlan plan, int count)
        {
            if (count == 0) return;
            foreach (var term in plan.IndexedRecipe)
                _usedLosses[term.LossIndex] -= (long)term.Count * count;
        }
    }
}
