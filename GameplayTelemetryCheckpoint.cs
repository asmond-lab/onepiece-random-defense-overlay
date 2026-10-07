using System.Collections.Immutable;

namespace OrandOverlay;

/// <summary>A consistent recorder snapshot: immutable sealed chunks and the still-unpublished tail.</summary>
public sealed record GameplayTelemetryCheckpoint(
    ImmutableArray<GameplayTelemetryPacket> SealedPackets,
    GameplayTelemetryPacket? Draft);
