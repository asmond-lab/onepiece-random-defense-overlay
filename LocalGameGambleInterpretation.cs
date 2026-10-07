using System.Collections.Immutable;

namespace OrandOverlay;

internal sealed record ActivityGambleObservation(
    ActivityGambleRule Rule,
    int Attempts,
    int? Successes,
    int? Failures,
    bool OutcomeComplete,
    ImmutableArray<string> MissingCounterNames,
    ImmutableDictionary<string, int> ObservedOutcomeCounterDeltas,
    string? SuccessDerivation,
    string? FailureDerivation,
    ImmutableDictionary<string, int> CandidateOutputs);

internal sealed class LocalGameGambleInterpretation(
    ImmutableArray<ActivityGambleRule> rules)
{
    internal ImmutableArray<ActivityGambleObservation> Observe(
        IReadOnlyDictionary<string, int> counterDeltas,
        IReadOnlyDictionary<string, int> added)
    {
        var observations = ImmutableArray.CreateBuilder<ActivityGambleObservation>();
        foreach (var rule in rules)
        {
            if (rule.Attempts is not { } attemptsName ||
                !counterDeltas.TryGetValue(attemptsName, out var attempts) ||
                attempts <= 0)
                continue;

            var requiredOutcomeNames = rule.Branches
                .Concat(rule.Successes is { } successesName ? [successesName] : [])
                .Concat(rule.Failures is { } failuresName ? [failuresName] : [])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var observedOutcomeDeltas = requiredOutcomeNames
                .Where(counterDeltas.ContainsKey)
                .ToImmutableDictionary(name => name,
                    name => counterDeltas[name], StringComparer.Ordinal);
            var missingCounterNames = requiredOutcomeNames
                .Where(name => !counterDeltas.ContainsKey(name))
                .Order(StringComparer.Ordinal)
                .ToImmutableArray();

            int? successes = null;
            if (rule.Successes is { } observedSuccessName)
            {
                if (counterDeltas.TryGetValue(observedSuccessName, out var observedSuccesses))
                    successes = observedSuccesses;
            }
            else if (rule.Branches.All(counterDeltas.ContainsKey))
            {
                successes = rule.Branches.Sum(branch => counterDeltas[branch]);
            }

            int? failures = null;
            if (rule.Failures is { } observedFailureName)
            {
                if (counterDeltas.TryGetValue(observedFailureName, out var observedFailures))
                    failures = observedFailures;
            }
            else if (successes.HasValue)
            {
                failures = attempts - successes;
            }

            observations.Add(new(
                rule,
                attempts,
                successes,
                failures,
                successes is >= 0 && failures is >= 0 && attempts == successes + failures,
                missingCounterNames,
                observedOutcomeDeltas,
                successes.HasValue
                    ? rule.Successes is null ? "source-equation" : "observed-counter"
                    : null,
                failures.HasValue
                    ? rule.Failures is null ? "source-equation" : "observed-counter"
                    : null,
                added.Where(pair => rule.Outputs.Contains(pair.Key))
                    .ToImmutableDictionary(StringComparer.Ordinal)));
        }
        return observations.ToImmutable();
    }
}
