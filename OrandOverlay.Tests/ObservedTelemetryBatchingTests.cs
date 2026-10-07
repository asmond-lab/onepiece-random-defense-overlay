using System.Reflection;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;
using Scope = OrandOverlay.Tests.ObservedTelemetryClientTests.Scope;

namespace OrandOverlay.Tests;

public sealed class ObservedTelemetryBatchingTests
{
    [Fact]
    public async Task SteadyIndividuallyDurableEventsAreGroupedAfterRestart()
    {
        using var scope = new Scope();
        await using var client = scope.Client();
        var session = client.SessionId;
        for (var index = 0; index < 12; index++)
        {
            Assert.True(client.Record(new() { Kind = "recognition", State = index % 2 == 0 ? "fresh" : "expired" }));
            // Await the actual writer drain, not a timer or a memory burst.
            await PersistAsync(client);
            Assert.Equal(0, client.PendingMemoryPacketCount);
            Assert.NotEmpty(Directory.GetFiles(scope.Root, "*", SearchOption.AllDirectories));
            Assert.Equal(0, scope.Handler.Calls);
        }
        var restarted = scope.Outbox();
        Assert.Equal(1, await restarted.FlushAsync());
        var packet = Assert.Single(scope.Handler.Packets);
        Assert.Equal(session, packet.SessionId);
        Assert.Equal(Enumerable.Range(1, 12).Select(i => (long)i), packet.Events.Select(e => e.Sequence));
        Assert.Equal(Enumerable.Range(0, 12).Select(i => i % 2 == 0 ? "fresh" : "expired"), packet.Events.Select(e => e.State));
        Assert.Empty(Directory.GetFiles(scope.Root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(64, 1)]
    [InlineData(65, 2)]
    [InlineData(256, 4)]
    public async Task CountBoundaryPreservesEveryAcceptedTransition(int count, int batches)
    {
        using var scope = new Scope();
        await using var client = scope.Client();
        for (var i = 0; i < count; i++)
            Assert.True(client.Record(Event(i + 1)));
        if (count == 256)
        {
            Assert.False(client.Record(new() { Kind = "recognition", State = "game-unavailable" }));
            Assert.True(client.IsBackpressured);
        }
        await client.FlushAsync();
        Assert.Equal(batches, scope.Handler.Packets.Count);
        Assert.Equal(batches, scope.Handler.Calls);
        var packets = scope.Handler.Packets.OrderBy(p => p.Events[0].Sequence).ToArray();
        Assert.All(packets, p => Assert.InRange(p.Events.Length, 1, 64));
        Assert.Equal(Enumerable.Range(1, count).Select(i => (long)i), packets.SelectMany(p => p.Events).Select(e => e.Sequence));
        Assert.Equal(Enumerable.Range(1, count).Select(i => Event(i).State), packets.SelectMany(p => p.Events).Select(e => e.State));
        Assert.Equal(count == 65 ? new[] { 64, 1 } : Enumerable.Repeat(64, batches), packets.Select(p => p.Events.Length));
        Assert.False(client.IsBackpressured);
        Assert.Empty(scope.PendingFiles());
    }

    [Fact]
    public async Task ByteBoundaryUsesExactSerializedEnvelopeAndPreservesInventory()
    {
        using var scope = new Scope();
        var outbox = scope.Outbox();
        var template = ObservedTelemetryWireTests.Packet();
        var events = Enumerable.Range(1, 12).Select(i => new ObservedTelemetryEvent
        {
            Kind = "inventory", State = "fresh", Sequence = i,
            Inventory = Enumerable.Range(0, 512).ToImmutableDictionary(k => k.ToString("D3") + new string('a', 77), _ => i % 2 == 0 ? 9999 : 10000)
        }).ToArray();
        foreach (var value in events)
            Assert.True(await outbox.StageAsync(template with { PacketId = Guid.NewGuid().ToString("N"), Events = [value] }));
        Assert.Equal(3, await outbox.FlushAsync());
        var packets = scope.Handler.Packets.OrderBy(p => p.Events[0].Sequence).ToArray();
        Assert.Equal(new[] { 5, 5, 2 }, packets.Select(p => p.Events.Length));
        Assert.All(packets, p => Assert.InRange(ObservedTelemetryWire.Serialize(p).Length, 1, ObservedTelemetryWire.MaxBytes));
        foreach (var packet in packets.Take(2))
        {
            var extra = events[(int)packet.Events[^1].Sequence];
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(packet with { Events = packet.Events.Add(extra) }, ObservedTelemetryWire.JsonOptions).Length > ObservedTelemetryWire.MaxBytes);
        }
        Assert.Equal(events.Select(e => JsonSerializer.Serialize(e, ObservedTelemetryWire.JsonOptions)),
            packets.SelectMany(p => p.Events).Select(e => JsonSerializer.Serialize(e, ObservedTelemetryWire.JsonOptions)));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("app")]
    [InlineData("map")]
    [InlineData("dataset")]
    [InlineData("source")]
    public async Task EnvelopeBoundaryNeverMergesAdjacentEvents(string field)
    {
        using var scope = new Scope();
        var first = ObservedTelemetryWireTests.Packet() with { Events = [Event(1)], Source = "live" };
        var second = first with { PacketId = Guid.NewGuid().ToString("N"), Events = [Event(2)] };
        second = field switch
        {
            "session" => second with { SessionId = Guid.NewGuid().ToString("N"), Events = [Event(1)] },
            "app" => second with { AppVersion = "1.0.3" },
            "map" => second with { MapVersion = "2.321" },
            "dataset" => second with { DatasetFingerprint = new string('b', 64) },
            _ => second with { Source = "synthetic-validation" }
        };
        var outbox = scope.Outbox();
        Assert.True(await outbox.StageAsync(first));
        Assert.True(await outbox.StageAsync(second));
        Assert.Equal(2, await outbox.FlushAsync());
        Assert.All(scope.Handler.Packets, p => Assert.Single(p.Events));
        Assert.Contains(scope.Handler.Packets, p => ObservedTelemetryWire.Serialize(p).SequenceEqual(ObservedTelemetryWire.Serialize(first)));
        Assert.Contains(scope.Handler.Packets, p => ObservedTelemetryWire.Serialize(p).SequenceEqual(ObservedTelemetryWire.Serialize(second)));
    }

    [Fact]
    public async Task LegacyAttemptedSingletonIsNeverRebatchedWithNewStaging()
    {
        using var scope = new Scope();
        var outbox = scope.Outbox();
        var legacy = ObservedTelemetryWireTests.Packet() with { Events = [Event(1)], Source = "live" };
        Assert.True(await outbox.EnqueueAsync(legacy));
        scope.Handler.WrongReceipt = true;
        Assert.Equal(0, await outbox.FlushAsync());
        var original = await File.ReadAllBytesAsync(Assert.Single(scope.PendingFiles()));
        foreach (var i in new[] { 2, 3 })
            Assert.True(await outbox.StageAsync(legacy with { PacketId = Guid.NewGuid().ToString("N"), Events = [Event(i)] }));
        scope.Handler.WrongReceipt = false;
        Assert.Equal(2, await scope.Outbox().FlushAsync());
        var retry = Assert.Single(scope.Handler.Packets.Skip(1), p => p.PacketId == legacy.PacketId);
        Assert.Equal(original, ObservedTelemetryWire.Serialize(retry));
        Assert.Equal(new long[] { 2, 3 }, Assert.Single(scope.Handler.Packets, p => p.PacketId != legacy.PacketId).Events.Select(e => e.Sequence));
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("lost")]
    [InlineData("consent")]
    [InlineData("cancel")]
    public async Task AttemptedSealedBatchSurvivesRestartWithIdenticalBytes(string failure)
    {
        using var scope = new Scope();
        await using var client = scope.Client();
        using var cancelled = new CancellationTokenSource();
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Handler.Receipt = packet =>
        {
            // The transport must expose only an already sealed, byte-identical file.
            Assert.Equal(ObservedTelemetryWire.Serialize(packet), File.ReadAllBytes(Assert.Single(scope.PendingFiles())));
            attempted.SetResult();
            if (failure == "lost") throw new HttpRequestException("Lost acknowledgement fixture.");
            if (failure == "consent") scope.Allowed = false;
            if (failure == "cancel") cancelled.Cancel();
            return JsonSerializer.Serialize(new { schemaVersion = 4, packetId = failure == "wrong" ? new string('f', 32) : packet.PacketId, accepted = true });
        };
        for (var i = 1; i <= 3; i++) { Assert.True(client.Record(Event(i))); await PersistAsync(client); }
        var flush = client.FlushAsync(cancelled.Token);
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (failure == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush);
        else await flush;
        var bytes = await File.ReadAllBytesAsync(Assert.Single(scope.PendingFiles()));
        Assert.Equal(3, ObservedTelemetryWire.Deserialize(bytes).Events.Length);
        Assert.Equal(0, client.AcceptedPacketCount);
        scope.Handler.Receipt = null;
        scope.Allowed = true;
        Assert.Equal(1, await scope.Outbox().FlushAsync());
        Assert.Equal(bytes, ObservedTelemetryWire.Serialize(scope.Handler.Packets[1]));
        Assert.Empty(scope.PendingFiles());
    }

    [Fact]
    public async Task SealFailureAndOrphanTemporaryRecoverWithoutDuplicateEvents()
    {
        using var scope = new Scope();
        var outbox = scope.Outbox();
        var packet = ObservedTelemetryWireTests.Packet() with { Events = [Event(1)], Source = "live" };
        Assert.True(await outbox.StageAsync(packet));
        var active = Path.Combine(scope.Root, "observed-v4", "pending", "active.stage");
        var bytes = await File.ReadAllBytesAsync(active);
        // Interrupted replacement: temporary data is not a committed event or POST candidate.
        var orphan = Path.Combine(Path.GetDirectoryName(active)!, Guid.NewGuid().ToString("N") + ".tmp");
        await File.WriteAllBytesAsync(orphan, ObservedTelemetryWire.Serialize(packet with { Events = [Event(1), Event(2)] }));
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddMinutes(-6));
        using (var held = new FileStream(active, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(0, await outbox.FlushAsync());
            Assert.NotNull(outbox.LastError);
            Assert.Equal(0, scope.Handler.Calls);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(active));
        }
        // New owner resumes the pre-rename state. Post-rename recovery is covered by lost ACK.
        Assert.Equal(1, await scope.Outbox().FlushAsync());
        Assert.Equal(bytes, ObservedTelemetryWire.Serialize(Assert.Single(scope.Handler.Packets)));
        Assert.Empty(Directory.GetFiles(scope.Root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CapacityRefusalRetainsActiveAndMemoryThenRecovers()
    {
        using var scope = new Scope();
        await using var client = scope.Client();
        Assert.True(client.Record(Event(1)));
        await PersistAsync(client);
        var folder = Path.Combine(scope.Root, "observed-v4", "pending");
        var fillers = Enumerable.Range(0, 255).Select(_ => Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json")).ToArray();
        foreach (var path in fillers) await File.WriteAllTextAsync(path, "{}");
        // Active consumes one packet slot but append does not need a new slot.
        Assert.True(client.Record(Event(2)));
        await PersistAsync(client);
        Assert.Equal(0, client.PendingMemoryPacketCount);
        client.ResetSession();
        Assert.True(client.Record(Event(1)));
        await PersistAsync(client);
        Assert.True(client.IsBackpressured);
        Assert.Equal(1, client.PendingMemoryPacketCount);
        foreach (var path in fillers) File.Delete(path);
        await client.FlushAsync();
        Assert.Equal(3, scope.Handler.Packets.SelectMany(p => p.Events).Count());
        Assert.Equal(2, scope.Handler.Packets.Count);
        Assert.False(client.IsBackpressured);
    }

    [Fact]
    public async Task RevokedAndCancelledStagingCannotReplaceCommittedData()
    {
        using var scope = new Scope();
        var outbox = scope.Outbox();
        var first = ObservedTelemetryWireTests.Packet() with { Events = [Event(1)], Source = "live" };
        Assert.True(await outbox.StageAsync(first));
        var path = Path.Combine(scope.Root, "observed-v4", "pending", "active.stage");
        var bytes = await File.ReadAllBytesAsync(path);
        var next = first with { PacketId = Guid.NewGuid().ToString("N"), Events = [Event(2)] };
        scope.Allowed = false;
        Assert.False(await outbox.StageAsync(next));
        Assert.Equal(0, await outbox.FlushAsync());
        scope.Allowed = true;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => outbox.StageAsync(next, cancelled.Token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.True(await outbox.StageAsync(next));
        Assert.Equal(1, await outbox.FlushAsync());
        Assert.Equal(new long[] { 1, 2 }, Assert.Single(scope.Handler.Packets).Events.Select(e => e.Sequence));
    }

    private static ObservedTelemetryEvent Event(int sequence) => new()
    { Kind = "recognition", State = sequence % 2 == 1 ? "fresh" : "expired", Sequence = sequence };

    private static Task PersistAsync(ObservedTelemetryClient client) =>
        (Task)typeof(ObservedTelemetryClient).GetMethod("PersistPendingAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, [CancellationToken.None])!;
}
