using Xunit;
using Scope = OrandOverlay.Tests.ObservedTelemetryClientTests.Scope;

namespace OrandOverlay.Tests;

public sealed class ObservedTelemetryTransportTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":3,\"accepted\":true}")]
    [InlineData("{\"schemaVersion\":4,\"schemaVersion\":4,\"accepted\":true}")]
    [InlineData("{\"schemaVersion\":4,\"accepted\":false}")]
    public async Task InvalidSuccessBodyDoesNotDeleteUnacknowledgedPacket(string receipt)
    {
        using var scope = new Scope();
        scope.Handler.Receipt = _ => receipt;
        await using var client = scope.Client();
        client.Record(new() { Kind = "recognition", State = "expired" });
        await client.FlushAsync();
        Assert.Single(scope.PendingFiles());
        Assert.Equal(0, client.AcceptedPacketCount);
    }

    [Fact]
    public async Task BlockedNetworkDoesNotBlockRecordOrBackgroundPersistence()
    {
        using var scope = new Scope();
        scope.Handler.NetworkBlock = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = scope.Client();
        client.Start();
        try
        {
            client.Record(new() { Kind = "recognition", State = "expired" });
            var blockedFlush = client.FlushAsync();
            var accepted = await Task.Run(() => client.Record(new() { Kind = "recognition", State = "fresh" })).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(accepted);
            Assert.False(blockedFlush.IsCompleted);
            scope.Handler.NetworkBlock.SetResult();
            await blockedFlush.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { scope.Handler.NetworkBlock.TrySetResult(); }
    }
}
