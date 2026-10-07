using System.Collections.Immutable;

namespace OrandOverlay;

internal sealed class LocalGameActivityCapture(
    Map2321ActivityRules rules, Action<string, object, long?, long?> emit)
{
    private Observation? _previous;
    private readonly LocalGameRecipeCorrelation _recipeCorrelation = new(rules.Recipes);
    private readonly LocalWispEvidenceCorrelation _wispCorrelation = new();
    private readonly LocalGameGambleInterpretation _gambleInterpretation = new(rules.Gambles);

    internal void Observe(IDiagnosticInventoryReference value, RecognitionDiagnostics diagnostics, long generation)
    {
        if (value.SelectedMapVersion != rules.MapVersion)
        {
            Fence("source-version-changed", generation, value.SourceRevision);
            return;
        }
        var rawcodes = diagnostics.ActivityProjectedRawcodes ?? diagnostics.ActivityRawcodes;
        if (rawcodes is null)
        {
            Fence("inventory-evidence-missing", generation, value.SourceRevision);
            emit("game.evidence-missing", new
            {
                Evidence = "activity-rawcodes-unavailable",
                diagnostics.ActivityCounterStatus,
                CorrelationReset = true
            }, generation, value.SourceRevision);
            return;
        }

        var context = string.Join("|", generation, value.SelectedMapVersion, value.DatasetFingerprint,
            value.BindingContextId.Length > 0 ? value.BindingContextId : value.ContextId, value.ViewSlot);
        var current = new Observation(context, value.SourceRevision, value.CompletedAt, rawcodes,
            diagnostics.ActivityCounters ?? ImmutableDictionary<string, int>.Empty);
        if (_previous is not { } previous)
        {
            _previous = current;
            return;
        }
        if (!StringComparer.Ordinal.Equals(previous.Context, current.Context) ||
            current.Revision <= previous.Revision)
        {
            Fence(previous.Context == current.Context ? "revision-rollback" : "context-changed",
                generation, value.SourceRevision);
            _previous = current;
            return;
        }
        if (current.Counters.Any(pair =>
                previous.Counters.TryGetValue(pair.Key, out var oldValue) && pair.Value < oldValue))
        {
            Fence("counter-rollback", generation, value.SourceRevision);
            _previous = current;
            return;
        }

        var added = InventoryDifference(previous.Rawcodes, current.Rawcodes);
        var removed = InventoryDifference(current.Rawcodes, previous.Rawcodes);
        var counterDeltas = CounterDeltas(previous.Counters, current.Counters);
        var counterIncrements = counterDeltas
            .Where(pair => pair.Value > 0)
            .ToImmutableDictionary(StringComparer.Ordinal);
        var newlyObservedCounters = current.Counters
            .Where(pair => !previous.Counters.ContainsKey(pair.Key))
            .ToImmutableDictionary(StringComparer.Ordinal);
        var counterCoverageChanged = newlyObservedCounters.Count > 0 ||
            previous.Counters.Keys.Any(name => !current.Counters.ContainsKey(name));
        var transition = new Transition(added, removed, counterDeltas,
            counterCoverageChanged, current.CompletedAt, generation, value.SourceRevision);

        EmitRecipe(transition);
        EmitWisps(transition);
        EmitGambles(transition);
        if (newlyObservedCounters.Count > 0)
            emit("game.counter-observed", new
            {
                Values = newlyObservedCounters,
                DeltaKnown = false,
                Evidence = "newly-observed-cumulative-counter-no-baseline",
                ActionConfirmed = false,
                LocalPlayerIdentityConfirmed = false
            }, generation, value.SourceRevision);
        if (counterIncrements.Count > 0)
            emit("game.counter", new
            {
                Increments = counterIncrements,
                Evidence = "observed-current-view-counter-delta",
                ActionConfirmed = false,
                LocalPlayerIdentityConfirmed = false
            }, generation, value.SourceRevision);
        _previous = current;
    }

    internal void Reset()
    {
        Fence("explicit-reset", null, null);
    }

    private void EmitRecipe(Transition transition)
    {
        var matches = _recipeCorrelation.Observe(transition.Added, transition.Removed);
        if (matches.Length == 0) return;
        if (matches.Length > 1)
        {
            emit("game.ambiguity", new
            {
                Kind = "craft",
                CandidateRecipeIds = matches.Select(match => match.Rule.Id).ToArray(),
                transition.Added,
                transition.Removed,
                Evidence = "multiple-exact-active-recipes",
                ActionConfirmed = false
            }, transition.Generation, transition.Revision);
            return;
        }

        var match = matches[0];
        var recipe = match.Rule;
        emit("game.craft", new
        {
            RecipeId = recipe.Id,
            recipe.OutputRawcode,
            OutputCount = recipe.OutputCount * match.Count,
            Count = match.Count,
            Consumed = recipe.Ingredients.ToImmutableDictionary(
                pair => pair.Key, pair => pair.Value * match.Count, StringComparer.Ordinal),
            UnobservedRequirements = recipe.OtherRequirements,
            recipe.SourceLine,
            match.Correlation,
            Evidence = $"exact-observed-rawcode-delta+pinned-{rules.MapVersion}-active-recipe",
            ActionConfirmed = false,
            LocalPlayerIdentityConfirmed = false
        }, transition.Generation, transition.Revision);
    }

    private void EmitWisps(Transition transition)
    {
        foreach (var rule in rules.Wisps)
        {
            var rawCount = transition.Removed.GetValueOrDefault(rule.Rawcode);
            var counterCount = rule.Counter is { } counter &&
                transition.CounterDeltas.TryGetValue(counter, out var observedIncrement)
                    ? observedIncrement
                    : (int?)null;
            var candidates = transition.Added
                .Where(pair => rule.Outputs.Contains(pair.Key))
                .ToImmutableDictionary(StringComparer.Ordinal);
            var positiveCounterNames = transition.CounterDeltas
                .Where(pair => pair.Value > 0)
                .Select(pair => pair.Key)
                .ToArray();
            var unchanged = transition.Added.Count == 0 &&
                transition.Removed.Count == 0 &&
                positiveCounterNames.Length == 0 &&
                !transition.CounterCoverageChanged;
            var compatible = !transition.CounterCoverageChanged &&
                transition.Added.Keys.All(rule.Outputs.Contains) &&
                transition.Removed.Keys.All(name => name == rule.Rawcode) &&
                positiveCounterNames.All(name => name == rule.Counter);
            var correlation = _wispCorrelation.Observe(new(
                rule.Id, rawCount, counterCount, transition.ObservedAt, unchanged, compatible));
            if (correlation.Evidence is { } update)
                emit("game.wisp-evidence", new
                {
                    WispId = rule.Id,
                    update.Correlated,
                    update.ArrivalOrder,
                    update.MatchedCount,
                    update.Reason,
                    update.UnmatchedRawCount,
                    update.UnmatchedCounterCount,
                    CandidateOutputs = candidates,
                    CountsAsAction = false,
                    ActionConfirmed = false
                }, transition.Generation, transition.Revision);

            rawCount = correlation.ActionRawCount;
            counterCount = correlation.ActionCounterCount;
            if (rawCount <= 0 && counterCount is not > 0) continue;

            emit("game.wisp", new
            {
                WispId = rule.Id,
                rule.Rawcode,
                rule.Kind,
                Count = Math.Max(rawCount, counterCount ?? 0),
                RawConsumptionCount = rawCount,
                Counter = rule.Counter,
                CounterIncrement = counterCount,
                CandidateOutputs = candidates,
                Evidence = rawCount > 0 && counterCount is > 0
                    ? "raw-consumption+counter-delta+compatible-output"
                    : rawCount > 0 ? "raw-consumption+compatible-output"
                    : "counter-delta+compatible-output",
                EvidenceConsistent = rawCount <= 0 || counterCount is not > 0 || rawCount == counterCount,
                ActionConfirmed = false,
                OutputsAttributed = false,
                LocalPlayerIdentityConfirmed = false
            }, transition.Generation, transition.Revision);
        }
    }

    private void EmitGambles(Transition transition)
    {
        foreach (var observation in _gambleInterpretation.Observe(
                     transition.CounterDeltas, transition.Added))
        {
            emit("game.gamble", new
            {
                GambleId = observation.Rule.Id,
                observation.Attempts,
                observation.Successes,
                observation.Failures,
                observation.OutcomeComplete,
                observation.MissingCounterNames,
                observation.ObservedOutcomeCounterDeltas,
                observation.SuccessDerivation,
                observation.FailureDerivation,
                observation.CandidateOutputs,
                OutputsAttributed = false,
                Evidence = observation.OutcomeComplete
                    ? "complete-counter-equation+compatible-unattributed-output"
                    : observation.MissingCounterNames.Length > 0
                        ? "observed-attempt-counter+partial-outcome-counters"
                        : "observed-attempt-counter+inconsistent-outcome-counters",
                ActionConfirmed = false,
                LocalPlayerIdentityConfirmed = false
            }, transition.Generation, transition.Revision);
        }
    }

    private void Fence(string reason, long? generation, long? revision)
    {
        if (_previous is null) return;
        _previous = null;
        _recipeCorrelation.Reset();
        _wispCorrelation.Reset();
        emit("game.counter-reset", new
        {
            Reason = reason,
            CorrelationReset = true,
            ActionConfirmed = false
        }, generation, revision);
    }

    private static ImmutableDictionary<string, int> InventoryDifference(
        IReadOnlyDictionary<string, int> before, IReadOnlyDictionary<string, int> after) =>
        after.Where(pair => pair.Value > before.GetValueOrDefault(pair.Key))
            .ToImmutableDictionary(pair => pair.Key,
                pair => pair.Value - before.GetValueOrDefault(pair.Key), StringComparer.Ordinal);

    private static ImmutableDictionary<string, int> CounterDeltas(
        IReadOnlyDictionary<string, int> before, IReadOnlyDictionary<string, int> after) =>
        after.Where(pair => before.TryGetValue(pair.Key, out var oldValue) && pair.Value >= oldValue)
            .ToImmutableDictionary(pair => pair.Key,
                pair => pair.Value - before[pair.Key], StringComparer.Ordinal);

    private sealed record Observation(string Context, long Revision, DateTimeOffset CompletedAt,
        ImmutableDictionary<string, int> Rawcodes, ImmutableDictionary<string, int> Counters);
    private sealed record Transition(
        ImmutableDictionary<string, int> Added,
        ImmutableDictionary<string, int> Removed,
        ImmutableDictionary<string, int> CounterDeltas,
        bool CounterCoverageChanged,
        DateTimeOffset ObservedAt,
        long Generation,
        long Revision);
}
