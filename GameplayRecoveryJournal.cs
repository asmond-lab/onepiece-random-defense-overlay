using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OrandOverlay;

/// <summary>
/// Consent-gated local-only evidence for native observations which deliberately cannot be
/// represented by gameplay schema 3. Observation and gap records are never telemetry packets.
/// Pending-packet records are a separate local escrow of sealed packet bytes; the wrapper is never uploaded.
/// </summary>
public sealed class GameplayRecoveryJournal
{
    public const int MaxFiles = 512;
    public const long MaxStorageBytes = 32L * 1024 * 1024;
    /// <summary>Four slots and 2 MiB of the fixed quota are reserved for sealed-packet escrow.</summary>
    public const int PendingPacketReservedFiles = 4;
    public const long PendingPacketReservedBytes = 2L * 1024 * 1024;
    private const int MaxRegularFiles = MaxFiles - PendingPacketReservedFiles;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly Regex Rawcode = new("\\Arawcode:[A-Za-z0-9]{4}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex OwnedTemporaryName = new("\\A[0-9a-f]{32}(?:\\.packet)?\\.recovery\\.json\\.tmp\\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly object _gate = new();
    private readonly Func<bool> _permission;
    private readonly DataCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly string _directory;
    private string? _lastObservationFacts;
    private string? _lastGapFacts;
    private long _savedCount;
    private string? _lastError;
    private long _incompleteTemporaryCount;

    public GameplayRecoveryJournal(Func<bool> permission, string ownedAbsoluteRoot, DataCatalog catalog,
        TimeProvider? timeProvider = null)
    {
        _permission = permission ?? throw new ArgumentNullException(nameof(permission));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (!Path.IsPathFullyQualified(ownedAbsoluteRoot))
            throw new ArgumentException("An owned absolute root is required.", nameof(ownedAbsoluteRoot));
        _directory = Path.Combine(ownedAbsoluteRoot, "gameplay-v3", "recovery");
        _time = timeProvider ?? TimeProvider.System;
    }

    public long SavedCount { get { lock (_gate) return _savedCount; } }
    /// <summary>Cumulative count of incomplete owned temporary stages safely removed during maintenance.</summary>
    public long IncompleteTemporaryCount { get { lock (_gate) return _incompleteTemporaryCount; } }

    public bool RecoverPendingWrites()
    {
        lock (_gate)
        {
            if (!_permission()) return Fail("permission-denied");
            if (!Directory.Exists(_directory)) return true;
            try { ExpireAndList(); return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            { return Fail("io-failure"); }
        }
    }

    public void MarkRecognitionRecovered()
    {
        lock (_gate) _lastGapFacts = null;
    }

    /// <summary>Fixed status code only: permission-denied, invalid-input, capacity, or io-failure.</summary>
    public string? LastError { get { lock (_gate) return _lastError; } }

    public bool RecordObservation(GameplayTelemetryObservation actual, string reason)
    {
        lock (_gate)
        {
            if (!_permission()) return Fail("permission-denied");
            if (actual is null || !actual.IsCurrent || reason is not ("round-unavailable" or "storage-backpressure") ||
                actual.MatchGeneration < 0 || actual.RecognitionRevision < 0)
                return Fail("invalid-input");

            var observation = Sanitize(actual);
            var facts = JsonSerializer.Serialize(new { reason, observation.MatchGeneration, observation.Round,
                observation.CompletedStory, observation.Mode, observation.GuideNumber, observation.Difficulty, observation.Inventory,
                observation.RewardWisps, observation.Resources, observation.GambleFailures,
                observation.GambleCounters, observation.GoalUnitIds }, JsonOptions);
            if (facts == _lastObservationFacts) { _lastError = null; return true; }
            var record = new ObservationRecord
            {
                SchemaVersion = 1,
                AppVersion = UpdateService.CurrentBuildVersion,
                Kind = "observation",
                Reason = reason,
                ObservedAtUtc = _time.GetUtcNow(),
                Observation = observation
            };
            if (!Save(record)) return false;
            _lastObservationFacts = facts;
            return true;
        }
    }

    /// <summary>
    /// Consent-gated local escrow for a sealed packet that could not enter the regular outbox at graceful shutdown.
    /// Packet bytes are serialized once and retained as base64; only the parent outbox may later restore them.
    /// </summary>
    public bool RecordPendingPacket(GameplayTelemetryPacket packet)
    {
        lock (_gate)
        {
            if (!_permission()) return Fail("permission-denied");
            if (packet is null) return Fail("invalid-input");
            byte[] packetBytes;
            try { packetBytes = GameplayTelemetryWire.Serialize(packet); }
            catch (Exception exception) when (exception is ArgumentException or JsonException) { return Fail("invalid-input"); }
            return Save(new PendingPacketRecord
            {
                SchemaVersion = 1,
                AppVersion = packet.AppVersion,
                Kind = "pending-packet",
                Reason = "shutdown-unpersisted",
                ObservedAtUtc = _time.GetUtcNow(),
                PacketBytes = packetBytes
            }, pendingPacket: true);
        }
    }

    public bool RecordGap(long generation, long revision, int? lastVerifiedRound, string state)
    {
        lock (_gate)
        {
            if (!_permission()) return Fail("permission-denied");
            if (generation < 0 || revision < 0 || !Enum.GetNames<RecognitionState>().Contains(state, StringComparer.Ordinal) ||
                lastVerifiedRound is < 1 or > 65)
                return Fail("invalid-input");

            var gap = new GapMetadata { MatchGeneration = generation, RecognitionRevision = revision,
                LastVerifiedRound = lastVerifiedRound, State = state };
            var facts = JsonSerializer.Serialize(new { gap.MatchGeneration, gap.LastVerifiedRound, gap.State }, JsonOptions);
            if (facts == _lastGapFacts) { _lastError = null; return true; }
            var record = new GapRecord
            {
                SchemaVersion = 1,
                AppVersion = UpdateService.CurrentBuildVersion,
                Kind = "recognition-gap",
                Reason = "recognition-gap",
                ObservedAtUtc = _time.GetUtcNow(),
                Gap = gap
            };
            if (!Save(record)) return false;
            _lastGapFacts = facts;
            return true;
        }
    }

    private bool Save<T>(T record, bool pendingPacket = false)
    {
        try
        {
            // Consent is checked before every filesystem operation, including directory creation.
            if (!_permission()) return Fail("permission-denied");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
            if (bytes.LongLength > MaxStorageBytes) return Fail("capacity");
            if (!_permission()) return Fail("permission-denied");
            Directory.CreateDirectory(_directory);
            if (!_permission()) return Fail("permission-denied");
            var files = ExpireAndList();
            if (!HasCapacity(files, bytes.LongLength, pendingPacket)) return Fail("capacity");

            var suffix = pendingPacket ? ".packet.recovery.json" : ".recovery.json";
            var final = Path.Combine(_directory, Guid.NewGuid().ToString("N") + suffix);
            var temporary = final + ".tmp";
            try
            {
                if (!_permission()) return Fail("permission-denied");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
                if (!_permission()) return Fail("permission-denied");
                // This call's stage is counted but never recovered or deleted by the second maintenance pass.
                var current = ExpireAndList(temporary);
                if (!HasCapacity(current, 0, pendingPacket)) return Fail("capacity");
                if (!_permission()) return Fail("permission-denied");
                File.Move(temporary, final, overwrite: false);
                File.SetLastWriteTimeUtc(final, _time.GetUtcNow().UtcDateTime);
                _savedCount++;
                _lastError = null;
                return true;
            }
            finally
            {
                // Cleanup is limited to this call's temporary file, never another writer's stage.
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            return Fail("io-failure");
        }
    }

    private static bool HasCapacity(List<FileInfo> files, long additionalBytes, bool pendingPacket)
    {
        var totalBytes = files.Sum(file => file.Length);
        var regularFiles = files.Count(file => !file.Name.Contains(".packet.recovery.json", StringComparison.OrdinalIgnoreCase));
        if (files.Count > MaxFiles || (additionalBytes > 0 && files.Count >= MaxFiles) || totalBytes > MaxStorageBytes - additionalBytes) return false;
        // Normal observation/gap records cannot consume the escrow reserve. Pending packets may use the fixed absolute quota.
        return pendingPacket || (additionalBytes > 0 ? regularFiles < MaxRegularFiles : regularFiles <= MaxRegularFiles) &&
            totalBytes <= MaxStorageBytes - PendingPacketReservedBytes - additionalBytes;
    }

    private List<FileInfo> ExpireAndList(string? excludedTemporary = null)
    {
        RecoverTemporaryStages(excludedTemporary);
        var now = _time.GetUtcNow().UtcDateTime;
        var finals = Directory.GetFiles(_directory, "*.recovery.json")
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal).ToList();
        foreach (var file in finals.Where(file => now - file.LastWriteTimeUtc > Retention).ToArray())
        {
            if (!_permission()) throw new UnauthorizedAccessException();
            file.Delete();
            finals.Remove(file);
        }
        var temporaries = Directory.GetFiles(_directory, "*.recovery.json.tmp")
            .Where(path => OwnedTemporaryName.IsMatch(Path.GetFileName(path)))
            .Select(path => new FileInfo(path));
        return finals.Concat(temporaries).OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal).ToList();
    }

    private void RecoverTemporaryStages(string? excludedTemporary)
    {
        foreach (var temporary in Directory.GetFiles(_directory, "*.recovery.json.tmp")
            .Where(path => OwnedTemporaryName.IsMatch(Path.GetFileName(path))))
        {
            if (excludedTemporary is not null && string.Equals(temporary, excludedTemporary, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (!_permission()) throw new UnauthorizedAccessException();
                if (!TryReadCompleteLocalRecord(temporary))
                {
                    if (!_permission()) throw new UnauthorizedAccessException();
                    File.Delete(temporary);
                    _incompleteTemporaryCount++;
                    continue;
                }
                var final = temporary[..^4];
                // Existing finals and locked stages can belong to another writer, so leave them untouched.
                if (!File.Exists(final))
                {
                    if (!_permission()) throw new UnauthorizedAccessException();
                    File.Move(temporary, final, overwrite: false);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                // Preserve active writers and file conflicts; their bytes remain part of quota accounting.
            }
        }
    }

    private static bool TryReadCompleteLocalRecord(string path)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { return false; }
        using var ownedDocument = document;
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("schemaVersion", out var schemaVersion) && schemaVersion.ValueKind == JsonValueKind.Number &&
            schemaVersion.TryGetInt32(out var version) && version == 1 &&
            root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String &&
            kind.GetString() is "observation" or "recognition-gap" or "pending-packet";
    }

    private bool Fail(string code) { _lastError = code; return false; }

    private RecoveryObservation Sanitize(GameplayTelemetryObservation actual) => new()
    {
        MatchGeneration = actual.MatchGeneration,
        RecognitionRevision = actual.RecognitionRevision,
        // Zero and negative are the reader's unavailable sentinel. Never substitute a prior round.
        Round = actual.Round > 0 ? actual.Round : null,
        RoundSource = actual.Round > 0 ? "native-raw" : "native-unavailable",
        CompletedStory = actual.CompletedStory is >= 0 and <= 14 ? actual.CompletedStory : null,
        Mode = Enum.IsDefined(actual.Mode) ? actual.Mode.ToString() : null,
        GuideNumber = actual.GuideNumber is >= 0 and <= 99 ? actual.GuideNumber : null,
        Difficulty = MatchOutcomeDetector.IsKnownDifficulty(actual.Difficulty) ? actual.Difficulty : "unknown",
        Inventory = Units(actual.Inventory),
        RewardWisps = IntFacts(actual.RewardWisps, GameplayTelemetryWire.IsWisp),
        Resources = LongFacts(actual.Resources, GameplayTelemetryWire.IsResource),
        GambleFailures = actual.GambleFailures is >= 0 ? actual.GambleFailures : null,
        GambleCounters = Counters(actual.GambleCounters),
        GoalUnitIds = actual.GoalUnitIds.Where(IsUnit).Distinct(StringComparer.Ordinal).Take(8).ToArray()
    };

    private Dictionary<string, int> Units(ImmutableDictionary<string, int> source) => source
        .Where(pair => IsUnit(pair.Key) && pair.Value >= 0).Take(512)
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    private static Dictionary<string, int> IntFacts(ImmutableDictionary<string, int> source, Func<string, bool> allowed) => source
        .Where(pair => allowed(pair.Key) && pair.Value >= 0).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    private static Dictionary<string, long> LongFacts(ImmutableDictionary<string, long> source, Func<string, bool> allowed) => source
        .Where(pair => allowed(pair.Key) && pair.Value >= 0).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    private static Dictionary<string, Counter>? Counters(ImmutableDictionary<string, GameplayTelemetryGambleCounter>? source) => source?
        .Where(pair => GameplayTelemetryWire.IsGambleCounterType(pair.Key) && pair.Value.IsValid)
        .ToDictionary(pair => pair.Key, pair => new Counter(pair.Value.Attempts, pair.Value.Successes, pair.Value.Failures), StringComparer.Ordinal);
    private bool IsUnit(string id) => GameplayTelemetryWire.IsUnitId(id) &&
        (_catalog.UnitsById.ContainsKey(id) || Rawcode.IsMatch(id));

    private sealed class ObservationRecord
    {
        public int SchemaVersion { get; init; }
        public required string AppVersion { get; init; }
        public required string Kind { get; init; }
        public required string Reason { get; init; }
        public DateTimeOffset ObservedAtUtc { get; init; }
        public required RecoveryObservation Observation { get; init; }
    }
    private sealed class GapRecord
    {
        public int SchemaVersion { get; init; }
        public required string AppVersion { get; init; }
        public required string Kind { get; init; }
        public required string Reason { get; init; }
        public DateTimeOffset ObservedAtUtc { get; init; }
        public required GapMetadata Gap { get; init; }
    }
    private sealed class RecoveryObservation
    {
        public long MatchGeneration { get; init; }
        public long RecognitionRevision { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? Round { get; init; }
        public required string RoundSource { get; init; }
        public int? CompletedStory { get; init; }
        public string? Mode { get; init; }
        public int? GuideNumber { get; init; }
        public required string Difficulty { get; init; }
        public required Dictionary<string, int> Inventory { get; init; }
        public required Dictionary<string, int> RewardWisps { get; init; }
        public required Dictionary<string, long> Resources { get; init; }
        public int? GambleFailures { get; init; }
        public Dictionary<string, Counter>? GambleCounters { get; init; }
        public required string[] GoalUnitIds { get; init; }
    }
    private sealed class GapMetadata
    {
        public long MatchGeneration { get; init; }
        public long RecognitionRevision { get; init; }
        public int? LastVerifiedRound { get; init; }
        public required string State { get; init; }
    }
    private sealed class PendingPacketRecord
    {
        public int SchemaVersion { get; init; }
        public required string AppVersion { get; init; }
        public required string Kind { get; init; }
        public required string Reason { get; init; }
        public DateTimeOffset ObservedAtUtc { get; init; }
        public required byte[] PacketBytes { get; init; }
    }
    private sealed record Counter(int Attempts, int Successes, int Failures);
}
