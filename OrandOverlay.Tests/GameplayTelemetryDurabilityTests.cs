using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GameplayTelemetryDurabilityTests
{
    private static readonly Uri Endpoint = new("https://fixture.invalid/v3/gameplay");
    private static readonly GameplayTelemetryMetadata Metadata = new("0.6.70", "2.314", new string('a', 64), "1");
    private static readonly Lazy<DataCatalog> Catalog = new(() => { var catalog = new DataCatalog(); catalog.Load(false); return catalog; });

    [Fact]
    public async Task SlowPostDoesNotBlockANewDraftCheckpoint()
    {
        using var scope = new Scope();
        var postEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options) return Accepted();
            var bytes = await request.Content!.ReadAsByteArrayAsync(token);
            postEntered.TrySetResult();
            await releasePost.Task.WaitAsync(token);
            return Ack(PacketId(bytes));
        });
        using var http = new HttpClient(handler);
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        Assert.True(await box.EnqueueAsync(Packet()));
        var flush = box.FlushAsync();
        await postEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var draft = Packet();
        Assert.True(await box.SaveDraftAsync(draft).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(File.Exists(scope.Draft(draft.PacketId)));

        releasePost.TrySetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RapidObservationsCheckpointIntoChunksInsteadOfOneEventFiles()
    {
        using var scope = new Scope();
        using var handler = new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Options
            ? Rejected() : throw new InvalidOperationException("Handshake rejection must prevent POST.")));
        using var http = new HttpClient(handler);
        var recorder = Recorder();
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        await using var client = new GameplayTelemetryClient(() => true, recorder, box);
        // Finish the asynchronous startup seal before measuring the burst's chunk boundaries.
        recorder.Observe(Observation(0));
        var startupPacketId = recorder.Checkpoint().Draft!.PacketId;
        client.Start();
        await WaitUntilAsync(() => File.Exists(scope.Sealed(startupPacketId)));

        for (var revision = 1; revision <= 300; revision++)
        {
            recorder.Observe(Observation(revision));
            client.NotifyObservation();
        }
        var expectedRevisions = Enumerable.Range(1, 300).Select(revision => (long)revision).ToArray();
        (string Path, GameplayTelemetryPacket Packet)[] durableSnapshot = [];
        // Sealed chunks arrive before the final draft; retain the successful content snapshot.
        await WaitUntilAsync(() =>
        {
            (string Path, GameplayTelemetryPacket Packet)[] current;
            try
            {
                current = scope.ManagedFiles().Select(path =>
                {
                    // Inspect an atomic file generation without blocking its replacement or promotion.
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    return (Path: path, Packet: JsonSerializer.Deserialize<GameplayTelemetryPacket>(
                        stream, GameplayTelemetryWire.JsonOptions)!);
                }).ToArray();
            }
            catch (FileNotFoundException)
            {
                // Promotion can remove an enumerated draft before it is opened.
                return false;
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
            {
                // Windows sharing/lock violations during promotion are transient; all data assertions remain required.
                return false;
            }
            var revisions = current.SelectMany(file => file.Packet.Events)
                .Where(e => e.Kind == "observation" && e.RecognitionRevision != 0)
                .Select(e => e.RecognitionRevision).OrderBy(revision => revision).ToArray();
            if (current.Length < 5 || !revisions.SequenceEqual(expectedRevisions) ||
                !current.Any(file => file.Path.EndsWith(".draft.json", StringComparison.Ordinal) &&
                    file.Packet.Events.Any(e => e.Kind == "observation" && e.RecognitionRevision == 300)))
                return false;
            durableSnapshot = current;
            return true;
        });

        var files = durableSnapshot.Select(file => file.Path).ToArray();
        Assert.InRange(files.Length, 5, 8);
        Assert.True(files.Length < 300);
        Assert.Contains(files, path => path.EndsWith(".draft.json", StringComparison.Ordinal));
        Assert.Equal(expectedRevisions, durableSnapshot.SelectMany(file => file.Packet.Events)
            .Where(e => e.Kind == "observation" && e.RecognitionRevision != 0)
            .Select(e => e.RecognitionRevision).OrderBy(revision => revision).ToArray());
        await client.ShutdownAsync();
    }

    [Fact]
    public async Task CrashDraftRecoveryPreservesBytesAndIdentityWithoutInventingOutcome()
    {
        using var scope = new Scope();
        var posted = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options) return Accepted();
            var bytes = await request.Content!.ReadAsByteArrayAsync(token);
            posted.TrySetResult(bytes);
            return Ack(PacketId(bytes));
        });
        using var http = new HttpClient(handler);
        var packet = Packet(events: 3);
        var first = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        Assert.True(await first.SaveDraftAsync(packet));
        var draftBytes = await File.ReadAllBytesAsync(scope.Draft(packet.PacketId));

        var restarted = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        Assert.Equal(1, await restarted.RecoverDraftsAsync());
        Assert.False(File.Exists(scope.Draft(packet.PacketId)));
        Assert.Equal(draftBytes, await File.ReadAllBytesAsync(scope.Sealed(packet.PacketId)));
        await restarted.FlushAsync();

        var sent = await posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(draftBytes, sent);
        var recovered = JsonSerializer.Deserialize<GameplayTelemetryPacket>(sent, GameplayTelemetryWire.JsonOptions)!;
        Assert.Equal(packet.PacketId, recovered.PacketId);
        Assert.DoesNotContain(recovered.Events, e => e.Kind == "outcome");
    }

    [Fact]
    public async Task ShutdownPersistsTerminalBeforeBoundedTransport()
    {
        using var scope = new Scope();
        var received = new List<GameplayTelemetryEvent>();
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options) return Accepted();
            var packet = JsonSerializer.Deserialize<GameplayTelemetryPacket>(
                await request.Content!.ReadAsByteArrayAsync(token), GameplayTelemetryWire.JsonOptions)!;
            lock (received) received.AddRange(packet.Events);
            return Ack(packet.PacketId);
        });
        using var http = new HttpClient(handler);
        var recorder = Recorder();
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        await using var client = new GameplayTelemetryClient(() => true, recorder, box);
        client.Start();
        recorder.Observe(Observation(1));
        client.NotifyObservation();
        await client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(8));

        lock (received)
        {
            var terminal = Assert.Single(received, e => e.Kind == "outcome");
            Assert.Equal("interrupted", terminal.Outcome);
            Assert.Equal("appExit", terminal.OutcomeSource);
        }
    }

    [Fact]
    public async Task ConsentRevocationPreventsPromotionPostAndAcknowledgedDeletion()
    {
        using var scope = new Scope();
        var consent = true;
        var posts = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) Interlocked.Increment(ref posts);
            consent = false;
            return Task.FromResult(Accepted());
        });
        using var http = new HttpClient(handler);
        var packet = Packet();
        var box = new GameplayTelemetryOutbox(() => consent, scope.Root, http, Endpoint);
        Assert.True(await box.SaveDraftAsync(packet));
        consent = false;
        Assert.False(await box.EnqueueAsync(packet));
        await box.FlushAsync();
        Assert.Equal(0, posts);
        Assert.True(File.Exists(scope.Draft(packet.PacketId)));

        consent = true;
        Assert.Equal(1, await box.RecoverDraftsAsync());
        await box.FlushAsync();
        Assert.Equal(0, posts);
        Assert.True(File.Exists(scope.Sealed(packet.PacketId)));
    }

    [Fact]
    public async Task GenericSuccessAndWrongAcknowledgementPreserveImmutablePacket()
    {
        using var scope = new Scope();
        var calls = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Options) return Task.FromResult(Accepted());
            calls++;
            return Task.FromResult(calls == 1
                ? Ack(new string('0', 32))
                : new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        using var http = new HttpClient(handler);
        var packet = Packet();
        var bytes = GameplayTelemetryWire.Serialize(packet);
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        Assert.True(await box.EnqueueAsync(packet));
        await box.FlushAsync();
        Assert.Equal(bytes, await File.ReadAllBytesAsync(scope.Sealed(packet.PacketId)));
        await box.FlushAsync();
        Assert.Equal(bytes, await File.ReadAllBytesAsync(scope.Sealed(packet.PacketId)));
    }

    [Fact]
    public async Task CapacityRefusesNewPacketAndPreservesAllOldUnacknowledgedFiles()
    {
        using var scope = new Scope();
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No HTTP expected."));
        using var http = new HttpClient(handler);
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        var first = Packet();
        var firstBytes = GameplayTelemetryWire.Serialize(first);
        Assert.True(await box.EnqueueAsync(first));
        for (var i = 1; i < GameplayTelemetryOutbox.MaxPackets; i++) Assert.True(await box.EnqueueAsync(Packet()));
        var before = scope.SealedFiles().OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.False(await box.EnqueueAsync(Packet()));

        Assert.Equal(GameplayTelemetryOutbox.MaxPackets, scope.SealedFiles().Length);
        Assert.Equal(before, scope.SealedFiles().OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(scope.Sealed(first.PacketId)));
        Assert.True(box.CapacityRefusalCount >= 1);
        Assert.IsType<IOException>(box.LastError);
    }

    [Fact]
    public async Task ConcurrentFlushCallsNeverOverlapPosts()
    {
        using var scope = new Scope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var maximum = 0;
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options) return Accepted();
            var now = Interlocked.Increment(ref inFlight);
            maximum = Math.Max(maximum, now);
            var bytes = await request.Content!.ReadAsByteArrayAsync(token);
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            Interlocked.Decrement(ref inFlight);
            return Ack(PacketId(bytes));
        });
        using var http = new HttpClient(handler);
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        Assert.True(await box.EnqueueAsync(Packet()));
        var first = box.FlushAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = box.FlushAsync();
        release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, maximum);
    }

    [Fact]
    public async Task DraftPromotionAndRetryAreIdempotentAndCountAsOneLogicalPacket()
    {
        using var scope = new Scope();
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No HTTP expected."));
        using var http = new HttpClient(handler);
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        var partial = Packet(events: 1);
        var full = partial with { Events = Events(3) };
        Assert.True(await box.SaveDraftAsync(partial));
        Assert.True(await box.SaveDraftAsync(full));
        Assert.True(await box.EnqueueAsync(full));
        Assert.True(await box.EnqueueAsync(full));
        Assert.False(File.Exists(scope.Draft(full.PacketId)));
        Assert.Equal(GameplayTelemetryWire.Serialize(full), await File.ReadAllBytesAsync(scope.Sealed(full.PacketId)));
        Assert.Single(scope.ManagedFiles());

        var conflict = full with { ChunkIndex = full.ChunkIndex + 1 };
        Assert.False(await box.EnqueueAsync(conflict));
        Assert.Equal(GameplayTelemetryWire.Serialize(full), await File.ReadAllBytesAsync(scope.Sealed(full.PacketId)));
    }

    [Fact]
    public async Task FullStartupQueueStillStartsTransportAndResumesPersistence()
    {
        using var scope = new Scope();
        var sentIds = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options) return Accepted();
            var id = PacketId(await request.Content!.ReadAsByteArrayAsync(token));
            sentIds.TryAdd(id, 0);
            return Ack(id);
        });
        using var http = new HttpClient(handler);
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        for (var i = 0; i < GameplayTelemetryOutbox.MaxPackets; i++) Assert.True(await box.EnqueueAsync(Packet()));
        var recorder = Recorder(); recorder.Observe(Observation(1));
        var newId = recorder.Checkpoint().Draft!.PacketId;
        await using var client = new GameplayTelemetryClient(() => true, recorder, box);
        client.Start();
        await WaitUntilAsync(() => sentIds.ContainsKey(newId) && !client.IsBackpressured);
        Assert.Equal(GameplayTelemetryOutbox.MaxPackets + 1, sentIds.Count);
        Assert.Equal(0, client.PendingMemoryPacketCount);
    }

    [Fact]
    public async Task CrashAfterSealedWriteKeepsFullPacketAndRemovesItsShorterDraft()
    {
        using var scope = new Scope();
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No HTTP expected."));
        using var http = new HttpClient(handler);
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        var draft = Packet(events: 1);
        var full = draft with { Events = Events(3) };
        Assert.True(await box.SaveDraftAsync(draft));
        var fullBytes = GameplayTelemetryWire.Serialize(full);
        await File.WriteAllBytesAsync(scope.Sealed(full.PacketId), fullBytes);
        await box.RecoverDraftsAsync();
        Assert.False(File.Exists(scope.Draft(full.PacketId)));
        Assert.Equal(fullBytes, await File.ReadAllBytesAsync(scope.Sealed(full.PacketId)));
        Assert.Null(box.LastError);
    }

    [Fact]
    public async Task CompletedCrashStageExtendsDraftButIncompleteStageCannotReplaceIt()
    {
        using var scope = new Scope();
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No HTTP expected."));
        using var http = new HttpClient(handler);
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        var partial = Packet(events: 1); var full = partial with { Events = Events(3) };
        Assert.True(await box.SaveDraftAsync(partial));
        var bytes = GameplayTelemetryWire.Serialize(full);
        await File.WriteAllBytesAsync(scope.Draft(full.PacketId) + ".tmp", bytes);
        var incomplete = scope.Sealed(Guid.NewGuid().ToString("N")) + ".tmp";
        await File.WriteAllTextAsync(incomplete, "{");
        await box.RecoverDraftsAsync();
        Assert.Equal(bytes, await File.ReadAllBytesAsync(scope.Sealed(full.PacketId)));
        Assert.False(File.Exists(scope.Draft(full.PacketId) + ".tmp"));
        Assert.False(File.Exists(incomplete));
        Assert.True(box.ErrorCount > 0);
    }

    [Fact]
    public async Task UnownedTemporaryBytesCountTowardCapacityButAreNotDeleted()
    {
        using var scope = new Scope();
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No HTTP expected."));
        using var http = new HttpClient(handler);
        var packet = Packet(); Directory.CreateDirectory(Path.GetDirectoryName(scope.Sealed(packet.PacketId))!);
        var unknown = Path.Combine(Path.GetDirectoryName(scope.Sealed(packet.PacketId))!, "user-notes.tmp");
        using (var file = File.Create(unknown)) file.SetLength(GameplayTelemetryOutbox.MaxStorageBytes);
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        Assert.False(await box.EnqueueAsync(packet));
        Assert.True(File.Exists(unknown));
        Assert.True(box.CapacityRefusalCount > 0);
    }

    [Fact]
    public async Task ConsentChangedDuringAlreadyStartedPostPreservesPacketAndStopsLaterRequests()
    {
        using var scope = new Scope();
        var consent = true; var requests = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (request, token) =>
        {
            Interlocked.Increment(ref requests);
            if (request.Method == HttpMethod.Options) return Accepted();
            var id = PacketId(await request.Content!.ReadAsByteArrayAsync(token));
            entered.TrySetResult(); await release.Task.WaitAsync(token); return Ack(id);
        });
        using var http = new HttpClient(handler);
        var box = new GameplayTelemetryOutbox(() => Volatile.Read(ref consent), scope.Root, http, Endpoint);
        var packet = Packet(); Assert.True(await box.EnqueueAsync(packet));
        var flush = box.FlushAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Write(ref consent, false); release.TrySetResult(); await flush;
        Assert.True(File.Exists(scope.Sealed(packet.PacketId)));
        await box.FlushAsync(); Assert.Equal(2, requests);
    }

    [Fact]
    public async Task ShutdownRetriesRetainedMemoryAfterFinalTransportFreesSpace()
    {
        using var scope = new Scope();
        var online = false;
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options)
            {
                if (!Volatile.Read(ref online)) { rejected.TrySetResult(); return Rejected(); }
                return Accepted();
            }
            return Ack(PacketId(await request.Content!.ReadAsByteArrayAsync(token)));
        });
        using var http = new HttpClient(handler);
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        for (var i = 0; i < GameplayTelemetryOutbox.MaxPackets; i++) Assert.True(await box.EnqueueAsync(Packet()));
        var recorder = Recorder(); recorder.Observe(Observation(1));
        await using var client = new GameplayTelemetryClient(() => true, recorder, box); client.Start();
        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => client.IsBackpressured);
        Volatile.Write(ref online, true);
        await client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, client.PendingMemoryPacketCount);
        Assert.Empty(client.UnpersistedPacketsAfterShutdown());
        var events = scope.SealedFiles().SelectMany(file => JsonSerializer.Deserialize<GameplayTelemetryPacket>(
            File.ReadAllBytes(file), GameplayTelemetryWire.JsonOptions)!.Events);
        Assert.Contains(events, e => e.Kind == "outcome" && e.OutcomeSource == "appExit");
    }

    [Fact]
    public async Task OfflineFullQueueEscrowsExitTailAndRestoresItsExactBytesLater()
    {
        using var scope = new Scope();
        var online = false;
        var sent = new System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>();
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options) return Volatile.Read(ref online) ? Accepted() : Rejected();
            var bytes = await request.Content!.ReadAsByteArrayAsync(token); var id = PacketId(bytes);
            sent[id] = bytes; return Ack(id);
        });
        using var http = new HttpClient(handler);
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        for (var i = 0; i < GameplayTelemetryOutbox.MaxPackets; i++) Assert.True(await box.EnqueueAsync(Packet()));
        var recorder = Recorder(); recorder.Observe(Observation(1));
        await using var client = new GameplayTelemetryClient(() => true, recorder, box); client.Start();
        await WaitUntilAsync(() => client.IsBackpressured);
        await client.ShutdownAsync();
        var pending = client.UnpersistedPacketsAfterShutdown(); Assert.NotEmpty(pending);
        var expected = pending.ToDictionary(packet => packet.PacketId, GameplayTelemetryWire.Serialize);
        var journal = new GameplayRecoveryJournal(() => true, scope.Root, Catalog.Value);
        foreach (var packet in pending) Assert.True(journal.RecordPendingPacket(packet));
        Volatile.Write(ref online, true); await box.FlushAsync();
        var restarted = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        Assert.Equal(pending.Count, await restarted.RestorePendingPacketsAsync());
        await restarted.FlushAsync();
        foreach (var pair in expected) Assert.Equal(pair.Value, sent[pair.Key]);
        Assert.Empty(Directory.GetFiles(Path.Combine(scope.Root, "gameplay-v3", "recovery"), "*.packet.recovery.json"));
    }

    private static GameplaySessionRecorder Recorder() => new(() => true, Metadata, Catalog.Value);

    private static GameplayTelemetryObservation Observation(long revision) => new()
    {
        MatchGeneration = 1,
        RecognitionRevision = revision,
        Round = 20,
        CompletedStory = 9,
        Difficulty = "unknown",
        Inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:S20h", (int)(revision % 7 + 1)),
        RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e018", 2),
        Resources = ImmutableDictionary<string, long>.Empty.Add("gold", 100),
        GambleFailures = (int)revision
    };

    private static GameplayTelemetryPacket Packet(int events = 1) => new()
    {
        PacketId = Guid.NewGuid().ToString("N"),
        MatchId = Guid.NewGuid().ToString("N"),
        ChunkIndex = 0,
        AppVersion = Metadata.AppVersion,
        MapVersion = Metadata.MapVersion,
        MapScriptSha256 = Metadata.MapScriptSha256,
        ProfileVersion = Metadata.ProfileVersion,
        Difficulty = "unknown",
        Events = Events(events)
    };

    private static ImmutableArray<GameplayTelemetryEvent> Events(int count) => Enumerable.Range(1, count).Select(i => new GameplayTelemetryEvent
    {
        Sequence = i,
        RecognitionRevision = i,
        ElapsedMs = i,
        Round = 20,
        CompletedStory = 9,
        Mode = "Normal",
        GuideNumber = 0,
        Kind = "observation",
        Evidence = "native-observed",
        Inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:S20h", i),
        RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e018", 2),
        Resources = ImmutableDictionary<string, long>.Empty.Add("gold", 100)
    }).ToImmutableArray();

    private static string PacketId(byte[] bytes) => JsonDocument.Parse(bytes).RootElement.GetProperty("packetId").GetString()!;

    private static HttpResponseMessage Accepted()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add(TelemetryUploader.SchemaHeader, "3");
        response.Headers.Add(TelemetryUploader.AcceptedHeader, "true");
        response.Headers.Add(TelemetryUploader.EnabledHeader, "true");
        return response;
    }

    private static HttpResponseMessage Rejected() => new(HttpStatusCode.ServiceUnavailable);

    private static HttpResponseMessage Ack(string packetId)
    {
        var response = Accepted();
        response.Content = new StringContent(JsonSerializer.Serialize(new { schemaVersion = 3, packetId, accepted = true }));
        return response;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Durability condition was not reached.");
            await Task.Delay(20);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private sealed class Scope : IDisposable
    {
        public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "durability-storage", Guid.NewGuid().ToString("N"));
        public string Draft(string packetId) => Path.Combine(Root, "gameplay-v3", "pending", packetId + ".draft.json");
        public string Sealed(string packetId) => Path.Combine(Root, "gameplay-v3", "pending", packetId + ".v3.json");
        public string[] ManagedFiles() => Directory.Exists(Root)
            ? Directory.GetFiles(Root, "*.json", SearchOption.AllDirectories) : [];
        public string[] SealedFiles() => Directory.Exists(Root)
            ? Directory.GetFiles(Root, "*.v3.json", SearchOption.AllDirectories) : [];
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
