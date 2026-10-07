using System.Collections.Immutable;

namespace OrandOverlay;

public enum RuntimeRecommendationSnapshotState
{
    Current,
    Stale,
    InvalidSource,
    TransientWithoutLastGood
}

public sealed record RuntimeRecommendationField
{
    public PlanningValue Value { get; }
    public int ConfidenceBp { get; }
    public ImmutableArray<string> FiniteScenarios { get; }

    public RuntimeRecommendationField(PlanningValue value, int confidenceBp,
        ImmutableArray<string> finiteScenarios)
    {
        if (confidenceBp is < 0 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(confidenceBp));
        Value = value;
        ConfidenceBp = confidenceBp;
        FiniteScenarios = finiteScenarios.IsDefault
            ? ImmutableArray<string>.Empty
            : finiteScenarios;
    }
}

public readonly record struct RuntimeRecommendationObservation(
    string SignalIdentifier,
    string FieldName,
    long? Value,
    int ConfidenceBp);

public sealed record RuntimeRecommendationInputFrame
{
    public long MatchGeneration { get; }
    public long RecognitionRevision { get; }
    public RecognitionState RecognitionState { get; }
    public RuntimeMapIdentityResult MapIdentity { get; }
    public ImmutableArray<RuntimeRecommendationObservation> Observations { get; }

    public RuntimeRecommendationInputFrame(long matchGeneration, long recognitionRevision,
        RecognitionState recognitionState, RuntimeMapIdentityResult mapIdentity,
        ImmutableArray<RuntimeRecommendationObservation> observations)
    {
        if (matchGeneration < 0) throw new ArgumentOutOfRangeException(nameof(matchGeneration));
        if (recognitionRevision < 0) throw new ArgumentOutOfRangeException(nameof(recognitionRevision));
        MatchGeneration = matchGeneration;
        RecognitionRevision = recognitionRevision;
        RecognitionState = recognitionState;
        MapIdentity = mapIdentity ?? throw new ArgumentNullException(nameof(mapIdentity));
        Observations = observations.IsDefault
            ? ImmutableArray<RuntimeRecommendationObservation>.Empty
            : observations;
    }
}

public sealed class RuntimeRecommendationInputsTracker
{
    private const int SourceBoundConfidenceBp = 8000;
    private static readonly ImmutableDictionary<string, ImmutableArray<string>> FieldsBySignal =
        new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
        {
            ["resources"] = ["gold", "lumber"],
            ["random-wisp"] = ["random-wisp-state"],
            ["helper"] = ["helper-presence", "helper-mana"],
            ["exact-top-count"] = ["exact-top-count"],
            ["isekai"] = ["isekai"],
            ["rerolls-gamble-bounty-boss-item"] =
                ["rerolls", "gamble-actions", "bounty-kills", "boss-state", "item-state"],
            ["selected-top-cooldown"] = ["selected-top-cooldown"],
            ["current-wave"] = ["current-wave"],
            ["remaining-combat-horizon"] = ["remaining-waves", "targets-within-400"]
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private readonly RuntimeSignalFeasibilityProfile _profile;
    private NavigationStateSnapshot? _lastGood;
    private long _lastMatchGeneration = -1;
    private long _lastRevision = -1;

    public RuntimeRecommendationInputsTracker(RuntimeSignalFeasibilityProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        var actual = profile.OptionalSignals.Select(signal => signal.Identifier).ToArray();
        if (actual.Length != FieldsBySignal.Count ||
            actual.Any(identifier => !FieldsBySignal.ContainsKey(identifier)))
            throw new ArgumentException("Optional runtime signals do not match the Task 5 contract.", nameof(profile));
    }

    public NavigationStateSnapshot Capture(RuntimeRecommendationInputFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.MatchGeneration != _lastMatchGeneration)
        {
            _lastGood = null;
            _lastRevision = -1;
            _lastMatchGeneration = frame.MatchGeneration;
        }

        if (RecognitionPolicy.MayUseLastGoodForRecommendations(frame.RecognitionState))
            return _lastGood ?? Empty(frame, RuntimeRecommendationSnapshotState.TransientWithoutLastGood);

        if (frame.RecognitionState != RecognitionState.Ready ||
            !RuntimeAdaptivePlanningReadiness.IsReady(_profile, frame.MapIdentity))
        {
            _lastGood = null;
            return Empty(frame, RuntimeRecommendationSnapshotState.InvalidSource);
        }

        if (frame.RecognitionRevision <= _lastRevision)
            return Empty(frame, RuntimeRecommendationSnapshotState.Stale);

        var observations = frame.Observations
            .Where(observation => !string.IsNullOrWhiteSpace(observation.SignalIdentifier) &&
                                  !string.IsNullOrWhiteSpace(observation.FieldName))
            .GroupBy(observation => (observation.SignalIdentifier, observation.FieldName))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var fields = ImmutableArray.CreateBuilder<RuntimeRecommendationField>();
        foreach (var signal in _profile.OptionalSignals)
        {
            foreach (var fieldName in FieldsBySignal[signal.Identifier])
            {
                observations.TryGetValue((signal.Identifier, fieldName), out var candidates);
                fields.Add(CreateField(signal, fieldName, candidates));
            }
        }

        var snapshot = new NavigationStateSnapshot(frame.MatchGeneration,
            frame.RecognitionRevision, RuntimeRecommendationSnapshotState.Current,
            isPlannerReady: true, fields.MoveToImmutable());
        _lastGood = snapshot;
        _lastRevision = frame.RecognitionRevision;
        return snapshot;
    }

    public void ConfirmReset(long matchGeneration)
    {
        if (matchGeneration != _lastMatchGeneration) return;
        _lastGood = null;
        _lastRevision = -1;
    }

    private static RuntimeRecommendationField CreateField(
        RuntimeSignalFeasibility signal, string name,
        RuntimeRecommendationObservation[]? candidates)
    {
        if (candidates is not { Length: 1 })
            return Unknown(name, signal.FiniteScenarios);

        var candidate = candidates[0];
        if (candidate.ConfidenceBp is not (SourceBoundConfidenceBp or 10000) ||
            candidate.Value is not long value || !IsValid(name, value))
            return Unknown(name, signal.FiniteScenarios);

        return new RuntimeRecommendationField(PlanningValue.Known(name, value),
            candidate.ConfidenceBp, signal.FiniteScenarios);
    }

    private static bool IsValid(string name, long value) => name switch
    {
        "current-wave" => value is >= 0 and <= 200,
        "exact-top-count" => value is >= 0 and <= 3,
        "random-wisp-state" => value is >= 0 and <= 2,
        "helper-presence" or "isekai" => value is 0 or 1,
        "selected-top-cooldown" => value > 0,
        _ => value is >= 0 and <= int.MaxValue
    };

    private static RuntimeRecommendationField Unknown(
        string name, ImmutableArray<string> scenarios) =>
        new(PlanningValue.Unknown(name), SourceBoundConfidenceBp, scenarios);

    private NavigationStateSnapshot Empty(RuntimeRecommendationInputFrame frame,
        RuntimeRecommendationSnapshotState state)
    {
        var fields = _profile.OptionalSignals.SelectMany(signal =>
            FieldsBySignal[signal.Identifier].Select(name => Unknown(name, signal.FiniteScenarios)))
            .ToImmutableArray();
        return new NavigationStateSnapshot(frame.MatchGeneration, frame.RecognitionRevision,
            state, isPlannerReady: false, fields);
    }
}

public enum NavigationRecommendationMode
{
    ProvisionalPreview,
    ActionableRerank,
    OverlayRecommendationLocked,
    ManualOverrideLocked,
    SourceForcedExpectation
}

public static class NavigationRecommendationPolicy
{
    public static NavigationRecommendationMode ForRound(
        int round, bool hasRobustOverlayRecommendationLock, bool manualNavigationOverride)
    {
        if (round >= 24) return NavigationRecommendationMode.SourceForcedExpectation;
        if (round <= 20) return NavigationRecommendationMode.ProvisionalPreview;
        if (manualNavigationOverride) return NavigationRecommendationMode.ManualOverrideLocked;
        return hasRobustOverlayRecommendationLock
            ? NavigationRecommendationMode.OverlayRecommendationLocked
            : NavigationRecommendationMode.ActionableRerank;
    }

    public static bool ClaimsRuntimeConfirmation(NavigationRecommendationMode mode) => false;
}
