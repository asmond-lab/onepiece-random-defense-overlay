using System.Collections.Immutable;

namespace OrandOverlay;

public sealed record ObservedTelemetryMetadata(string AppVersion, string DatasetFingerprint,
    string GameVersion = "3.0.0.24268", string Source = "live");

public sealed record ObservedTelemetryEvent
{
    public long Sequence { get; init; }
    public long ElapsedMs { get; init; }
    public required string Kind { get; init; }
    public required string State { get; init; }
    public int? Round { get; init; }
    public long? DurationMs { get; init; }
    public long? AgeMs { get; init; }
    public long? GapMs { get; init; }
    public long? SourceRevision { get; init; }
    public ImmutableDictionary<string, int>? Inventory { get; init; }
    public string? TargetUnitId { get; init; }
    public string? Stage { get; init; }
    public string? ReasonCode { get; init; }
    public string? Lane { get; init; }
}

public sealed record ObservedTelemetryPacket
{
    public int SchemaVersion { get; init; } = 4;
    public int ConsentVersion { get; init; } = 4;
    public required string PacketId { get; init; }
    public required string SessionId { get; init; }
    public required string AppVersion { get; init; }
    public string MapVersion { get; init; } = "2.320";
    public required string DatasetFingerprint { get; init; }
    public string GameVersion { get; init; } = "3.0.0.24268";
    public string Source { get; init; } = "live";
    public required ImmutableArray<ObservedTelemetryEvent> Events { get; init; }
}
