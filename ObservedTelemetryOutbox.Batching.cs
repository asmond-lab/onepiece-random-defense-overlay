using System.Text.Json;

namespace OrandOverlay;

public sealed partial class ObservedTelemetryOutbox
{
    // Only this file is mutable. It is never a transport candidate. Atomic replacement
    // commits each writer drain; atomic rename seals its ID and exact wire bytes.
    private string ActivePath => Path.Combine(_directory, "active.stage");

    internal async Task<bool> StageAsync(ObservedTelemetryPacket packet, CancellationToken token = default)
    {
        if (!_permission()) return false;
        byte[] bytes;
        try { bytes = ObservedTelemetryWire.Serialize(packet); }
        catch (ArgumentException error) { LastError = error; return false; }
        await _diskGate.WaitAsync(token).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            if (!_permission()) return false;
            Directory.CreateDirectory(_directory);
            ExpireOwnedFiles();
            long replacedBytes = 0;
            if (File.Exists(ActivePath))
            {
                var activeBytes = await ReadBoundedAsync(ActivePath, token).ConfigureAwait(false);
                var active = ObservedTelemetryWire.Deserialize(activeBytes);
                if (SameEnvelope(active, packet) && active.Events.Length + packet.Events.Length <= 64 &&
                    active.Events[^1].Sequence < packet.Events[0].Sequence)
                {
                    var candidate = active with { Events = active.Events.AddRange(packet.Events) };
                    ObservedTelemetryWire.Validate(candidate);
                    var candidateBytes = JsonSerializer.SerializeToUtf8Bytes(candidate, ObservedTelemetryWire.JsonOptions);
                    if (candidateBytes.Length <= ObservedTelemetryWire.MaxBytes)
                    {
                        bytes = candidateBytes;
                        replacedBytes = activeBytes.Length;
                    }
                }
                if (replacedBytes == 0)
                {
                    if (!_permission()) return false;
                    await SealActiveAsync(token).ConfigureAwait(false);
                }
            }
            var files = DurableFiles();
            if ((replacedBytes == 0 && files.Length >= MaxPackets) ||
                files.Sum(file => file.Length) - replacedBytes + bytes.LongLength > MaxStorageBytes)
            {
                Interlocked.Increment(ref _capacityRefusalCount);
                throw new IOException("Observed telemetry storage is full; pending records retained.");
            }
            if (!_permission()) return false;
            temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            if (!_permission()) return false;
            File.Move(temporary, ActivePath, overwrite: true);
            // No cancellable work after commit: the client must dequeue this input.
            temporary = null;
            LastError = null;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { LastError = error; return false; }
        finally
        {
            if (temporary is not null && File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { LastError = error; }
            }
            _diskGate.Release();
        }
    }

    private FileInfo[] DurableFiles() => Directory.GetFiles(_directory, "*.json")
        .Concat(File.Exists(ActivePath) ? new[] { ActivePath } : Array.Empty<string>())
        .Select(path => new FileInfo(path)).ToArray();

    private async Task SealActiveAsync(CancellationToken token)
    {
        if (!File.Exists(ActivePath)) return;
        // Read and validate before the only commit operation. A crash leaves either
        // active.stage or the immutable JSON, never both and never a cleanup journal.
        var bytes = await ReadBoundedAsync(ActivePath, token).ConfigureAwait(false);
        var packet = ObservedTelemetryWire.Deserialize(bytes);
        token.ThrowIfCancellationRequested();
        if (!_permission()) throw new IOException("Observed telemetry consent unavailable.");
        File.Move(ActivePath, Path.Combine(_directory, packet.PacketId + ".json"));
    }

    private static bool SameEnvelope(ObservedTelemetryPacket left, ObservedTelemetryPacket right) =>
        left.SchemaVersion == right.SchemaVersion && left.ConsentVersion == right.ConsentVersion &&
        left.SessionId == right.SessionId && left.AppVersion == right.AppVersion &&
        left.MapVersion == right.MapVersion && left.DatasetFingerprint == right.DatasetFingerprint &&
        left.GameVersion == right.GameVersion && left.Source == right.Source;
}
