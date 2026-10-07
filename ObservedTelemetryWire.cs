using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OrandOverlay;

public static class ObservedTelemetryWire
{
    public const int MaxBytes = 256 * 1024;
    public const long MaxSafeInteger = 9007199254740991;
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly Regex UnitId = new(@"\A[A-Za-z0-9_:-]{1,80}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Version = new(@"\A[A-Za-z0-9_.-]{1,80}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Hex32 = new(@"\A[a-f0-9]{32}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Hex64 = new(@"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant);
    private static bool Matches(Regex pattern, string? value) => value is not null && pattern.IsMatch(value);
    private static bool Safe(long value) => value >= 0 && value <= MaxSafeInteger;
    private static bool Safe(long? value) => value is null || Safe(value.Value);

    public static bool IsValidEvent(ObservedTelemetryEvent value)
    {
        if (value.Sequence <= 0 || !Safe(value.Sequence) || !Safe(value.ElapsedMs) ||
            value.Round is < 1 or > 65 || value.DurationMs is < 0 or > 86400000 ||
            value.AgeMs is < 0 or > 86400000 || !Safe(value.GapMs) || !Safe(value.SourceRevision) ||
            value.TargetUnitId is not null && !Matches(UnitId, value.TargetUnitId) ||
            value.Lane is not (null or "basic" or "full" or "presentation" or "scan") ||
            value.Stage is not (null or "Rare" or "Legend" or "Upper" or "Utility") ||
            value.ReasonCode is not (null or "none" or "freshness" or "binding" or "unavailable" or "unsupported" or
                "configuration" or "cancelled" or "read-error" or "unknown")) return false;
        var validPair = value.Kind switch
        {
            "inventory" => value.State == "fresh" && value.Inventory is not null,
            "recognition" => value.State is "fresh" or "expired" or "read-failed" or "rejected" or "scanning" or
                "paused" or "auto-off" or "game-unavailable",
            "selection" => value.State == "target-selected" && value.TargetUnitId is not null,
            "recommendation" => value.State == "recommended",
            "session" => value.State is "session-start" or "session-reset" or "app-exit",
            _ => false
        };
        if (!validPair || value.Inventory is not null && value.Kind != "inventory") return false;
        return value.Inventory is null || value.Inventory.Count <= 512 &&
            value.Inventory.All(item => Matches(UnitId, item.Key) && item.Value is > 0 and <= 10000);
    }

    public static void Validate(ObservedTelemetryPacket packet)
    {
        if (packet.SchemaVersion != 4 || packet.ConsentVersion != 4 || !Matches(Hex32, packet.PacketId) ||
            !Matches(Hex32, packet.SessionId) || !Matches(Version, packet.AppVersion) ||
            !Map2320DataBundle.IsCompatible(packet.MapVersion) || !Matches(Hex64, packet.DatasetFingerprint) ||
            packet.GameVersion != "3.0.0.24268" || packet.Source is not ("live" or "synthetic-validation") ||
            packet.Events.IsDefaultOrEmpty || packet.Events.Length > 64)
            throw new ArgumentException("Invalid observed telemetry packet.");
        long previous = 0;
        foreach (var value in packet.Events)
        {
            if (value is null || !IsValidEvent(value) || value.Sequence <= previous)
                throw new ArgumentException("Invalid observed telemetry event.");
            previous = value.Sequence;
        }
    }

    public static byte[] Serialize(ObservedTelemetryPacket packet)
    {
        Validate(packet);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, JsonOptions);
        if (bytes.Length > MaxBytes) throw new ArgumentException("Observed telemetry packet too large.");
        return bytes;
    }

    public static ObservedTelemetryPacket Deserialize(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new JsonException("Observed telemetry packet too large.");
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected telemetry object.");
        RejectDuplicateKeys(document.RootElement);
        var result = JsonSerializer.Deserialize<ObservedTelemetryPacket>(bytes, JsonOptions)
            ?? throw new JsonException("Missing observed telemetry packet.");
        // Required wire fields cannot be supplied implicitly by record defaults.
        foreach (var required in new[] { "schemaVersion", "consentVersion", "mapVersion", "gameVersion", "source" })
            if (!document.RootElement.TryGetProperty(required, out _)) throw new JsonException("Missing packet field.");
        foreach (var value in document.RootElement.GetProperty("events").EnumerateArray())
            if (!value.TryGetProperty("sequence", out _) || !value.TryGetProperty("elapsedMs", out _))
                throw new JsonException("Missing event clock.");
        Validate(result);
        return result;
    }

    internal static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null) throw new JsonException("Null telemetry fields must be omitted.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("Duplicate telemetry key.");
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateKeys(item);
    }
}
