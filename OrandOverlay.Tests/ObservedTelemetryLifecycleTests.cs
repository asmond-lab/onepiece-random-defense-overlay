using Xunit;
using Scope = OrandOverlay.Tests.ObservedTelemetryClientTests.Scope;

namespace OrandOverlay.Tests;

public sealed class ObservedTelemetryLifecycleTests
{
    [Fact]
    public async Task StartAndStopEmitSessionLifecycleExactlyOnce()
    {
        using var scope = new Scope();
        await using var client = scope.Client();
        client.Start();
        client.Start();
        await client.StopAsync();
        await client.StopAsync();
        var events = scope.Handler.Packets.SelectMany(packet => packet.Events).OrderBy(value => value.Sequence).ToArray();
        Assert.Equal(new[] { "session-start", "app-exit" }, events.Select(value => value.State));
        Assert.False(client.Record(new() { Kind = "recognition", State = "fresh" }));
        Assert.Empty(scope.PendingFiles());
    }

    [Fact]
    public async Task DisposingNeverStartedClientDoesNotReadWriteOrSendPendingData()
    {
        using var scope = new Scope();
        await scope.Outbox().EnqueueAsync(ObservedTelemetryWireTests.Packet());
        var pending = Assert.Single(scope.PendingFiles());
        var bytes = await File.ReadAllBytesAsync(pending);
        await scope.Client().DisposeAsync();
        Assert.Equal(0, scope.Handler.Calls);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(pending));
    }

    [Fact]
    public async Task NewSessionStartUsesFreshIdentityAfterReset()
    {
        using var scope = new Scope();
        await using var client = scope.Client();
        client.Start();
        var previous = client.SessionId;
        client.Record(new() { Kind = "session", State = "session-reset" });
        client.ResetSession();
        await client.StopAsync();
        var old = scope.Handler.Packets.Where(packet => packet.SessionId == previous).SelectMany(packet => packet.Events).OrderBy(value => value.Sequence);
        var next = scope.Handler.Packets.Where(packet => packet.SessionId == client.SessionId).SelectMany(packet => packet.Events).OrderBy(value => value.Sequence);
        Assert.Equal(new[] { "session-start", "session-reset" }, old.Select(value => value.State));
        Assert.Equal(new[] { "session-start", "app-exit" }, next.Select(value => value.State));
    }
}
