using Xunit;
using Scope = OrandOverlay.Tests.ObservedTelemetryClientTests.Scope;

namespace OrandOverlay.Tests;

public sealed class ObservedTelemetryDurabilityTests
{
    private static ObservedTelemetryEvent State(string state = "scanning", string? lane = null) =>
        new() { Kind = "recognition", State = state, Lane = lane };

    [Fact]
    public async Task NoConsentMeansNoRecordNoDirectoryAndNoNetwork()
    {
        using var scope = new Scope { Allowed = false };
        await using var client = scope.Client();
        client.Start();
        Assert.False(client.Record(State()));
        await client.FlushAsync();
        Assert.Equal(0, scope.Handler.Calls);
        Assert.False(Directory.Exists(scope.Root));
    }

    [Fact]
    public async Task RevocationAfterCapturePreventsDiskAndHttp()
    {
        using var scope = new Scope();
        await using var client = scope.Client();
        Assert.True(client.Record(State()));
        scope.Allowed = false;
        await client.FlushAsync();
        Assert.Equal(0, scope.Handler.Calls);
        Assert.False(Directory.Exists(scope.Root));
    }

    [Fact]
    public async Task OfflinePacketsSurviveRestartAndKeepTheirOriginalSession()
    {
        using var scope = new Scope();
        scope.Handler.Offline = true;
        string original;
        await using (var first = scope.Client())
        {
            original = first.SessionId;
            first.Record(State("read-failed"));
            await first.FlushAsync();
            Assert.Single(scope.PendingFiles());
        }
        var bytes = await File.ReadAllBytesAsync(Assert.Single(scope.PendingFiles()));
        var durable = ObservedTelemetryWire.Deserialize(bytes);
        scope.Handler.Offline = false;
        await using var restarted = scope.Client();
        Assert.NotEqual(original, restarted.SessionId);
        restarted.Start();
        await restarted.FlushAsync();
        var accepted = Assert.Single(scope.Handler.Packets, packet => packet.SessionId == original);
        Assert.Equal(durable.PacketId, accepted.PacketId);
        Assert.Equal(original, accepted.SessionId);
        Assert.Empty(scope.PendingFiles());
    }

    [Fact]
    public async Task MismatchedAcknowledgementPreservesPacketUntilMatchingRetry()
    {
        using var scope = new Scope();
        scope.Handler.WrongReceipt = true;
        await using var client = scope.Client();
        client.Record(State());
        await client.FlushAsync();
        var path = Assert.Single(scope.PendingFiles());
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(0, client.AcceptedPacketCount);
        scope.Handler.WrongReceipt = false;
        await client.FlushAsync();
        Assert.Empty(scope.PendingFiles());
        Assert.Equal(1, client.AcceptedPacketCount);
        Assert.Equal(scope.Handler.Packets[0].PacketId, scope.Handler.Packets[1].PacketId);
        Assert.Equal(bytes, ObservedTelemetryWire.Serialize(scope.Handler.Packets[1]));
    }

    [Fact]
    public async Task DisabledServerCannotAcknowledgePendingRecords()
    {
        using var scope = new Scope();
        scope.Handler.Disabled = true;
        await using var client = scope.Client();
        client.Record(State());
        await client.FlushAsync();
        Assert.Single(scope.PendingFiles());
        Assert.Equal(0, client.AcceptedPacketCount);
        Assert.NotEmpty(scope.Handler.Packets);
    }

    [Fact]
    public async Task ResetKeepsExistingPacketsSeparateAndRestartsSequence()
    {
        using var scope = new Scope();
        await using var client = scope.Client();
        var oldSession = client.SessionId;
        client.Record(State("expired"));
        client.ResetSession();
        Assert.NotEqual(oldSession, client.SessionId);
        client.Record(State("fresh"));
        await client.FlushAsync();
        Assert.Equal(2, scope.Handler.Packets.Count);
        Assert.Equal("expired", Assert.Single(Assert.Single(scope.Handler.Packets, packet => packet.SessionId == oldSession).Events).State);
        Assert.Equal("fresh", Assert.Single(Assert.Single(scope.Handler.Packets, packet => packet.SessionId == client.SessionId).Events).State);
        Assert.All(scope.Handler.Packets, packet => Assert.Equal(1, Assert.Single(packet.Events).Sequence));
    }

    [Fact]
    public async Task DedupIgnoresTimingNoiseButPreservesLaneAndStateTransitions()
    {
        using var scope = new Scope();
        await using var client = scope.Client();
        Assert.True(client.Record(State("fresh", "basic") with { DurationMs = 12 }));
        Assert.False(client.Record(State("fresh", "basic") with { DurationMs = 16, SourceRevision = 99 }));
        Assert.True(client.Record(State("fresh", "full")));
        Assert.True(client.Record(State("expired", "presentation")));
        Assert.True(client.Record(State("fresh", "presentation")));
        await client.FlushAsync();
        Assert.Equal(4, scope.Handler.Packets.SelectMany(packet => packet.Events).Count());
    }

    [Fact]
    public async Task MemoryBackpressureNeverEvictsPreviouslyAcceptedRecords()
    {
        using var scope = new Scope();
        await using var client = scope.Client();
        for (var index = 0; index < ObservedTelemetryClient.MaxPendingMemoryPackets; index++)
            Assert.True(client.Record(State(index % 2 == 0 ? "fresh" : "expired")));
        Assert.False(client.Record(State("game-unavailable")));
        Assert.True(client.IsBackpressured);
        Assert.Equal(ObservedTelemetryClient.MaxPendingMemoryPackets, client.PendingMemoryPacketCount);
        await client.FlushAsync();
        Assert.Equal(ObservedTelemetryClient.MaxPendingMemoryPackets, scope.Handler.Packets.SelectMany(packet => packet.Events).Count());
        Assert.False(client.IsBackpressured);
    }
}
