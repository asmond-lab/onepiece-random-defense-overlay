using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace OrandOverlay;

public enum PlannerPhase { AwaitFirstRare, AccumulateSpecialUncommon, ChooseLegend, AwaitMarineford, SpendRares, CommitRound20, Committed, ManualOverride }
public enum ReasonCode { Ready, UnknownInput, ArithmeticLimitExceeded, NoSafeRecommendation, StaleFingerprint, ManualOverride }

public enum DamageLane { Physical, Magic, Unknown }

public readonly record struct ManualLatches
{
    public bool GoalOverride { get; }
    public bool NavigationOverride { get; }
    public ManualLatches(bool goalOverride, bool navigationOverride) =>
        (GoalOverride, NavigationOverride) = (goalOverride, navigationOverride);
    public static ManualLatches None => new(false, false);
}

public sealed record StorySignal
{
    public int ActiveStage { get; }
    public ImmutableArray<string> CompletedMilestones { get; }
    public bool IsConfirmed { get; }
    public StorySignal(int activeStage, ImmutableArray<string> completedMilestones, bool isConfirmed)
    {
        if (activeStage is < 0 or > 14) throw new ArgumentOutOfRangeException(nameof(activeStage));
        ActiveStage = activeStage;
        CompletedMilestones = completedMilestones.IsDefault ? ImmutableArray<string>.Empty : completedMilestones;
        IsConfirmed = isConfirmed;
    }
}

[JsonConverter(typeof(PlanningValueJsonConverter))]
public readonly record struct PlanningValue
{
    public string Name { get; }
    public bool IsKnown { get; }
    public long Value { get; }

    private PlanningValue(string name, bool isKnown, long value)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A semantic name is required.", nameof(name));
        Name = name;
        IsKnown = isKnown;
        Value = value;
    }

    public static PlanningValue Known(string name, long value) => new(name, true, value);
    public static PlanningValue Unknown(string name) => new(name, false, 0);
    public string Serialize() => IsKnown ? $"known:{Name}:{Value}" : $"unknown:{Name}";
}

public sealed record AdaptivePlanningInput
{
    public long MatchGeneration { get; }
    public int Round { get; }
    public PlannerPhase Phase { get; }
    public StorySignal Story { get; }
    public ImmutableArray<PlanningValue> Values { get; }
    public ManualLatches ManualLatches { get; }
    public string ProfileHash { get; }
    public string DataHash { get; }

    public AdaptivePlanningInput(long matchGeneration, int round, PlannerPhase phase,
        StorySignal story, ImmutableArray<PlanningValue> values, ManualLatches manualLatches,
        string profileHash, string dataHash)
    {
        if (matchGeneration < 0) throw new ArgumentOutOfRangeException(nameof(matchGeneration));
        if (round is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(round));
        Story = story ?? throw new ArgumentNullException(nameof(story));
        if (string.IsNullOrWhiteSpace(profileHash)) throw new ArgumentException("A profile hash is required.", nameof(profileHash));
        if (string.IsNullOrWhiteSpace(dataHash)) throw new ArgumentException("A data hash is required.", nameof(dataHash));
        MatchGeneration = matchGeneration;
        Round = round;
        Phase = phase;
        Values = values.IsDefault ? ImmutableArray<PlanningValue>.Empty : values;
        ManualLatches = manualLatches;
        ProfileHash = profileHash;
        DataHash = dataHash;
    }

    public ImmutableArray<byte> CanonicalBytes
    {
        get
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(MatchGeneration);
            writer.Write(Round);
            writer.Write((int)Phase);
            writer.Write(Story.ActiveStage);
            writer.Write(Story.IsConfirmed);
            writer.Write(Story.CompletedMilestones.Length);
            foreach (var milestone in Story.CompletedMilestones) WriteString(writer, milestone);
            writer.Write(Values.Length);
            foreach (var value in Values)
            {
                writer.Write(value.IsKnown);
                WriteString(writer, value.Name);
                if (value.IsKnown) writer.Write(value.Value);
            }
            writer.Write(ManualLatches.GoalOverride);
            writer.Write(ManualLatches.NavigationOverride);
            WriteString(writer, ProfileHash);
            WriteString(writer, DataHash);
            writer.Flush();
            return ImmutableArray.Create(stream.ToArray());
        }
    }

    public string CanonicalSerialization => Convert.ToHexString(CanonicalBytes.ToArray());

    public string CanonicalFingerprint =>
        Convert.ToHexString(SHA256.HashData(CanonicalBytes.ToArray()));

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

public sealed record AdaptiveRouteResult
{
    public DamageLane Lane { get; } public string GoalUnitId { get; }
    public int FinalRouteBp { get; } public int ConfidenceBp { get; }
    public ImmutableArray<string> PackageUnitIds { get; }
    public ImmutableArray<ReasonCode> Reasons { get; }

    public AdaptiveRouteResult(DamageLane lane, string goalUnitId, int finalRouteBp,
        int confidenceBp, ImmutableArray<string> packageUnitIds, ImmutableArray<ReasonCode> reasons)
    {
        if (finalRouteBp is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(finalRouteBp));
        if (confidenceBp is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(confidenceBp));
        if (string.IsNullOrWhiteSpace(goalUnitId)) throw new ArgumentException("A goal is required.", nameof(goalUnitId));
        Lane = lane; GoalUnitId = goalUnitId; FinalRouteBp = finalRouteBp; ConfidenceBp = confidenceBp;
        PackageUnitIds = packageUnitIds.IsDefault ? ImmutableArray<string>.Empty : packageUnitIds;
        Reasons = reasons.IsDefault ? ImmutableArray<ReasonCode>.Empty : reasons;
    }
}

public sealed record NavigationStateSnapshot
{
    public long MatchGeneration { get; }
    public long RecognitionRevision { get; }
    public RuntimeRecommendationSnapshotState State { get; }
    public bool IsPlannerReady { get; }
    public ImmutableArray<RuntimeRecommendationField> Fields { get; }
    public ImmutableArray<PlanningValue> Values { get; }

    public NavigationStateSnapshot(long matchGeneration, long recognitionRevision,
        RuntimeRecommendationSnapshotState state, bool isPlannerReady,
        ImmutableArray<RuntimeRecommendationField> fields)
    {
        if (matchGeneration < 0) throw new ArgumentOutOfRangeException(nameof(matchGeneration));
        if (recognitionRevision < 0) throw new ArgumentOutOfRangeException(nameof(recognitionRevision));
        MatchGeneration = matchGeneration;
        RecognitionRevision = recognitionRevision;
        State = state;
        IsPlannerReady = isPlannerReady;
        Fields = fields.IsDefault ? ImmutableArray<RuntimeRecommendationField>.Empty : fields;
        Values = Fields.Select(field => field.Value).ToImmutableArray();
    }

    public RuntimeRecommendationField Field(string name) => Fields.Single(field =>
        field.Value.Name.Equals(name, StringComparison.Ordinal));
}

public readonly record struct RationalInterval
{
    public Rational Lower { get; }
    public Rational Upper { get; }

    public RationalInterval(Rational lower, Rational upper)
    {
        if (lower.Numerator * upper.Denominator >
            upper.Numerator * lower.Denominator)
            throw new ArgumentException("Lower bound must not exceed upper bound.");
        Lower = lower;
        Upper = upper;
    }

    public bool Contains(Rational value) => Lower.Numerator * value.Denominator <= value.Numerator * Lower.Denominator &&
        value.Numerator * Upper.Denominator <= Upper.Numerator * value.Denominator;
}

public readonly record struct SignedRouteDelta
{
    public int Value { get; }

    public SignedRouteDelta(int value)
    {
        if (value is < -10000 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }
}

public sealed record NavigationOutcomeDistribution
{
    public ImmutableArray<Rational> Probabilities { get; } public RationalInterval CoreFloor { get; }
    public RationalInterval ExpectedUplift { get; } public RationalInterval UpperTail { get; }
    public int ConfidenceBp { get; } public ImmutableArray<ReasonCode> Reasons { get; }

    public NavigationOutcomeDistribution(ImmutableArray<Rational> probabilities,
        RationalInterval coreFloor, RationalInterval expectedUplift,
        RationalInterval upperTail, int confidenceBp, ImmutableArray<ReasonCode> reasons)
    {
        Probabilities = probabilities.IsDefault
            ? ImmutableArray<Rational>.Empty
            : probabilities;
        CoreFloor = coreFloor; ExpectedUplift = expectedUplift; UpperTail = upperTail;
        Reasons = reasons.IsDefault ? ImmutableArray<ReasonCode>.Empty : reasons;
        if (confidenceBp is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(confidenceBp));
        ConfidenceBp = confidenceBp;
    }
}

public sealed record AdaptivePlanningResult
{
    public string InputFingerprint { get; } public PlannerPhase Phase { get; }
    public AdaptiveRouteResult? Route { get; } public NavigationOutcomeDistribution? Navigation { get; }
    public ImmutableArray<ReasonCode> Reasons { get; }

    public AdaptivePlanningResult(string inputFingerprint, PlannerPhase phase,
        AdaptiveRouteResult? route, NavigationOutcomeDistribution? navigation,
        ImmutableArray<ReasonCode> reasons)
    {
        InputFingerprint = inputFingerprint;
        Phase = phase;
        Route = route;
        Navigation = navigation;
        Reasons = reasons.IsDefault ? ImmutableArray<ReasonCode>.Empty : reasons;
    }
}

public sealed record AdaptiveDecisionTrace
{
    public long MatchGeneration { get; } public string InputFingerprint { get; }
    public string DecisionFingerprint { get; } public PlannerPhase Phase { get; }
    public ImmutableArray<ReasonCode> Reasons { get; }

    public AdaptiveDecisionTrace(long matchGeneration, string inputFingerprint,
        string decisionFingerprint, PlannerPhase phase, ImmutableArray<ReasonCode> reasons)
    {
        MatchGeneration = matchGeneration;
        InputFingerprint = inputFingerprint;
        DecisionFingerprint = decisionFingerprint;
        Phase = phase;
        Reasons = reasons.IsDefault ? ImmutableArray<ReasonCode>.Empty : reasons;
    }
}

public sealed class PlanningValueJsonConverter : JsonConverter<PlanningValue>
{
    public override PlanningValue Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty("name", out var name) ||
            !root.TryGetProperty("isKnown", out var known))
            throw new JsonException("PlanningValue requires name and isKnown.");
        var semanticName = name.GetString();
        if (string.IsNullOrWhiteSpace(semanticName))
            throw new JsonException("PlanningValue name is required.");
        if (!known.GetBoolean())
        {
            if (root.TryGetProperty("value", out _))
                throw new JsonException("Unknown PlanningValue cannot carry a value.");
            return PlanningValue.Unknown(semanticName);
        }
        if (!root.TryGetProperty("value", out var value) ||
            !value.TryGetInt64(out var number))
            throw new JsonException("Known PlanningValue requires an integer value.");
        return PlanningValue.Known(semanticName, number);
    }

    public override void Write(Utf8JsonWriter writer, PlanningValue value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        writer.WriteBoolean("isKnown", value.IsKnown);
        if (value.IsKnown) writer.WriteNumber("value", value.Value);
        writer.WriteEndObject();
    }
}

public static class AdaptivePlanningJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
        options.Converters.Add(new RationalJsonConverter()); options.Converters.Add(new SignedRouteDeltaJsonConverter()); return options;
    }
}

public sealed class RationalJsonConverter : JsonConverter<Rational>
{
    public override Rational Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader); var root = document.RootElement;
        if (!root.TryGetProperty("numerator", out var n) || !root.TryGetProperty("denominator", out var d)) throw new JsonException("Rational requires numerator and denominator.");
        if (!BigInteger.TryParse(Text(n), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator) || !BigInteger.TryParse(Text(d), NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator)) throw new JsonException("Rational bounds must be integers.");
        try { return new Rational(numerator, denominator); } catch (ArgumentOutOfRangeException exception) { throw new JsonException("Invalid Rational bounds.", exception); }
    }
    public override void Write(Utf8JsonWriter writer, Rational value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteString("numerator", value.Numerator.ToString(CultureInfo.InvariantCulture)); writer.WriteString("denominator", value.Denominator.ToString(CultureInfo.InvariantCulture)); writer.WriteEndObject();
    }
    private static string Text(JsonElement element) => element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
}

public sealed class SignedRouteDeltaJsonConverter : JsonConverter<SignedRouteDelta>
{
    public override SignedRouteDelta Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (!reader.TryGetInt32(out var value)) throw new JsonException("SignedRouteDelta must be an integer.");
        try { return new SignedRouteDelta(value); } catch (ArgumentOutOfRangeException exception) { throw new JsonException("SignedRouteDelta is out of range.", exception); }
    }
    public override void Write(Utf8JsonWriter writer, SignedRouteDelta value, JsonSerializerOptions options) => writer.WriteNumberValue(value.Value);
}
