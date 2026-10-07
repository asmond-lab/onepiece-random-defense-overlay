using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace OrandOverlay;

public sealed partial class ObservedTelemetryOutbox
{
    public async Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!_permission()) return 0;
        await _transportGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] paths;
            await _diskGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_permission() || !Directory.Exists(_directory)) return 0;
                ExpireOwnedFiles();
                cancellationToken.ThrowIfCancellationRequested();
                if (!_permission()) return 0;
                await SealActiveAsync(cancellationToken).ConfigureAwait(false);
                paths = Directory.GetFiles(_directory, "*.json").OrderBy(File.GetLastWriteTimeUtc).ToArray();
            }
            finally { _diskGate.Release(); }
            if (paths.Length == 0 || !_permission()) return 0;
            var acknowledged = 0;
            foreach (var path in paths)
            {
                if (!_permission()) break;
                try
                {
                    var bytes = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
                    var packet = ObservedTelemetryWire.Deserialize(bytes);
                    if (!Path.GetFileName(path).Equals(packet.PacketId + ".json", StringComparison.Ordinal))
                        throw new InvalidDataException("Telemetry filename identity mismatch.");
                    using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
                    { Content = new ByteArrayContent(bytes) };
                    request.Content.Headers.ContentType = new("application/json");
                    using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (response.StatusCode != HttpStatusCode.OK || !AcceptedHeaders(response) ||
                        !await HasMatchingReceiptAsync(response, packet.PacketId, cancellationToken).ConfigureAwait(false))
                        throw new HttpRequestException("Observed telemetry receipt was not accepted.");
      
                    await _diskGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try { if (!_permission()) return acknowledged; File.Delete(path); }
                    finally { _diskGate.Release(); }
                    acknowledged++;
                    Interlocked.Increment(ref _acceptedPacketCount);
                    Interlocked.Exchange(ref _lastAcknowledgedUtcTicks, _time.GetUtcNow().UtcDateTime.Ticks);
                    LastError = null;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
                { LastError = error; }
            }
            return acknowledged;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { LastError = error; return 0; }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        { LastError = error; return 0; }
        finally { _transportGate.Release(); }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (!_permission()) throw new HttpRequestException("Observed telemetry consent unavailable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
    }

    private static bool AcceptedHeaders(HttpResponseMessage response) =>
        HasHeader(response, "X-Orand-Telemetry-Schema", "4") &&
        HasHeader(response, "X-Orand-Telemetry-Accepted", "true") &&
        HasHeader(response, "X-Orand-Telemetry-Enabled", "true");

    private static bool HasHeader(HttpResponseMessage response, string name, string expected) =>
        response.Headers.TryGetValues(name, out var values) && values.SequenceEqual([expected], StringComparer.Ordinal);

    private static async Task<bool> HasMatchingReceiptAsync(HttpResponseMessage response, string packetId, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        if (response.Content.Headers.ContentLength is > 4096) return false;
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var buffer = new byte[4097];
        var size = 0;
        while (size < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(size), timeout.Token).ConfigureAwait(false);
            if (read == 0) break;
            size += read;
        }
        if (size > 4096) return false;
        using var document = JsonDocument.Parse(buffer.AsMemory(0, size));
        var root = document.RootElement;
        ObservedTelemetryWire.RejectDuplicateKeys(root);
        return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 3 &&
               root.TryGetProperty("schemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out var version) && version == 4 &&
               root.TryGetProperty("packetId", out var identity) && identity.ValueKind == JsonValueKind.String && identity.GetString() == packetId &&
               root.TryGetProperty("accepted", out var accepted) && accepted.ValueKind == JsonValueKind.True;
    }
}
