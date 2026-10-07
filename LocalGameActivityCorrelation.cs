using System.Collections.Immutable;

namespace OrandOverlay;

internal sealed record ActivityRecipeMatch(
    ActivityRecipeRule Rule, int Count, string Correlation);

internal sealed class LocalGameRecipeCorrelation(ImmutableArray<ActivityRecipeRule> rules)
{
    private PendingRecipe? _pending;

    internal ImmutableArray<ActivityRecipeMatch> Observe(
        ImmutableDictionary<string, int> added,
        ImmutableDictionary<string, int> removed)
    {
        var direct = Match(added, removed, "same-observation");
        if (direct.Length > 0)
        {
            _pending = null;
            return direct;
        }

        if (_pending is { } pending)
        {
            _pending = null;
            var correlated = Match(
                Merge(pending.Added, added),
                Merge(pending.Removed, removed),
                "adjacent-observation-correlation");
            if (correlated.Length > 0) return correlated;
        }

        if (CanStart(added, removed))
            _pending = new(added, removed);
        return [];
    }

    internal void Reset() => _pending = null;

    private ImmutableArray<ActivityRecipeMatch> Match(
        IReadOnlyDictionary<string, int> added,
        IReadOnlyDictionary<string, int> removed,
        string correlation) =>
        rules.Select(rule => new ActivityRecipeMatch(
                rule, ExactCount(rule, added, removed), correlation))
            .Where(match => match.Count > 0)
            .ToImmutableArray();

    private bool CanStart(IReadOnlyDictionary<string, int> added,
        IReadOnlyDictionary<string, int> removed)
    {
        if (added.Count == 1 && removed.Count == 0)
            return rules.Any(rule => added.TryGetValue(rule.OutputRawcode, out var count) &&
                count > 0 && count % rule.OutputCount == 0);
        if (removed.Count > 0 && added.Count == 0)
            return rules.Any(rule => IngredientCount(rule, removed) > 0);
        return false;
    }

    private static int ExactCount(ActivityRecipeRule rule,
        IReadOnlyDictionary<string, int> added,
        IReadOnlyDictionary<string, int> removed)
    {
        if (rule.Ingredients.Count == 0 || added.Count != 1 ||
            !added.TryGetValue(rule.OutputRawcode, out var outputCount) ||
            outputCount <= 0 || outputCount % rule.OutputCount != 0)
            return 0;
        var count = outputCount / rule.OutputCount;
        return IngredientCount(rule, removed) == count ? count : 0;
    }

    private static int IngredientCount(ActivityRecipeRule rule,
        IReadOnlyDictionary<string, int> removed)
    {
        if (rule.Ingredients.Count == 0 || removed.Count != rule.Ingredients.Count)
            return 0;
        var first = rule.Ingredients.First();
        if (!removed.TryGetValue(first.Key, out var removedFirst) ||
            removedFirst <= 0 || removedFirst % first.Value != 0)
            return 0;
        var count = removedFirst / first.Value;
        return rule.Ingredients.All(pair =>
            removed.GetValueOrDefault(pair.Key) == pair.Value * count) ? count : 0;
    }

    private static ImmutableDictionary<string, int> Merge(
        IReadOnlyDictionary<string, int> first,
        IReadOnlyDictionary<string, int> second) =>
        first.Keys.Concat(second.Keys).Distinct(StringComparer.Ordinal)
            .ToImmutableDictionary(key => key,
                key => first.GetValueOrDefault(key) + second.GetValueOrDefault(key),
                StringComparer.Ordinal);

    private sealed record PendingRecipe(
        ImmutableDictionary<string, int> Added,
        ImmutableDictionary<string, int> Removed);
}

internal sealed record WispEvidenceUpdate(
    bool Correlated,
    string? ArrivalOrder,
    int MatchedCount,
    string? Reason,
    int UnmatchedRawCount,
    int UnmatchedCounterCount);

internal sealed record WispCorrelationResult(
    int ActionRawCount,
    int? ActionCounterCount,
    WispEvidenceUpdate? Evidence);

internal sealed record WispEvidenceFrame(
    string RuleId,
    int RawCount,
    int? CounterCount,
    DateTimeOffset ObservedAt,
    bool IsUnchanged,
    bool IsCompatible);

internal sealed class LocalWispEvidenceCorrelation
{
    private readonly Dictionary<string, PendingWisp> _pending = new(StringComparer.Ordinal);

    internal WispCorrelationResult Observe(WispEvidenceFrame frame)
    {
        var remainingRaw = frame.RawCount;
        var remainingCounter = frame.CounterCount ?? 0;
        WispEvidenceUpdate? evidence = null;
        if (_pending.TryGetValue(frame.RuleId, out var pending))
        {
            var age = frame.ObservedAt - pending.ObservedAt;
            var counterFirst = Math.Min(remainingRaw, pending.CounterCount);
            var rawFirst = Math.Min(remainingCounter, pending.RawCount);
            var matched = counterFirst + rawFirst;
            if (age < TimeSpan.Zero || age > DiagnosticInventoryObservation.FreshnessBudget)
            {
                _pending.Remove(frame.RuleId);
                evidence = new(false, null, 0,
                    age < TimeSpan.Zero ? "source-time-order" : "freshness-budget",
                    pending.RawCount, pending.CounterCount);
            }
            else if (matched > 0 && frame.IsCompatible)
            {
                _pending.Remove(frame.RuleId);
                remainingRaw -= counterFirst;
                remainingCounter -= rawFirst;
                evidence = new(true,
                    counterFirst > 0 ? "counter-first" : "raw-first",
                    matched, null,
                    pending.RawCount - rawFirst,
                    pending.CounterCount - counterFirst);
            }
            else if (frame.IsUnchanged)
            {
                return new(0, frame.CounterCount.HasValue ? 0 : null, null);
            }
            else
            {
                _pending.Remove(frame.RuleId);
                evidence = new(false, null, 0,
                    matched > 0 ? "incompatible-delta" :
                    frame.IsCompatible ? "noncomplementary-action" : "unrelated-observation",
                    pending.RawCount, pending.CounterCount);
            }
        }

        var paired = Math.Min(remainingRaw, remainingCounter);
        var pendingRaw = remainingRaw - paired;
        var pendingCounter = remainingCounter - paired;
        if (pendingRaw > 0 || pendingCounter > 0)
            _pending[frame.RuleId] = new(pendingRaw, pendingCounter, frame.ObservedAt);

        int? actionCounter = frame.CounterCount.HasValue ? remainingCounter : null;
        return new(remainingRaw, actionCounter, evidence);
    }

    internal void Reset() => _pending.Clear();

    private sealed record PendingWisp(
        int RawCount, int CounterCount, DateTimeOffset ObservedAt);
}
