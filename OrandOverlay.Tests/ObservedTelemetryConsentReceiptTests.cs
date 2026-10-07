using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ObservedTelemetryConsentReceiptTests
{
    [Fact]
    public async Task ConsentRevokedDuringSuccessfulRequestPreservesPacketUntilReconsentedRetry()
    {
        using var scope = new ObservedTelemetryClientTests.Scope();
        scope.Handler.Receipt = packet =>
        {
            scope.Allowed = false;
            return JsonSerializer.Serialize(new { schemaVersion = 4, packetId = packet.PacketId, accepted = true });
        };
        await using var client = scope.Client();
        client.Record(new() { Kind = "recognition", State = "expired" });
        await client.FlushAsync();
        Assert.Single(scope.PendingFiles());
        Assert.Equal(0, client.AcceptedPacketCount);
        scope.Handler.Receipt = null;
        scope.Allowed = true;
        await client.FlushAsync();
        Assert.Empty(scope.PendingFiles());
        Assert.Equal(1, client.AcceptedPacketCount);
        Assert.Equal(scope.Handler.Packets[0].PacketId, scope.Handler.Packets[1].PacketId);
    }
}
