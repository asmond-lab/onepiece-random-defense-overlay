using System.Net;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ObservedTelemetryTimingTests
{
    [Fact]
    public async Task SameStateHasThirtySecondHeartbeatAndSessionClockRestarts()
    {
        using var scope = new ObservedTelemetryClientTests.Scope();
        var clock = new Clock();
        await using var client = new ObservedTelemetryClient(() => true,
            new("1.0.2-test.9", new string('a', 64), Source: "synthetic-validation"), scope.Outbox(), clock);
        var value = new ObservedTelemetryEvent { Kind = "recognition", State = "fresh" };
        Assert.True(client.Record(value));
        clock.Now += TimeSpan.FromSeconds(29);
        Assert.False(client.Record(value));
        clock.Now += TimeSpan.FromSeconds(1);
        Assert.True(client.Record(value));
        await client.FlushAsync();
        Assert.Equal(new long[] { 0, 30000 }, scope.Handler.Packets.SelectMany(packet => packet.Events).OrderBy(item => item.Sequence).Select(item => item.ElapsedMs));
        client.ResetSession();
        Assert.True(client.Record(value));
        await client.FlushAsync();
        Assert.Equal(0, scope.Handler.Packets.Last().Events[0].ElapsedMs);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
