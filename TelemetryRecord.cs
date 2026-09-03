using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OrandOverlay;

/// <summary>식별자와 사건 시각·순서를 포함하지 않는 매치 단위 익명 집계(v2).</summary>
public sealed class TelemetryRecord
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 2;
    [JsonPropertyName("appVersion")] public string AppVersion { get; init; } = "unknown";
    [JsonPropertyName("mapVersion")] public string MapVersion { get; init; } = "unknown";
    [JsonPropertyName("difficulty")] public string Difficulty { get; init; } = "unknown";
    [JsonPropertyName("damageLane")] public string DamageLane { get; init; } = "unknown";
    [JsonPropertyName("goalTierFamily")] public string GoalTierFamily { get; init; } = "unknown";
    [JsonPropertyName("surface")] public string Surface { get; init; } = "unknown";
    [JsonPropertyName("urgency")] public string Urgency { get; init; } = "none";
    [JsonPropertyName("outcome")] public string Outcome { get; init; } = "unknown";
    [JsonPropertyName("matchCount")] public int MatchCount { get; init; } = 1;
    [JsonPropertyName("observedObjects")] public string ObservedObjects { get; init; } = "0";
    [JsonPropertyName("completedTops")] public string CompletedTops { get; init; } = "0";
    [JsonPropertyName("readyScans")] public string ReadyScans { get; init; } = "0";
    [JsonPropertyName("waitingScans")] public string WaitingScans { get; init; } = "0";
    [JsonPropertyName("transientScans")] public string TransientScans { get; init; } = "0";
    [JsonPropertyName("unsupportedScans")] public string UnsupportedScans { get; init; } = "0";
}

public readonly record struct RecognitionTelemetryCounts(
    int Ready, int Waiting, int Transient, int Unsupported)
{
    public RecognitionTelemetryCounts Observe(RecognitionState state) => state switch
    {
        RecognitionState.Ready => this with { Ready = Ready + 1 },
        RecognitionState.Waiting => this with { Waiting = Waiting + 1 },
        RecognitionState.TransientReadError => this with { Transient = Transient + 1 },
        _ => this with { Unsupported = Unsupported + 1 }
    };
}

public static class MatchTelemetryRecorder
{
    private static readonly HashSet<string> TierFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "전설", "히든", "변화된", "랜덤전용", "제한", "초월", "불멸", "영원"
    };

    public static TelemetryRecord Build(
        string appVersion,
        string mapVersion,
        string difficulty,
        DamageLane damageLane,
        string goalTier,
        RecommendationSurface surface,
        RecommendationUrgency urgency,
        int observedUnitCount,
        int completedTopCount,
        RecognitionTelemetryCounts recognition,
        string outcome) => new()
    {
        AppVersion = TelemetryVocabulary.Version(appVersion),
        MapVersion = TelemetryVocabulary.Version(mapVersion),
        Difficulty = TelemetryVocabulary.Difficulty(difficulty),
        DamageLane = damageLane switch
        {
            DamageLane.Physical => "physical",
            DamageLane.Magic => "magic",
            _ => "unknown"
        },
        GoalTierFamily = TierFamily(goalTier),
        Surface = surface switch
        {
            RecommendationSurface.FastRare => "fast-rare",
            RecommendationSurface.StoryLegend => "story-legend",
            RecommendationSurface.TopAndNavigation => "top-navigation",
            _ => "unknown"
        },
        Urgency = urgency switch
        {
            RecommendationUrgency.BossSurvival => "boss-survival",
            RecommendationUrgency.StoryDeadline => "story-deadline",
            _ => "none"
        },
        Outcome = TelemetryVocabulary.Outcome(outcome),
        ObservedObjects = TelemetryVocabulary.CountBucket(observedUnitCount),
        CompletedTops = TelemetryVocabulary.CountBucket(completedTopCount),
        ReadyScans = TelemetryVocabulary.CountBucket(recognition.Ready),
        WaitingScans = TelemetryVocabulary.CountBucket(recognition.Waiting),
        TransientScans = TelemetryVocabulary.CountBucket(recognition.Transient),
        UnsupportedScans = TelemetryVocabulary.CountBucket(recognition.Unsupported)
    };

    private static string TierFamily(string? tier)
    {
        var family = (tier ?? "").Split('[', 2)[0].Trim();
        return TierFamilies.Contains(family) ? family : "unknown";
    }
}

public static partial class TelemetryVocabulary
{
    [GeneratedRegex("^[0-9]+(?:\\.[0-9]+){0,3}(?:-[A-Za-z0-9.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public static string Version(string? value) =>
        value is { Length: > 0 and <= 32 } && VersionPattern().IsMatch(value)
            ? value
            : "unknown";

    public static string Difficulty(string? value) => value?.Trim() switch
    {
        "쉬움" or "easy" => "easy",
        "보통" or "normal" => "normal",
        "어려움" or "hard" => "hard",
        "악몽" or "nightmare" => "nightmare",
        "지옥" or "hell" => "hell",
        "신" or "god" => "god",
        "신+" or "god+" => "god-plus",
        _ => "unknown"
    };

    public static string Outcome(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "clear" => "clear",
        "fail" => "fail",
        _ => "unknown"
    };

    public static string CountBucket(int value) => value switch
    {
        <= 0 => "0",
        1 => "1",
        <= 4 => "2-4",
        <= 9 => "5-9",
        <= 24 => "10-24",
        _ => "25+"
    };
}

public static partial class TelemetryPrivacyContract
{
    private static readonly HashSet<string> AllowedProperties =
        new([
            "schemaVersion", "appVersion", "mapVersion", "difficulty", "damageLane",
            "goalTierFamily", "surface", "urgency", "outcome", "matchCount",
            "observedObjects", "completedTops", "readyScans", "waitingScans",
            "transientScans", "unsupportedScans"
        ], StringComparer.Ordinal);
    private static readonly HashSet<string> Difficulties =
        new(["easy", "normal", "hard", "nightmare", "hell", "god", "god-plus", "unknown"],
            StringComparer.Ordinal);
    private static readonly HashSet<string> DamageLanes =
        new(["physical", "magic", "unknown"], StringComparer.Ordinal);
    private static readonly HashSet<string> TierFamilies =
        new(["전설", "히든", "변화된", "랜덤전용", "제한", "초월", "불멸", "영원", "unknown"],
            StringComparer.Ordinal);
    private static readonly HashSet<string> Surfaces =
        new(["fast-rare", "story-legend", "top-navigation", "unknown"],
            StringComparer.Ordinal);
    private static readonly HashSet<string> Urgencies =
        new(["none", "boss-survival", "story-deadline"], StringComparer.Ordinal);
    private static readonly HashSet<string> Outcomes =
        new(["clear", "fail", "unknown"], StringComparer.Ordinal);
    private static readonly HashSet<string> CountBuckets =
        new(["0", "1", "2-4", "5-9", "10-24", "25+"], StringComparer.Ordinal);

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex("(?:rawcode:|[12][0-9]{3}-[01][0-9]-[0-3][0-9]T)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveValuePattern();

    public static bool IsSafe(TelemetryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return IsSafeJson(JsonSerializer.Serialize(record));
    }

    public static bool IsSafeJson(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Length != AllowedProperties.Count ||
                properties.Select(property => property.Name)
                    .Distinct(StringComparer.Ordinal).Count() != AllowedProperties.Count ||
                properties.Any(property => !AllowedProperties.Contains(property.Name)))
                return false;
            var record = JsonSerializer.Deserialize<TelemetryRecord>(payload);
            if (record is null || record.SchemaVersion != 2 || record.MatchCount != 1 ||
                TelemetryVocabulary.Version(record.AppVersion) != record.AppVersion ||
                TelemetryVocabulary.Version(record.MapVersion) != record.MapVersion ||
                !Difficulties.Contains(record.Difficulty) ||
                !DamageLanes.Contains(record.DamageLane) ||
                !TierFamilies.Contains(record.GoalTierFamily) ||
                !Surfaces.Contains(record.Surface) ||
                !Urgencies.Contains(record.Urgency) ||
                !Outcomes.Contains(record.Outcome) ||
                !BucketsAreValid(record))
                return false;
            return properties.Where(property => property.Value.ValueKind == JsonValueKind.String)
                .Select(property => property.Value.GetString() ?? "")
                .All(value => !GuidPattern().IsMatch(value) &&
                              !SensitiveValuePattern().IsMatch(value));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool BucketsAreValid(TelemetryRecord record) =>
        CountBuckets.Contains(record.ObservedObjects) &&
        CountBuckets.Contains(record.CompletedTops) &&
        CountBuckets.Contains(record.ReadyScans) &&
        CountBuckets.Contains(record.WaitingScans) &&
        CountBuckets.Contains(record.TransientScans) &&
        CountBuckets.Contains(record.UnsupportedScans);
}
