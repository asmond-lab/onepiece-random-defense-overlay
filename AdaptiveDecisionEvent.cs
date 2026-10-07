using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

public enum AdaptiveNavigationRegime { None, SecureCore, GuaranteedRecovery, DesperationRecovery }

public readonly record struct AdaptiveDecisionInterval
{
    public int LowerBp { get; }
    public int UpperBp { get; }

    public AdaptiveDecisionInterval(int lowerBp, int upperBp)
    {
        if (lowerBp is < -10000 or > 10000) throw new ArgumentOutOfRangeException(nameof(lowerBp));
        if (upperBp is < -10000 or > 10000 || lowerBp > upperBp) throw new ArgumentOutOfRangeException(nameof(upperBp));
        LowerBp = lowerBp;
        UpperBp = upperBp;
    }
}

public sealed record AdaptiveRouteComponent
{
    public string CandidateId { get; }
    public DamageLane Lane { get; }
    public int GoalProgressBp { get; }
    public int PackageProgressBp { get; }
    public int RouteScoreBp { get; }

    public AdaptiveRouteComponent(string candidateId, DamageLane lane, int goalProgressBp,
        int packageProgressBp, int routeScoreBp)
    {
        CandidateId = Required(candidateId, nameof(candidateId));
        if (goalProgressBp is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(goalProgressBp));
        if (packageProgressBp is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(packageProgressBp));
        if (routeScoreBp is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(routeScoreBp));
        Lane = lane;
        GoalProgressBp = goalProgressBp;
        PackageProgressBp = packageProgressBp;
        RouteScoreBp = routeScoreBp;
    }

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A machine identifier is required.", name) : value;
}

public sealed record AdaptiveNavigationScenario
{
    public string CandidateId { get; }
    public int CoreCompletionProbabilityBp { get; }
    public AdaptiveDecisionInterval ExpectedUpliftBp { get; }
    public AdaptiveDecisionInterval UpperTailUpliftBp { get; }

    public AdaptiveNavigationScenario(string candidateId, int coreCompletionProbabilityBp,
        AdaptiveDecisionInterval expectedUpliftBp, AdaptiveDecisionInterval upperTailUpliftBp)
    {
        if (string.IsNullOrWhiteSpace(candidateId)) throw new ArgumentException("A machine identifier is required.", nameof(candidateId));
        if (coreCompletionProbabilityBp is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(coreCompletionProbabilityBp));
        CandidateId = candidateId;
        CoreCompletionProbabilityBp = coreCompletionProbabilityBp;
        ExpectedUpliftBp = expectedUpliftBp;
        UpperTailUpliftBp = upperTailUpliftBp;
    }
}

public sealed record AdaptiveDecisionEventInput
{
    public string ScoringVersion { get; }
    public string ProfileDataGeneration { get; }
    public string MapDataGeneration { get; }
    public long MatchGeneration { get; }
    public string InputFingerprint { get; }
    public PlannerPhase Phase { get; }
    public ImmutableArray<ReasonCode> Blockers { get; }
    public ImmutableArray<AdaptiveRouteComponent> RouteComponents { get; }
    public ImmutableArray<string> CandidateIds { get; }
    public int ConfidenceBp { get; }
    public bool IsFallback { get; }
    public ImmutableArray<AdaptiveNavigationScenario> NavigationScenarios { get; }
    public AdaptiveNavigationRegime NavigationRegime { get; }
    public bool OverlayRecommendationLocked { get; }
    public ManualLatches ManualLatches { get; }
    public string? SourceDefinedForcedExpectationId { get; }

    public AdaptiveDecisionEventInput(string scoringVersion, string profileDataGeneration,
        string mapDataGeneration, long matchGeneration, string inputFingerprint, PlannerPhase phase,
        ImmutableArray<ReasonCode> blockers, ImmutableArray<AdaptiveRouteComponent> routeComponents,
        ImmutableArray<string> candidateIds, int confidenceBp, bool isFallback,
        ImmutableArray<AdaptiveNavigationScenario> navigationScenarios,
        AdaptiveNavigationRegime navigationRegime, bool overlayRecommendationLocked,
        ManualLatches manualLatches, string? sourceDefinedForcedExpectationId)
    {
        if (matchGeneration < 0) throw new ArgumentOutOfRangeException(nameof(matchGeneration));
        if (confidenceBp is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(confidenceBp));
        ScoringVersion = Required(scoringVersion, nameof(scoringVersion));
        ProfileDataGeneration = Required(profileDataGeneration, nameof(profileDataGeneration));
        MapDataGeneration = Required(mapDataGeneration, nameof(mapDataGeneration));
        InputFingerprint = Required(inputFingerprint, nameof(inputFingerprint));
        MatchGeneration = matchGeneration;
        Phase = phase;
        Blockers = Normalize(blockers);
        RouteComponents = Normalize(routeComponents, item => $"{item.CandidateId}\u001f{(int)item.Lane:D10}");
        CandidateIds = Normalize(candidateIds, item => Required(item));
        ConfidenceBp = confidenceBp;
        IsFallback = isFallback;
        NavigationScenarios = Normalize(navigationScenarios, item => item.CandidateId);
        NavigationRegime = navigationRegime;
        OverlayRecommendationLocked = overlayRecommendationLocked;
        ManualLatches = manualLatches;
        SourceDefinedForcedExpectationId = string.IsNullOrWhiteSpace(sourceDefinedForcedExpectationId)
            ? null : sourceDefinedForcedExpectationId;
    }

    private static string Required(string value, string name = "value") => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A machine identifier is required.", name) : value;

    private static ImmutableArray<T> Normalize<T>(ImmutableArray<T> values) where T : struct, Enum =>
        (values.IsDefault ? ImmutableArray<T>.Empty : values).Distinct().Order().ToImmutableArray();

    private static ImmutableArray<T> Normalize<T>(ImmutableArray<T> values, Func<T, string> key) =>
        (values.IsDefault ? ImmutableArray<T>.Empty : values).OrderBy(key, StringComparer.Ordinal).ToImmutableArray();

    private static ImmutableArray<string> Normalize(ImmutableArray<string> values, Func<string, string> required) =>
        (values.IsDefault ? ImmutableArray<string>.Empty : values).Select(required).Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal).ToImmutableArray();
}

public sealed record AdaptiveDecisionEvent
{
    public string ScoringVersion { get; }
    public string ProfileDataGeneration { get; }
    public string MapDataGeneration { get; }
    public long MatchGeneration { get; }
    public string InputFingerprint { get; }
    public string DecisionFingerprint { get; }
    public PlannerPhase Phase { get; }
    public ImmutableArray<ReasonCode> Blockers { get; }
    public ImmutableArray<AdaptiveRouteComponent> RouteComponents { get; }
    public ImmutableArray<string> CandidateIds { get; }
    public int ConfidenceBp { get; }
    public bool IsFallback { get; }
    public ImmutableArray<AdaptiveNavigationScenario> NavigationScenarios { get; }
    public AdaptiveNavigationRegime NavigationRegime { get; }
    public bool OverlayRecommendationLocked { get; }
    public ManualLatches ManualLatches { get; }
    public string? SourceDefinedForcedExpectationId { get; }

    [JsonIgnore] public ImmutableArray<byte> CanonicalBytes { get; }
    [JsonIgnore] public ImmutableArray<byte> SerializedBytes => ImmutableArray.Create(JsonSerializer.SerializeToUtf8Bytes(this));

    public AdaptiveDecisionEvent(AdaptiveDecisionEventInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        ScoringVersion = input.ScoringVersion;
        ProfileDataGeneration = input.ProfileDataGeneration;
        MapDataGeneration = input.MapDataGeneration;
        MatchGeneration = input.MatchGeneration;
        InputFingerprint = input.InputFingerprint;
        Phase = input.Phase;
        Blockers = input.Blockers;
        RouteComponents = input.RouteComponents;
        CandidateIds = input.CandidateIds;
        ConfidenceBp = input.ConfidenceBp;
        IsFallback = input.IsFallback;
        NavigationScenarios = input.NavigationScenarios;
        NavigationRegime = input.NavigationRegime;
        OverlayRecommendationLocked = input.OverlayRecommendationLocked;
        ManualLatches = input.ManualLatches;
        SourceDefinedForcedExpectationId = input.SourceDefinedForcedExpectationId;
        CanonicalBytes = WriteCanonicalBytes();
        DecisionFingerprint = Convert.ToHexString(SHA256.HashData(CanonicalBytes.AsSpan()));
    }

    private ImmutableArray<byte> WriteCanonicalBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        Write(writer, ScoringVersion); Write(writer, ProfileDataGeneration); Write(writer, MapDataGeneration);
        writer.Write(MatchGeneration); Write(writer, InputFingerprint); writer.Write((int)Phase);
        writer.Write(Blockers.Length); foreach (var blocker in Blockers) writer.Write((int)blocker);
        writer.Write(RouteComponents.Length); foreach (var route in RouteComponents) { Write(writer, route.CandidateId); writer.Write((int)route.Lane); writer.Write(route.GoalProgressBp); writer.Write(route.PackageProgressBp); writer.Write(route.RouteScoreBp); }
        writer.Write(CandidateIds.Length); foreach (var candidate in CandidateIds) Write(writer, candidate);
        writer.Write(ConfidenceBp); writer.Write(IsFallback);
        writer.Write(NavigationScenarios.Length); foreach (var scenario in NavigationScenarios) { Write(writer, scenario.CandidateId); writer.Write(scenario.CoreCompletionProbabilityBp); Write(writer, scenario.ExpectedUpliftBp); Write(writer, scenario.UpperTailUpliftBp); }
        writer.Write((int)NavigationRegime); writer.Write(OverlayRecommendationLocked);
        writer.Write(ManualLatches.GoalOverride); writer.Write(ManualLatches.NavigationOverride);
        writer.Write(SourceDefinedForcedExpectationId is not null); if (SourceDefinedForcedExpectationId is not null) Write(writer, SourceDefinedForcedExpectationId);
        writer.Flush();
        return ImmutableArray.Create(stream.ToArray());
    }

    private static void Write(BinaryWriter writer, string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
    private static void Write(BinaryWriter writer, AdaptiveDecisionInterval value) { writer.Write(value.LowerBp); writer.Write(value.UpperBp); }
}
