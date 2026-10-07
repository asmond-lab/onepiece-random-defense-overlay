using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OrandOverlay;

public sealed record GameplayTelemetryMetadata(string AppVersion, string MapVersion,
    string MapScriptSha256, string ProfileVersion);

public sealed record GameplayTelemetryGambleCounter(int Attempts, int Successes, int Failures)
{
    [JsonIgnore] public bool IsValid => Attempts >= 0 && Successes >= 0 && Failures >= 0 && (long)Successes + Failures == Attempts;
}

public sealed record GameplayTelemetryObservation
{
    public required long MatchGeneration { get; init; }
    public required long RecognitionRevision { get; init; }
    public required int Round { get; init; }
    public int CompletedStory { get; init; }
    public PlayMode Mode { get; init; }
    public int GuideNumber { get; init; }
    public bool IsCurrent { get; init; } = true;
    public string Difficulty { get; init; } = "unknown";
    public ImmutableDictionary<string, int> Inventory { get; init; } = ImmutableDictionary<string, int>.Empty;
    public ImmutableDictionary<string, int> RewardWisps { get; init; } = ImmutableDictionary<string, int>.Empty;
    public ImmutableDictionary<string, long> Resources { get; init; } = ImmutableDictionary<string, long>.Empty;
    public int? GambleFailures { get; init; }
    public ImmutableDictionary<string, GameplayTelemetryGambleCounter>? GambleCounters { get; init; }
    public ImmutableArray<string> GoalUnitIds { get; init; } = [];
}

public sealed record GameplayTelemetryEvent
{
    public required long Sequence { get; init; }
    public required long RecognitionRevision { get; init; }
    public required long ElapsedMs { get; init; }
    public required int Round { get; init; }
    public required int CompletedStory { get; init; }
    public required string Mode { get; init; }
    public required int GuideNumber { get; init; }
    public required string Kind { get; init; }
    public required string Evidence { get; init; }
    public ImmutableDictionary<string, int>? Inventory { get; init; }
    public ImmutableDictionary<string, int>? RewardWisps { get; init; }
    public ImmutableDictionary<string, long>? Resources { get; init; }
    public int? GambleFailures { get; init; }
    public ImmutableDictionary<string, GameplayTelemetryGambleCounter>? GambleCounters { get; init; }
    public string? Action { get; init; }
    public string? TargetUnitId { get; init; }
    public ImmutableArray<string>? GoalUnitIds { get; init; }
    public ImmutableDictionary<string, int>? Selection { get; init; }
    public string? UnitId { get; init; }
    public int? Count { get; init; }
    public ImmutableDictionary<string, int>? Consumed { get; init; }
    public string? WispId { get; init; }
    public ImmutableDictionary<string, int>? Outputs { get; init; }
    public string? GambleType { get; init; }
    public ImmutableDictionary<string, long>? Cost { get; init; }
    public string? Outcome { get; init; }
    public string? OutcomeSource { get; init; }
}

public sealed record GameplayTelemetryPacket
{
    public int SchemaVersion { get; init; } = 3;
    public int ConsentVersion { get; init; } = 3;
    public required string PacketId { get; init; }
    public required string MatchId { get; init; }
    public required int ChunkIndex { get; init; }
    public required string AppVersion { get; init; }
    public required string MapVersion { get; init; }
    public required string MapScriptSha256 { get; init; }
    public required string ProfileVersion { get; init; }
    public required string Difficulty { get; init; }
    public required ImmutableArray<GameplayTelemetryEvent> Events { get; init; }
}

/// <summary>Same strict boundary applies before disk and again before network.</summary>
public static class GameplayTelemetryWire
{
    public const int MaxBytes = 512 * 1024;
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly Regex Id = new("\\A[A-Za-z0-9_:-]{1,80}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex Version = new("\\A[A-Za-z0-9_.-]{1,80}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex Hex32 = new("\\A[a-f0-9]{32}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex Hex64 = new("\\A[a-f0-9]{64}\\z", RegexOptions.CultureInvariant);
    public static bool IsUnitId(string? value) => value is not null && Id.IsMatch(value);
    public static bool IsGambleCounterType(string value) => value is "low" or "middle" or "high" or "world" or "absalom" or "lumberWisp";
    public static bool IsWisp(string value) => value is "e016" or "e017" or "e018" or "e019" or "e0IX" or "e01A";
    public static bool IsResource(string value) => value is "gold" or "lumber" or "trait-points";
    public static byte[] Serialize(GameplayTelemetryPacket packet)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, JsonOptions);
        if (!IsSafe(bytes)) throw new ArgumentException("Unsafe gameplay packet.", nameof(packet));
        return bytes;
    }
    public static bool IsSafe(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaxBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (!Fields(root, ["schemaVersion", "consentVersion", "packetId", "matchId", "chunkIndex", "appVersion", "mapVersion", "mapScriptSha256", "profileVersion", "difficulty", "events"], [])) return false;
            if (root.GetProperty("schemaVersion").GetInt32() != 3 || root.GetProperty("consentVersion").GetInt32() != 3 || root.GetProperty("chunkIndex").GetInt32() < 0 ||
                !Hex32.IsMatch(Text(root, "packetId")) || !Hex32.IsMatch(Text(root, "matchId")) || !Hex64.IsMatch(Text(root, "mapScriptSha256")) ||
                !Version.IsMatch(Text(root, "appVersion")) || !Version.IsMatch(Text(root, "mapVersion")) || !Version.IsMatch(Text(root, "profileVersion")) ||
                !(Text(root, "difficulty") == "unknown" || MatchOutcomeDetector.IsKnownDifficulty(Text(root, "difficulty")))) return false;
            var events = root.GetProperty("events");
            if (events.ValueKind != JsonValueKind.Array || events.GetArrayLength() is < 1 or > 64) return false;
            long sequence = 0;
            foreach (var e in events.EnumerateArray())
            {
                if (!SafeEvent(e) || e.GetProperty("sequence").GetInt64() <= sequence) return false;
                sequence = e.GetProperty("sequence").GetInt64();
            }
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException) { return false; }
    }
    private static bool SafeEvent(JsonElement e)
    {
        string[] common = ["sequence", "recognitionRevision", "elapsedMs", "round", "completedStory", "mode", "guideNumber", "kind", "evidence"];
        var kind = Text(e, "kind");
        string[] required = kind switch
        {
            "observation" => ["inventory", "rewardWisps", "resources"],
            "recommendation" => ["action", "goalUnitIds"],
            "craft" => ["unitId", "count", "consumed"],
            "selection" => ["wispId", "count", "outputs"],
            "gamble" => ["gambleType", "count", "cost", "outputs"],
            "outcome" => ["outcome", "outcomeSource", "inventory", "goalUnitIds"],
            _ => []
        };
        string[] optional = kind switch { "observation" => ["gambleFailures", "gambleCounters"], "recommendation" => ["targetUnitId", "selection"], _ => [] };
        if (required.Length == 0 || !Fields(e, common.Concat(required).ToArray(), optional)) return false;
        if (e.GetProperty("sequence").GetInt64() <= 0 || e.GetProperty("recognitionRevision").GetInt64() < 0 || e.GetProperty("elapsedMs").GetInt64() < 0 ||
            e.GetProperty("round").GetInt32() is < 1 or > 65 || e.GetProperty("completedStory").GetInt32() is < 0 or > 14 || e.GetProperty("guideNumber").GetInt32() is < 0 or > 99 ||
            Text(e, "mode") is not ("Normal" or "Manual" or "Beginner" or "Guide") ||
            Text(e, "evidence") is not ("native-observed" or "inventory-matched" or "observation-only" or "user-confirmed" or "recommendation" or "unknown")) return false;
        foreach (var p in e.EnumerateObject())
        {
            if (p.Name == "gambleCounters")
            {
                if (p.Value.ValueKind != JsonValueKind.Object) return false;
                var types = new HashSet<string>(StringComparer.Ordinal);
                foreach (var counter in p.Value.EnumerateObject())
                {
                    if (!types.Add(counter.Name) || !IsGambleCounterType(counter.Name) ||
                        !Fields(counter.Value, ["attempts", "successes", "failures"], []) ||
                        !counter.Value.GetProperty("attempts").TryGetInt32(out var attempts) || attempts < 0 ||
                        !counter.Value.GetProperty("successes").TryGetInt32(out var successes) || successes < 0 ||
                        !counter.Value.GetProperty("failures").TryGetInt32(out var failures) || failures < 0 ||
                        (long)successes + failures != attempts) return false;
                }
            }
            if (p.Name is "inventory" or "consumed" or "outputs" or "selection" or "rewardWisps" or "resources" or "cost")
            {
                if (p.Value.ValueKind != JsonValueKind.Object) return false;
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pair in p.Value.EnumerateObject())
                    if (!keys.Add(pair.Name) || keys.Count > 512 || !IsUnitId(pair.Name) ||
                        (p.Name == "rewardWisps" && !IsWisp(pair.Name)) ||
                        (p.Name is "resources" or "cost" && !IsResource(pair.Name)) ||
                        !pair.Value.TryGetInt64(out var quantity) || quantity < 0 ||
                        (p.Name is not ("resources" or "cost") && quantity > int.MaxValue)) return false;
            }
            if (p.Name is "unitId" or "targetUnitId" && !IsUnitId(p.Value.GetString()!)) return false;
            if (p.Name == "wispId" && !IsWisp(p.Value.GetString()!)) return false;
            if (p.Name is "count" or "gambleFailures" && (!p.Value.TryGetInt32(out var count) || count < 0)) return false;
            if (p.Name == "goalUnitIds" && (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() > 8 || p.Value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || !IsUnitId(x.GetString()!)))) return false;
        }
        return kind switch
        {
            "recommendation" => Enum.GetNames<CoachActionKind>().Contains(Text(e, "action")),
            "outcome" => Text(e, "outcome") is "clear" or "fail" or "interrupted" && Text(e, "outcomeSource") is "mapSettlement" or "clearRound" or "unitWipe" or "appExit" or "unknown",
            "gamble" => Text(e, "gambleType") is "low" or "medium" or "high",
            _ => true
        };
    }
    private static string Text(JsonElement e, string name) => e.GetProperty(name).GetString() ?? "";
    private static bool Fields(JsonElement e, string[] required, string[] optional)
    {
        if (e.ValueKind != JsonValueKind.Object) return false;
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in e.EnumerateObject())
            if (!found.Add(p.Name) || !required.Contains(p.Name) && !optional.Contains(p.Name)) return false;
        return required.All(found.Contains);
    }
}
