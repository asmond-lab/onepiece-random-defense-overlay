using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GameplayCheckpointTests
{
    private static readonly Lazy<DataCatalog> Catalog = new(() => { var c = new DataCatalog(); c.Load(false); return c; });
    private static GameplaySessionRecorder Recorder(Func<bool>? consent = null) => new(consent ?? (() => true),
        new("0.6.70-test", "2.314", new string('a', 64), "1"), Catalog.Value);
    private static GameplayTelemetryObservation Observation(long revision) => new()
    {
        MatchGeneration = 1, RecognitionRevision = revision, Round = 20,
        Inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:S20h", 2),
        RewardWisps = ImmutableDictionary<string, int>.Empty,
        Resources = ImmutableDictionary<string, long>.Empty.Add("gold", revision), Difficulty = "악몽"
    };

    [Fact]
    public void CheckpointPersistsTailWithoutCreatingOnePacketPerObservation()
    {
        var recorder = Recorder();
        string? firstId = null;
        for (var revision = 1; revision <= 63; revision++)
        {
            recorder.Observe(Observation(revision));
            var checkpoint = recorder.Checkpoint();
            Assert.Empty(checkpoint.SealedPackets);
            Assert.NotNull(checkpoint.Draft);
            firstId ??= checkpoint.Draft.PacketId;
            Assert.Equal(firstId, checkpoint.Draft.PacketId);
            Assert.Equal(revision, checkpoint.Draft.Events.Length);
            Assert.True(GameplayTelemetryWire.IsSafe(GameplayTelemetryWire.Serialize(checkpoint.Draft)));
        }
        recorder.Observe(Observation(64));
        var full = recorder.Checkpoint();
        var sealedPacket = Assert.Single(full.SealedPackets);
        Assert.Equal(firstId, sealedPacket.PacketId);
        Assert.Equal(64, sealedPacket.Events.Length);
        Assert.Null(full.Draft);
        recorder.Observe(Observation(65));
        var next = recorder.Checkpoint().Draft!;
        Assert.NotEqual(firstId, next.PacketId);
        Assert.Equal(1, next.ChunkIndex);
        Assert.Equal(65, Assert.Single(next.Events).Sequence);
    }

    [Fact]
    public void PartialDraftSnapshotsAreImmutableAndSealKeepsTheIdentity()
    {
        var recorder = Recorder(); recorder.Observe(Observation(1));
        var first = recorder.Checkpoint().Draft!;
        var bytes = GameplayTelemetryWire.Serialize(first);
        recorder.Observe(Observation(2));
        var second = recorder.Checkpoint().Draft!;
        Assert.Equal(first.PacketId, second.PacketId);
        Assert.Single(first.Events); Assert.Equal(2, second.Events.Length);
        Assert.Equal(bytes, GameplayTelemetryWire.Serialize(first));
        Assert.Equal(GameplayTelemetryWire.Serialize(second),
            GameplayTelemetryWire.Serialize(Assert.Single(recorder.Checkpoint(true).SealedPackets)));
        Assert.Null(recorder.Checkpoint().Draft);
    }

    [Fact]
    public void ConsentRevocationForgetsUnsealedAndSealedData()
    {
        var permission = true;
        var recorder = Recorder(() => permission); recorder.Observe(Observation(1));
        permission = false;
        var checkpoint = recorder.Checkpoint();
        Assert.Empty(checkpoint.SealedPackets); Assert.Null(checkpoint.Draft);
        permission = true; Assert.Null(recorder.Checkpoint().Draft);
    }

    [Fact]
    public void StorageBackpressureIsExplicitAndDoesNotEraseAcceptedEvents()
    {
        var recorder = Recorder(); recorder.Observe(Observation(1));
        recorder.CapturePaused = true; recorder.Observe(Observation(2));
        Assert.Equal(1, recorder.PausedObservationCount);
        Assert.Single(recorder.Checkpoint().Draft!.Events);
        recorder.CapturePaused = false; recorder.Observe(Observation(3));
        var resumed = recorder.Checkpoint();
        var interrupted = Assert.Single(resumed.SealedPackets);
        Assert.Equal(1, interrupted.Events[0].RecognitionRevision);
        Assert.Equal("interrupted", interrupted.Events[^1].Outcome);
        Assert.Equal(3, Assert.Single(resumed.Draft!.Events).RecognitionRevision);
        Assert.NotEqual(interrupted.MatchId, resumed.Draft.MatchId);
    }

    [Fact]
    public void MissingAcceptedObservationsCannotProduceAFalselyCompleteClear()
    {
        var recorder = Recorder(); recorder.Observe(Observation(1));
        recorder.CapturePaused = true; recorder.Observe(Observation(2));
        recorder.Complete("clear", "mapSettlement");
        var outcome = Assert.Single(recorder.DrainPackets().SelectMany(p => p.Events), e => e.Kind == "outcome");
        Assert.Equal("interrupted", outcome.Outcome);
        Assert.Equal("unknown", outcome.OutcomeSource);
    }

    [Fact]
    public void NativeUnknownRawcodeIsRetainedButArbitraryIdentifiersAreNot()
    {
        var unknown = Enumerable.Range(0, 10000).Select(n => "rawcode:" + n.ToString("0000"))
            .First(id => !Catalog.Value.RawcodeCatalog.ContainsKey(id[8..]));
        var recorder = Recorder();
        recorder.Observe(Observation(1) with { Inventory = ImmutableDictionary<string, int>.Empty
            .Add(unknown, 3).Add("player:private", 1).Add("rawcode:toolong", 1) });
        var inventory = Assert.Single(recorder.Checkpoint().Draft!.Events).Inventory!;
        Assert.Single(inventory); Assert.Equal(3, inventory[unknown]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PassiveResourceUpdatesCannotErasePendingCraftCorrelation(bool outputFirst)
    {
        var unit = Catalog.Value.AllUnits.First(u => u.Recipe.Count > 0 &&
            u.Recipe.All(p => p.Value > 0 && Catalog.Value.Unit(p.Key).Tier != "자원") && !u.Recipe.ContainsKey(u.Id));
        var before = Observation(1) with { Inventory = unit.Recipe.ToImmutableDictionary() };
        var partial = before with { RecognitionRevision = 2,
            Inventory = outputFirst ? before.Inventory.Add(unit.Id, 1) : ImmutableDictionary<string, int>.Empty };
        var recorder = Recorder(); recorder.Observe(before); recorder.Observe(partial);
        for (var revision = 3; revision <= 25; revision++)
            recorder.Observe(partial with { RecognitionRevision = revision,
                Resources = ImmutableDictionary<string, long>.Empty.Add("gold", revision) });
        recorder.Observe(before with { RecognitionRevision = 26,
            Inventory = ImmutableDictionary<string, int>.Empty.Add(unit.Id, 1) });
        var events = recorder.DrainPackets().SelectMany(packet => packet.Events).ToArray();
        Assert.Single(events, e => e.Kind == "craft");
        Assert.Equal(26, events.Count(e => e.Kind == "observation"));
    }
}
