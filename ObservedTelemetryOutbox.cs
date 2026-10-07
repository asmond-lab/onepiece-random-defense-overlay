using System.Net.Http;
using System.Text.Json;

namespace OrandOverlay;

public sealed partial class ObservedTelemetryOutbox
{
    public const int MaxPackets = 256;
    public const long MaxStorageBytes = 32L * 1024 * 1024;
    public static readonly Uri DefaultEndpoint = new("https://orand-telemetry.epic42121.workers.dev/v4/observations");
    private readonly Func<bool> _permission;
    private readonly string _directory;
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _diskGate = new(1, 1), _transportGate = new(1, 1);
    private long _acceptedPacketCount, _lastAcknowledgedUtcTicks, _capacityRefusalCount;
    public Exception? LastError { get; private set; }
    public long AcceptedPacketCount => Interlocked.Read(ref _acceptedPacketCount);
    public long CapacityRefusalCount => Interlocked.Read(ref _capacityRefusalCount);
    public DateTimeOffset? LastAcknowledgedAt => Interlocked.Read(ref _lastAcknowledgedUtcTicks) is var ticks && ticks > 0
        ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    public ObservedTelemetryOutbox(Func<bool> permission, string ownedUserRoot, HttpClient httpClient,
        Uri? endpoint = null, TimeProvider? timeProvider = null)
    {
        if (!Path.IsPathFullyQualified(ownedUserRoot)) throw new ArgumentException("An absolute owned root is required.", nameof(ownedUserRoot));
        if (endpoint is not null && endpoint != DefaultEndpoint) throw new ArgumentException("Expected the observed telemetry endpoint.", nameof(endpoint));
        _permission = permission;
        _directory = Path.Combine(ownedUserRoot, "observed-v4", "pending");
        _http = httpClient;
        _endpoint = endpoint ?? DefaultEndpoint;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<bool> EnqueueAsync(ObservedTelemetryPacket packet, CancellationToken cancellationToken = default)
    {
        if (!_permission()) return false;
        byte[] bytes;
        try { bytes = ObservedTelemetryWire.Serialize(packet); }
        catch (ArgumentException error) { LastError = error; return false; }
        await _diskGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            if (!_permission()) return false;
            Directory.CreateDirectory(_directory);
            ExpireOwnedFiles();
            var path = Path.Combine(_directory, packet.PacketId + ".json");
            if (File.Exists(path))
            {
                var existing = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
                if (!existing.AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Immutable packet identity conflict.");
                return true;
            }
            var files = DurableFiles();
            if (files.Length >= MaxPackets || files.Sum(file => file.Length) + bytes.LongLength > MaxStorageBytes)
            {
                Interlocked.Increment(ref _capacityRefusalCount);
                throw new IOException("Observed telemetry storage is full; pending records retained.");
            }
            if (!_permission()) return false;
            temporary = Path.Combine(_directory, packet.PacketId + ".tmp");
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (!_permission()) return false;
            File.Move(temporary, path);
            temporary = null;
            LastError = null;
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { LastError = error; return false; }
        finally
        {
            if (temporary is not null && File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { LastError = error; }
            }
            _diskGate.Release();
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > ObservedTelemetryWire.MaxBytes) throw new InvalidDataException("Oversized telemetry file.");
        var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        if (bytes.Length > ObservedTelemetryWire.MaxBytes) throw new InvalidDataException("Oversized telemetry file.");
        return bytes;
    }

    private void ExpireOwnedFiles()
    {
        foreach (var path in Directory.GetFiles(_directory))
        {
            var name = Path.GetFileName(path);
            if (name == "active.stage")
            {
                if (_time.GetUtcNow().UtcDateTime - File.GetLastWriteTimeUtc(path) > TimeSpan.FromDays(30)) File.Delete(path);
                continue;
            }
            if (name.Length is not (37 or 36) || !Guid.TryParseExact(name[..32], "N", out _) ||
                !(name.EndsWith(".json", StringComparison.Ordinal) || name.EndsWith(".tmp", StringComparison.Ordinal))) continue;
            var age = _time.GetUtcNow().UtcDateTime - File.GetLastWriteTimeUtc(path);
            if (age > TimeSpan.FromDays(30) || name.EndsWith(".tmp", StringComparison.Ordinal) && age > TimeSpan.FromMinutes(5))
                File.Delete(path);
        }
    }
}
