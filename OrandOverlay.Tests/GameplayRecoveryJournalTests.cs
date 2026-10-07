using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GameplayRecoveryJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orand-recovery-" + Guid.NewGuid().ToString("N"));
    private static readonly DataCatalog Catalog = new();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void NoConsentDoesNotTouchTheFilesystem()
    {
        var journal = new GameplayRecoveryJournal(() => false, _root, Catalog);
        Assert.False(journal.RecordObservation(Observation(), "round-unavailable"));
        Assert.False(Directory.Exists(_root));
        Assert.Equal("permission-denied", journal.LastError);
    }

    [Fact]
    public void UnknownRoundIsRetainedAsExplicitNullWithoutAGuess()
    {
        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        Assert.True(journal.RecordObservation(Observation() with { Round = 0 }, "round-unavailable"));
        using var json = ReadOnlyRecord();
        var observation = json.RootElement.GetProperty("observation");
        Assert.Equal(JsonValueKind.Null, observation.GetProperty("round").ValueKind);
        Assert.Equal("native-unavailable", observation.GetProperty("roundSource").GetString());
        Assert.Equal(7, observation.GetProperty("inventory").GetProperty("rawcode:S20h").GetInt32());
    }

    [Fact]
    public void BackpressurePreservesSanitizedNativeFacts()
    {
        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        var observation = Observation() with
        {
            Inventory = Inventory(("rawcode:S20h", 7), ("player:private", 1), ("C:\\private", 1)),
            RewardWisps = Inventory(("e018", 2), ("private", 5)),
            Resources = ImmutableDictionary<string, long>.Empty.Add("gold", 100).Add("name", 9)
        };
        Assert.True(journal.RecordObservation(observation, "storage-backpressure"));
        using var json = ReadOnlyRecord();
        var payload = json.RootElement.GetRawText();
        Assert.Contains("storage-backpressure", payload);
        Assert.DoesNotContain("player:private", payload);
        Assert.DoesNotContain("C:\\private", payload);
        Assert.DoesNotContain("private\"", payload);
        var native = json.RootElement.GetProperty("observation");
        Assert.Equal(7, native.GetProperty("inventory").GetProperty("rawcode:S20h").GetInt32());
        Assert.Equal(2, native.GetProperty("rewardWisps").GetProperty("e018").GetInt32());
        Assert.Equal(100, native.GetProperty("resources").GetProperty("gold").GetInt64());
    }

    [Fact]
    public void GapContainsNoInventedInventory()
    {
        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        Assert.True(journal.RecordGap(1, 2, null, nameof(RecognitionState.TransientReadError)));
        using var json = ReadOnlyRecord();
        Assert.Equal("recognition-gap", json.RootElement.GetProperty("kind").GetString());
        Assert.False(json.RootElement.TryGetProperty("observation", out _));
        Assert.False(json.RootElement.GetProperty("gap").TryGetProperty("inventory", out _));
        Assert.Equal("TransientReadError", json.RootElement.GetProperty("gap").GetProperty("state").GetString());
    }

    [Fact]
    public void RecordsAreUniqueAndIdenticalConsecutiveFactsAreDeduplicated()
    {
        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        Assert.True(journal.RecordObservation(Observation(), "storage-backpressure"));
        Assert.True(journal.RecordObservation(Observation() with { RecognitionRevision = 2 }, "storage-backpressure"));
        Assert.True(journal.RecordObservation(Observation() with { RecognitionRevision = 3, Round = 21 }, "storage-backpressure"));
        var files = Files();
        Assert.Equal(2, files.Length);
        Assert.Equal(2, files.Select(Path.GetFileName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, journal.SavedCount);
    }

    [Fact]
    public void CapacityFailureLeavesExistingRecordsUntouched()
    {
        var directory = Path.Combine(_root, "gameplay-v3", "recovery");
        Directory.CreateDirectory(directory);
        for (var index = 0; index < GameplayRecoveryJournal.MaxFiles; index++)
            File.WriteAllText(Path.Combine(directory, index.ToString("D4") + ".recovery.json"), "{}");
        var before = Files().OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        Assert.False(journal.RecordObservation(Observation(), "storage-backpressure"));
        Assert.Equal("capacity", journal.LastError);
        Assert.Equal(before, Files().OrderBy(path => path, StringComparer.Ordinal));
    }

    [Fact]
    public void RevokedConsentPreventsAtomicPublish()
    {
        var checks = 0;
        var journal = new GameplayRecoveryJournal(() => ++checks < 6, _root, Catalog);
        Assert.False(journal.RecordObservation(Observation(), "storage-backpressure"));
        Assert.Empty(Files());
        Assert.Equal("permission-denied", journal.LastError);
    }

    [Fact]
    public void PendingPacketEscrowPreservesExactSerializedBytesAndRequiresConsent()
    {
        var denied = new GameplayRecoveryJournal(() => false, _root, Catalog);
        Assert.False(denied.RecordPendingPacket(Packet()));
        Assert.False(Directory.Exists(_root));
        Assert.Equal("permission-denied", denied.LastError);

        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        var packet = Packet();
        var expected = GameplayTelemetryWire.Serialize(packet);
        Assert.True(journal.RecordPendingPacket(packet));
        var file = Assert.Single(Directory.GetFiles(_root, "*.packet.recovery.json", SearchOption.AllDirectories));
        using var json = JsonDocument.Parse(File.ReadAllBytes(file));
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("pending-packet", root.GetProperty("kind").GetString());
        Assert.Equal("shutdown-unpersisted", root.GetProperty("reason").GetString());
        Assert.Equal(packet.AppVersion, root.GetProperty("appVersion").GetString());
        Assert.Equal(expected, Convert.FromBase64String(root.GetProperty("packetBytes").GetString()!));
    }

    [Fact]
    public void CompleteOwnedTemporaryRecordIsRestoredAndIncompleteStageIsCleaned()
    {
        var directory = Path.Combine(_root, "gameplay-v3", "recovery");
        Directory.CreateDirectory(directory);
        var complete = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".recovery.json.tmp");
        var incomplete = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".recovery.json.tmp");
        File.WriteAllText(complete, "{\"schemaVersion\":1,\"kind\":\"observation\"}");
        File.WriteAllText(incomplete, "{");

        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        Assert.True(journal.RecordObservation(Observation(), "storage-backpressure"));
        Assert.False(File.Exists(complete));
        Assert.True(File.Exists(complete[..^4]));
        Assert.False(File.Exists(incomplete));
        Assert.Equal(1, journal.IncompleteTemporaryCount);
    }

    [Fact]
    public void UnknownTemporaryNamesAreUntouched()
    {
        var directory = Path.Combine(_root, "gameplay-v3", "recovery");
        Directory.CreateDirectory(directory);
        var unknown = Path.Combine(directory, "foreign.recovery.json.tmp");
        File.WriteAllText(unknown, "{");

        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        Assert.True(journal.RecordObservation(Observation(), "storage-backpressure"));
        Assert.True(File.Exists(unknown));
        Assert.Equal(0, journal.IncompleteTemporaryCount);
    }

    [Fact]
    public void RegularRecordsCannotConsumePendingPacketFileReserve()
    {
        var directory = Path.Combine(_root, "gameplay-v3", "recovery");
        Directory.CreateDirectory(directory);
        for (var index = 0; index < GameplayRecoveryJournal.MaxFiles - GameplayRecoveryJournal.PendingPacketReservedFiles; index++)
            File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".recovery.json"), "{}");

        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        Assert.False(journal.RecordObservation(Observation(), "storage-backpressure"));
        Assert.Equal("capacity", journal.LastError);
        Assert.True(journal.RecordPendingPacket(Packet()));
    }

    [Fact]
    public void RegularRecordsCannotConsumePendingPacketByteReserve()
    {
        var directory = Path.Combine(_root, "gameplay-v3", "recovery");
        Directory.CreateDirectory(directory);
        using (var file = File.Create(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".recovery.json")))
            file.SetLength(GameplayRecoveryJournal.MaxStorageBytes - GameplayRecoveryJournal.PendingPacketReservedBytes);

        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        Assert.False(journal.RecordObservation(Observation(), "storage-backpressure"));
        Assert.Equal("capacity", journal.LastError);
        Assert.True(journal.RecordPendingPacket(Packet()));
    }

    [Fact]
    public void NewGapAfterRecoveryIsNotSuppressedAsAnOldDuplicate()
    {
        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        Assert.True(journal.RecordGap(1, 1, 20, nameof(RecognitionState.TransientReadError)));
        Assert.True(journal.RecordGap(1, 2, 20, nameof(RecognitionState.TransientReadError)));
        Assert.Single(Files());
        journal.MarkRecognitionRecovered();
        Assert.True(journal.RecordGap(1, 4, 20, nameof(RecognitionState.TransientReadError)));
        Assert.Equal(2, Files().Length);
    }

    [Fact]
    public void StaleObservationsAreNotPresentedAsCurrentNativeRecoveryFacts()
    {
        var journal = new GameplayRecoveryJournal(() => true, _root, Catalog);
        Assert.False(journal.RecordObservation(Observation() with { IsCurrent = false }, "round-unavailable"));
        Assert.False(Directory.Exists(_root));
    }

    private static GameplayTelemetryPacket Packet() => new()
    {
        PacketId = new string('a', 32), MatchId = new string('b', 32), ChunkIndex = 0,
        AppVersion = "1.0.0", MapVersion = "1.0.0", MapScriptSha256 = new string('c', 64),
        ProfileVersion = "1.0.0", Difficulty = "unknown",
        Events = [new GameplayTelemetryEvent
        {
            Sequence = 1, RecognitionRevision = 0, ElapsedMs = 0, Round = 1, CompletedStory = 0,
            Mode = "Normal", GuideNumber = 0, Kind = "observation", Evidence = "native-observed",
            Inventory = ImmutableDictionary<string, int>.Empty, RewardWisps = ImmutableDictionary<string, int>.Empty,
            Resources = ImmutableDictionary<string, long>.Empty
        }]
    };

    private static GameplayTelemetryObservation Observation() => new()
    {
        MatchGeneration = 1, RecognitionRevision = 1, Round = 20,
        Inventory = Inventory(("rawcode:S20h", 7)),
        RewardWisps = Inventory(("e018", 2)),
        Resources = ImmutableDictionary<string, long>.Empty.Add("gold", 100)
    };
    private static ImmutableDictionary<string, int> Inventory(params (string Id, int Count)[] values) =>
        values.ToImmutableDictionary(value => value.Id, value => value.Count);
    private string[] Files() => Directory.Exists(_root)
        ? Directory.GetFiles(_root, "*.recovery.json", SearchOption.AllDirectories) : [];
    private JsonDocument ReadOnlyRecord() => JsonDocument.Parse(File.ReadAllBytes(Assert.Single(Files())));
}
