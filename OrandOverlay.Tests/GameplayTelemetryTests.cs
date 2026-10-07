using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GameplayTelemetryTests
{
    private static readonly GameplayTelemetryMetadata Metadata = new("0.6.70", "2.314", new string('a', 64), "1");
    private static readonly Lazy<DataCatalog> Catalog = new(() => { var c = new DataCatalog(); c.Load(false); return c; });
    private static GameplaySessionRecorder Recorder(Func<bool>? permission = null, TimeProvider? time = null) => new(permission ?? (() => true), Metadata, Catalog.Value, time);
    private static ImmutableDictionary<string, int> Inventory(params (string Id, int Count)[] entries) => entries.ToImmutableDictionary(x => x.Id, x => x.Count);
    private static GameplayTelemetryObservation Observation(long revision = 1) => new()
    {
        MatchGeneration = 1, RecognitionRevision = revision, Round = 20, CompletedStory = 9,
        Inventory = Inventory(("rawcode:S20h", 2)), RewardWisps = Inventory(("e018", 2)),
        Resources = ImmutableDictionary<string, long>.Empty.Add("gold", 100), Difficulty = "악몽"
    };
    private static GameplayTelemetryPacket Packet()
    {
        var r = Recorder(); r.Observe(Observation()); return Assert.Single(r.DrainPackets());
    }
    [Fact]
    public void RawcodeInventoryIsPreservedAndUnknownIdentifiersAreExcluded()
    {
        var r = Recorder();
        r.Observe(Observation() with { Inventory = Observation().Inventory.Add("player:private", 1).Add("C:\\private", 1) });
        var e = Assert.Single(Assert.Single(r.DrainPackets()).Events);
        Assert.Equal(2, e.Inventory!["rawcode:S20h"]);
        Assert.Single(e.Inventory);
    }
    [Theory]
    [InlineData(PlayMode.Normal)] [InlineData(PlayMode.Manual)] [InlineData(PlayMode.Beginner)] [InlineData(PlayMode.Guide)]
    public void DuplicateStaleAndUnchangedFramesAreFencedButRelevantTransitionsRemain(PlayMode mode)
    {
        var r = Recorder(); var first = Observation() with { Mode = mode };
        r.Observe(first); r.Observe(first); r.Observe(first with { RecognitionRevision = 0, Round = 1 });
        r.Observe(first with { RecognitionRevision = 2 });
        r.Observe(first with { RecognitionRevision = 3, Round = 21, GambleFailures = 4 });
        r.Observe(first with { RecognitionRevision = 4, IsCurrent = false, Inventory = Inventory() });
        var events = r.DrainPackets().SelectMany(p => p.Events).ToArray();
        Assert.Equal(2, events.Length); Assert.Equal(new long[] { 1, 2 }, events.Select(e => e.Sequence));
        Assert.Equal(3, events[1].RecognitionRevision); Assert.Equal(4, events[1].GambleFailures);
        Assert.All(events, e => Assert.Equal(mode.ToString(), e.Mode));
        Assert.DoesNotContain(events, e => e.Kind == "gamble");
    }
    [Fact]
    public void OutcomeOnceUsesLastGoodRosterAndResetCreatesRandomFragment()
    {
        var r = Recorder(); r.Observe(Observation());
        r.Observe(Observation(2) with { Inventory = Inventory() });
        r.Complete("fail", "unitWipe"); r.Complete("clear", "mapSettlement");
        var first = r.DrainPackets(); var terminal = Assert.Single(first.SelectMany(p => p.Events), e => e.Kind == "outcome");
        Assert.Equal("fail", terminal.Outcome); Assert.Equal(2, terminal.Inventory!["rawcode:S20h"]);
        r.Observe(Observation(3)); Assert.Empty(r.DrainPackets());
        r.Reset(); r.Observe(Observation() with { MatchGeneration = 2 });
        var second = Assert.Single(r.DrainPackets());
        Assert.NotEqual(first[0].MatchId, second.MatchId); Assert.Equal(1, second.Events[0].Sequence);
        r.Observe(Observation(99) with { MatchGeneration = 1 }); Assert.Empty(r.DrainPackets());
    }
    [Fact]
    public void GenerationChangeInterruptsPriorFragmentWithoutMixingEvents()
    {
        var r = Recorder(); r.Observe(Observation());
        r.Observe(Observation() with { MatchGeneration = 2 });
        var packets = r.DrainPackets(); Assert.Equal(2, packets.Length);
        Assert.Equal("interrupted", packets[0].Events.Last().Outcome);
        Assert.NotEqual(packets[0].MatchId, packets[1].MatchId);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifficultyDiscoveryCreatesANewImmutableFragment(bool alreadyQueued)
    {
        var recorder = Recorder();
        recorder.Observe(Observation() with { Difficulty = "unknown" });
        var packets = new List<GameplayTelemetryPacket>();
        if (alreadyQueued) packets.AddRange(recorder.DrainPackets());
        recorder.Observe(Observation(2));
        packets.AddRange(recorder.DrainPackets());
        var fragments = packets.GroupBy(packet => packet.MatchId).ToArray();
        Assert.Equal(2, fragments.Length);
        Assert.All(fragments, fragment => Assert.Single(fragment.Select(packet => packet.Difficulty).Distinct()));
        var previous = Assert.Single(fragments.Where(fragment => fragment.First().Difficulty == "unknown"));
        Assert.Equal("interrupted", previous.SelectMany(packet => packet.Events).Last().Outcome);
        var current = Assert.Single(fragments.Where(fragment => fragment.First().Difficulty == "악몽"));
        Assert.Equal(0, current.First().ChunkIndex);
        Assert.Equal(1, current.First().Events[0].Sequence);
        Assert.Equal(2, current.First().Events[0].RecognitionRevision);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualCraftReceiptDoesNotDependOnRecommendationAndSupportsBothReadOrders(bool outputFirst)
    {
        var unit = Catalog.Value.AllUnits.First(u => u.Recipe.Count > 0 &&
            u.Recipe.All(p => p.Value > 0 && Catalog.Value.Unit(p.Key).Tier != "자원") && !u.Recipe.ContainsKey(u.Id));
        var ingredients = unit.Recipe.ToImmutableDictionary();
        var before = Observation() with { Inventory = ingredients, RewardWisps = Inventory() };
        var r = Recorder(); r.Observe(before);
        r.Recommend(Frame(before), new(CoachActionKind.Waiting, "private", "private", "private", "private", "private", "private"));
        r.Observe(before with { RecognitionRevision = 2, Inventory = outputFirst ? ingredients.Add(unit.Id, 1) : Inventory() });
        r.Observe(before with { RecognitionRevision = 3, Inventory = Inventory((unit.Id, 1)) });
        var events = r.DrainPackets().SelectMany(p => p.Events).ToArray();
        var craft = Assert.Single(events, e => e.Kind == "craft");
        Assert.Equal(unit.Id, craft.UnitId); Assert.Equal("inventory-matched", craft.Evidence);
        Assert.Equal(3, events.Count(e => e.Kind == "observation"));
        Assert.Single(events, e => e.Kind == "recommendation");
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelectionReceiptUsesActualCountsAcrossEitherObservationOrder(bool outputFirst)
    {
        var unit = Catalog.Value.AllUnits.First(u => TopGradePolicy.BaseTier(u.Tier) == "흔함");
        var before = Observation() with { Inventory = Inventory(), RewardWisps = Inventory(("e018", 2)) };
        var r = Recorder(); r.Observe(before);
        r.Observe(before with { RecognitionRevision = 2, Inventory = outputFirst ? Inventory((unit.Id, 2)) : Inventory(), RewardWisps = Inventory(("e018", outputFirst ? 2 : 0)) });
        r.Observe(before with { RecognitionRevision = 3, Inventory = Inventory((unit.Id, 2)), RewardWisps = Inventory(("e018", 0)) });
        var selection = Assert.Single(r.DrainPackets().SelectMany(p => p.Events), e => e.Kind == "selection");
        Assert.Equal(2, selection.Count); Assert.Equal(2, selection.Outputs![unit.Id]);
    }
    [Theory]
    [InlineData("e0IX")] [InlineData("e01A")]
    public void RandomAndTranscendenceWispCountsRemainObservationsWithoutSelectionReceipts(string spentWisp)
    {
        var before = Observation() with { Inventory = Inventory(), RewardWisps = Inventory(("e0IX", 2), ("e01A", 3)) };
        var r = Recorder(); r.Observe(before);
        r.Observe(before with { RecognitionRevision = 2, RewardWisps = before.RewardWisps.SetItem(spentWisp, before.RewardWisps[spentWisp] - 1) });
        r.Observe(before with { RecognitionRevision = 3, RewardWisps = before.RewardWisps.SetItem(spentWisp, before.RewardWisps[spentWisp] - 1), Inventory = Inventory(("rawcode:S20h", 1)) });
        var packet = Assert.Single(r.DrainPackets());
        Assert.Equal(3, packet.Events.Length);
        Assert.All(packet.Events, e => { Assert.Equal("observation", e.Kind); Assert.Null(e.Outputs); });
        Assert.Equal(2, packet.Events[0].RewardWisps!["e0IX"]);
        Assert.Equal(3, packet.Events[0].RewardWisps!["e01A"]);
        Assert.Equal(before.RewardWisps[spentWisp] - 1, packet.Events[1].RewardWisps![spentWisp]);
        var bytes = GameplayTelemetryWire.Serialize(packet);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(2, json.RootElement.GetProperty("events")[0].GetProperty("rewardWisps").GetProperty("e0IX").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("events")[0].GetProperty("rewardWisps").GetProperty("e01A").GetInt32());
        Assert.True(GameplayTelemetryWire.IsSafe(bytes));
    }
    [Fact]
    public void UncertainAcquisitionRetainsFactsWithoutInventingReceipt()
    {
        var r = Recorder(); r.Observe(Observation());
        r.Observe(Observation(2) with { Inventory = Observation().Inventory.SetItem("rawcode:S20h", 7), GambleFailures = 3 });
        var events = r.DrainPackets().SelectMany(p => p.Events).ToArray();
        Assert.Equal(2, events.Length); Assert.All(events, e => Assert.Equal("observation", e.Kind));
        Assert.Equal(7, events[1].Inventory!["rawcode:S20h"]);
    }
    [Fact]
    public void PausedHiddenCoachCannotStopObservationsOrTurnAdviceIntoActions()
    {
        var r = Recorder(); var observation = Observation(); r.Observe(observation);
        r.Recommend(Frame(observation) with { Paused = true, GuideVisible = false }, new(CoachActionKind.Craft, "", "", "", "", "", ""));
        Assert.Equal("observation", Assert.Single(Assert.Single(r.DrainPackets()).Events).Kind);
    }
    [Fact]
    public void ChunkCountBytesAndSequencesAreBoundedAndImmutable()
    {
        var r = Recorder();
        for (var i = 1; i <= 140; i++) r.Observe(Observation(i) with { GambleFailures = i });
        var packets = r.DrainPackets(); Assert.Equal(3, packets.Length);
        Assert.Equal(new[] { 64, 64, 12 }, packets.Select(p => p.Events.Length));
        Assert.All(packets, p => Assert.InRange(GameplayTelemetryWire.Serialize(p).Length, 1, GameplayTelemetryWire.MaxBytes));
        Assert.Equal(Enumerable.Range(1, 140).Select(x => (long)x), packets.SelectMany(p => p.Events).Select(e => e.Sequence));
        var saved = GameplayTelemetryWire.Serialize(packets[0]); r.Observe(Observation(141));
        Assert.Equal(saved, GameplayTelemetryWire.Serialize(packets[0]));
    }
    [Fact]
    public void PrivacyRejectsExtraFieldsEnumsAndPolicyMismatch()
    {
        var packet = Packet(); var json = Encoding.UTF8.GetString(GameplayTelemetryWire.Serialize(packet));
        Assert.False(GameplayTelemetryWire.IsSafe(Encoding.UTF8.GetBytes(json[..^1] + ",\"nickname\":\"private\"}")));
        Assert.False(GameplayTelemetryWire.IsSafe(JsonSerializer.SerializeToUtf8Bytes(packet with { ConsentVersion = 2 }, GameplayTelemetryWire.JsonOptions)));
        Assert.False(GameplayTelemetryWire.IsSafe(JsonSerializer.SerializeToUtf8Bytes(packet with { Events = [packet.Events[0] with { Mode = "other" }] }, GameplayTelemetryWire.JsonOptions)));
        Assert.False(GameplayTelemetryWire.IsSafe(Encoding.UTF8.GetBytes(json.Replace("\"schemaVersion\":3", "\"schemaVersion\":3,\"schemaVersion\":3"))));
    }
    [Fact]
    public async Task NoConsentHasNoDiskOrNetworkEffectsAndRevocationClearsMemory()
    {
        using var scope = new Scope(); bool consent = false;
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Unexpected HTTP"));
        using var http = new HttpClient(handler);
        var r = Recorder(() => consent); var box = new GameplayTelemetryOutbox(() => consent, scope.Root, http, Endpoint);
        r.Observe(Observation()); Assert.Empty(r.DrainPackets());
        Assert.False(await box.EnqueueAsync(Packet())); await box.FlushAsync(); Assert.False(Directory.Exists(scope.Root));
        consent = true; r.Observe(Observation()); consent = false; Assert.Empty(r.DrainPackets());
        consent = true; Assert.Empty(r.DrainPackets());
    }
    [Fact]
    public async Task OfflineRetryUsesExactIdentityAndOnlyMatchingAcknowledgementDeletes()
    {
        using var scope = new Scope(); var bodies = new List<byte[]>(); int posts = 0;
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options) return Accepted();
            var bytes = await request.Content!.ReadAsByteArrayAsync(token); bodies.Add(bytes); posts++;
            if (posts == 1) throw new HttpRequestException("offline fixture");
            return Ack(posts == 2 ? new string('0', 32) : JsonDocument.Parse(bytes).RootElement.GetProperty("packetId").GetString()!);
        });
        using var http = new HttpClient(handler); var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        var packet = Packet(); Assert.True(await box.EnqueueAsync(packet));
        await box.FlushAsync(); Assert.Single(scope.Files());
        await box.FlushAsync(); Assert.Single(scope.Files());
        await box.FlushAsync(); Assert.Empty(scope.Files());
        Assert.Equal(3, bodies.Count); Assert.All(bodies, b => Assert.Equal(GameplayTelemetryWire.Serialize(packet), b));
    }
    [Fact]
    public async Task DisabledHandshakeAndConsentRecheckPreventPost()
    {
        using var scope = new Scope(); bool consent = true; var methods = new List<HttpMethod>();
        using var handler = new Handler((request, _) =>
        {
            methods.Add(request.Method); consent = false; return Task.FromResult(Accepted());
        });
        using var http = new HttpClient(handler); var box = new GameplayTelemetryOutbox(() => consent, scope.Root, http, Endpoint);
        Assert.True(await box.EnqueueAsync(Packet())); await box.FlushAsync();
        Assert.Equal(new[] { HttpMethod.Options }, methods); Assert.Single(scope.Files());
    }
    [Theory]
    [InlineData("false")] [InlineData("")]
    public async Task ExplicitEnabledHeaderRequired(string enabled)
    {
        using var scope = new Scope(); int posts = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) posts++;
            var response = Accepted(); response.Headers.Remove(TelemetryUploader.EnabledHeader);
            if (enabled.Length > 0) response.Headers.Add(TelemetryUploader.EnabledHeader, enabled);
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler); var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        await box.EnqueueAsync(Packet()); await box.FlushAsync(); Assert.Equal(0, posts); Assert.Single(scope.Files());
    }
    [Fact]
    public async Task CancellationPreservesPacketAndConcurrentFlushesAreSerialized()
    {
        using var scope = new Scope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource(); int posts = 0;
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options) return Accepted();
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => cancelled.TrySetCanceled(token));
            Interlocked.Increment(ref posts); entered.TrySetResult();
            await cancelled.Task; return Accepted();
        });
        using var http = new HttpClient(handler); var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        await box.EnqueueAsync(Packet());
        var first = box.FlushAsync(cancel.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = box.FlushAsync(cancel.Token); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(1, posts); Assert.Single(scope.Files());
    }
    [Fact]
    public async Task RestartLoadsDurablePacketsAndThirtyDayRetentionExpiresThem()
    {
        using var scope = new Scope(); var time = new Clock(); int posts = 0;
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options) return Accepted();
            posts++; using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token));
            return Ack(body.RootElement.GetProperty("packetId").GetString()!);
        });
        using var http = new HttpClient(handler);
        var first = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint, time);
        await first.EnqueueAsync(Packet());
        var restarted = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint, time);
        await restarted.FlushAsync(); Assert.Equal(1, posts); Assert.Empty(scope.Files());
        await restarted.EnqueueAsync(Packet()); time.Advance(TimeSpan.FromDays(31));
        await restarted.FlushAsync(); Assert.Empty(scope.Files()); Assert.Equal(1, posts);
    }
    [Fact]
    public async Task ClientStartsAutomaticallyAndShutdownPersistsInterruptedOutcome()
    {
        using var scope = new Scope();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<GameplayTelemetryEvent>();
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options) return Accepted();
            var packet = JsonSerializer.Deserialize<GameplayTelemetryPacket>(await request.Content!.ReadAsByteArrayAsync(token), GameplayTelemetryWire.JsonOptions)!;
            lock (events) events.AddRange(packet.Events);
            received.TrySetResult(); return Ack(packet.PacketId);
        });
        using var http = new HttpClient(handler); var r = Recorder(); r.Observe(Observation());
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        await using var client = new GameplayTelemetryClient(() => true, r, box);
        client.Start(); await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await client.ShutdownAsync();
        var terminal = Assert.Single(events, e => e.Kind == "outcome");
        Assert.Equal("interrupted", terminal.Outcome); Assert.Equal("appExit", terminal.OutcomeSource);
    }
    [Fact]
    public void GambleCounterTransitionsAreSnapshotsNotAttemptReceipts()
    {
        var r = Recorder(); var first = Observation(); r.Observe(first);
        var counters = ImmutableDictionary<string, GameplayTelemetryGambleCounter>.Empty.Add("middle", new(4, 1, 3));
        r.Observe(first with { RecognitionRevision = 2, GambleCounters = counters });
        r.Observe(first with { RecognitionRevision = 3, GambleCounters = counters });
        r.Observe(first with { RecognitionRevision = 4, GambleCounters = counters.SetItem("middle", new(5, 2, 3)) });
        var packet = Assert.Single(r.DrainPackets());
        Assert.Equal(3, packet.Events.Length); Assert.Null(packet.Events[0].GambleCounters);
        Assert.Equal(5, packet.Events[2].GambleCounters!["middle"].Attempts);
        Assert.All(packet.Events, e => { Assert.Equal("observation", e.Kind); Assert.Null(e.Cost); Assert.Null(e.Outputs); });
        Assert.True(GameplayTelemetryWire.IsSafe(GameplayTelemetryWire.Serialize(packet)));
    }
    [Theory]
    [InlineData("middle", 4, 1, 2)] [InlineData("medium", 3, 1, 2)]
    [InlineData("low", -1, 0, -1)] [InlineData("high", 0, int.MaxValue, int.MaxValue)]
    public void InvalidGambleCounterSchemaIsRejected(string type, int attempts, int successes, int failures)
    {
        var packet = Packet();
        var invalid = packet with { Events = [packet.Events[0] with
        {
            GambleCounters = ImmutableDictionary<string, GameplayTelemetryGambleCounter>.Empty.Add(type, new(attempts, successes, failures))
        }] };
        Assert.False(GameplayTelemetryWire.IsSafe(JsonSerializer.SerializeToUtf8Bytes(invalid, GameplayTelemetryWire.JsonOptions)));
    }
    [Fact]
    public void DifficultyTransitionSealsPriorCohort()
    {
        var r = Recorder(); r.Observe(Observation() with { Difficulty = "unknown" }); r.Observe(Observation(2));
        var packets = r.DrainPackets(); Assert.Equal(2, packets.Length);
        Assert.Equal("unknown", packets[0].Difficulty); Assert.Equal("악몽", packets[1].Difficulty);
    }
    [Fact]
    public void LargeSnapshotsSplitBeforeByteLimit()
    {
        var inventory = Catalog.Value.AllUnits.Take(512).ToImmutableDictionary(u => u.Id, _ => int.MaxValue);
        var r = Recorder();
        for (int i = 1; i <= 64; i++) r.Observe(Observation(i) with { Inventory = inventory, GambleFailures = i });
        var packets = r.DrainPackets(); Assert.True(packets.Length > 1);
        Assert.Equal(64, packets.Sum(p => p.Events.Length));
        Assert.All(packets, p => Assert.InRange(GameplayTelemetryWire.Serialize(p).Length, 1, GameplayTelemetryWire.MaxBytes));
    }
    [Fact]
    public void SharedGoldenFixtureRoundTripsFrozenWire()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "fixtures", "gameplay-v3-golden.json"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var bytes = File.ReadAllBytes(Path.Combine(directory.FullName, "docs", "fixtures", "gameplay-v3-golden.json"));
        Assert.True(GameplayTelemetryWire.IsSafe(bytes));
        var packet = JsonSerializer.Deserialize<GameplayTelemetryPacket>(bytes, GameplayTelemetryWire.JsonOptions)!;
        Assert.True(GameplayTelemetryWire.IsSafe(GameplayTelemetryWire.Serialize(packet)));
    }
    [Fact]
    public async Task PacketIdentityConflictDoesNotReplaceDurableContentAndStorageIsBounded()
    {
        using var scope = new Scope();
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No HTTP expected"));
        using var http = new HttpClient(handler); var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        var packet = Packet(); Assert.True(await box.EnqueueAsync(packet));
        Assert.False(await box.EnqueueAsync(packet with { ChunkIndex = 5 }));
        Assert.Equal(GameplayTelemetryWire.Serialize(packet), File.ReadAllBytes(Assert.Single(scope.Files())));
        for (var i = 1; i < GameplayTelemetryOutbox.MaxPackets; i++) Assert.True(await box.EnqueueAsync(Packet()));
        Assert.False(await box.EnqueueAsync(Packet()));
        Assert.Equal(GameplayTelemetryOutbox.MaxPackets, scope.Files().Length);
        // Capacity must reject new input rather than silently evict an unacknowledged early chunk.
        Assert.Contains(scope.Files(), file => File.ReadAllBytes(file).AsSpan()
            .SequenceEqual(GameplayTelemetryWire.Serialize(packet)));
        Assert.True(scope.Files().Sum(p => new FileInfo(p).Length) <= GameplayTelemetryOutbox.MaxStorageBytes);
        Assert.Empty(Directory.GetFiles(scope.Root, "*.tmp", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task PeriodicTickRetriesOfflinePacketWithoutAnotherObservation()
    {
        using var scope = new Scope(); var time = new TickClock();
        var offline = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int posts = 0;
        using var handler = new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Options) return Accepted();
            if (Interlocked.Increment(ref posts) == 1) { offline.TrySetResult(); throw new HttpRequestException("offline fixture"); }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token));
            retried.TrySetResult(); return Ack(body.RootElement.GetProperty("packetId").GetString()!);
        });
        using var http = new HttpClient(handler); var r = Recorder(); r.Observe(Observation());
        var box = new GameplayTelemetryOutbox(() => true, scope.Root, http, Endpoint);
        await using var client = new GameplayTelemetryClient(() => true, r, box, time);
        client.Start();
        await Task.WhenAll(offline.Task, time.Created.Task).WaitAsync(TimeSpan.FromSeconds(10));
        time.Tick();
        await retried.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(Volatile.Read(ref posts) >= 2);
    }
    private static CoachFrame Frame(GameplayTelemetryObservation o) => new()
    {
        MatchGeneration = o.MatchGeneration, Revision = o.RecognitionRevision, RecognitionRevision = o.RecognitionRevision,
        Round = o.Round, CompletedStoryStage = o.CompletedStory, IsCurrent = true, Inventory = o.Inventory
    };
    private static Uri Endpoint => new("https://fixture.invalid/v3/gameplay");
    private static HttpResponseMessage Accepted()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add(TelemetryUploader.SchemaHeader, "3"); response.Headers.Add(TelemetryUploader.AcceptedHeader, "true");
        response.Headers.Add(TelemetryUploader.EnabledHeader, "true"); return response;
    }
    private static HttpResponseMessage Ack(string packetId)
    {
        var response = Accepted(); response.Content = new StringContent(JsonSerializer.Serialize(new { schemaVersion = 3, packetId, accepted = true })); return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }
    private sealed class Scope : IDisposable
    {
        public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "fixture-storage", Guid.NewGuid().ToString("N"));
        public string[] Files() => Directory.Exists(Root) ? Directory.GetFiles(Root, "*.v3.json", SearchOption.AllDirectories) : [];
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class TickClock : TimeProvider
    {
        public TaskCompletionSource Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TickTimer? _timer;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timer = new TickTimer(callback, state); Created.TrySetResult(); return _timer;
        }
        public void Tick() => _timer!.Fire();
        private sealed class TickTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddDays(20000);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
