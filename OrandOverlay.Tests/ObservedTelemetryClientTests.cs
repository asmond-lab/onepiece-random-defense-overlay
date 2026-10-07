using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ObservedTelemetryClientTests
{
    [Fact]
    public async Task FreshInventoryAndRecognitionGapAreDurablySentAfterConsent()
    {
        using var scope = new Scope();
        await using var client = scope.Client();
        client.Start();
        Assert.True(client.Record(new() { Kind = "inventory", State = "fresh", Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy", 2) }));
        Assert.True(client.Record(new() { Kind = "recognition", State = "read-failed", ReasonCode = "read-error" }));
        await client.FlushAsync();
        var events = scope.Handler.Packets.SelectMany(packet => packet.Events).Where(value => value.Kind != "session").OrderBy(value => value.Sequence).ToArray();
        Assert.Equal(new[] { "fresh", "read-failed" }, events.Select(value => value.State));
        Assert.True(client.AcceptedPacketCount > 0);
        Assert.Empty(scope.PendingFiles());
    }

    internal sealed class Scope : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "randypick-observed-tests", Guid.NewGuid().ToString("N"));
        internal bool Allowed = true;
        internal Handler Handler { get; } = new();
        private readonly HttpClient _http;
        internal Scope() { _http = new HttpClient(Handler); }
        internal ObservedTelemetryOutbox Outbox() => new(() => Allowed, Root, _http);
        internal ObservedTelemetryClient Client() => new(() => Allowed, new("1.0.2-test.9", new string('a', 64), Source: "synthetic-validation"), Outbox());
        internal string[] PendingFiles() => Directory.Exists(Root) ? Directory.GetFiles(Root, "*.json", SearchOption.AllDirectories) : [];
        public void Dispose() { _http.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    internal sealed class Handler : HttpMessageHandler
    {
        internal List<ObservedTelemetryPacket> Packets { get; } = [];
        internal int Calls;
        internal bool Offline, WrongReceipt, Disabled;
        internal Func<ObservedTelemetryPacket, string>? Receipt;
        internal TaskCompletionSource? NetworkBlock;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            if (NetworkBlock is not null) await NetworkBlock.Task.WaitAsync(token);
            if (Offline) throw new HttpRequestException("Offline fixture.");
            var response = new HttpResponseMessage(request.Method == HttpMethod.Options ? HttpStatusCode.NoContent : HttpStatusCode.OK);
            response.Headers.Add("X-Orand-Telemetry-Schema", "4");
            response.Headers.Add("X-Orand-Telemetry-Accepted", "true");
            response.Headers.Add("X-Orand-Telemetry-Enabled", Disabled ? "false" : "true");
            if (request.Method != HttpMethod.Options)
            {
                var packet = ObservedTelemetryWire.Deserialize(await request.Content!.ReadAsByteArrayAsync(token));
                Packets.Add(packet);
                response.Content = new StringContent(Receipt?.Invoke(packet) ?? JsonSerializer.Serialize(new { schemaVersion = 4, packetId = WrongReceipt ? new string('f', 32) : packet.PacketId, accepted = true }));
            }
            return response;
        }
    }
}
