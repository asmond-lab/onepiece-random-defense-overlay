using System.Net.Http;
using System.Text.Json;

namespace OrandOverlay;

/// <summary>Consent-gated, atomically durable schema-v3 gameplay packets.</summary>
public sealed class GameplayTelemetryOutbox
{
    public const int MaxPackets = 256;
    public const long MaxStorageBytes = 32L * 1024 * 1024;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private readonly Func<bool> _permission;
    private readonly string _directory;
    private readonly string _recoveryDirectory;
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _diskGate = new(1, 1);
    private readonly SemaphoreSlim _transportGate = new(1, 1);
    private long _expiredFileCount, _capacityRefusalCount, _errorCount, _lastWriteUtcTicks;

    public Exception? LastError { get; private set; }
    public long ExpiredFileCount => Interlocked.Read(ref _expiredFileCount);
    public long CapacityRefusalCount => Interlocked.Read(ref _capacityRefusalCount);
    public long ErrorCount => Interlocked.Read(ref _errorCount);

    public GameplayTelemetryOutbox(Func<bool> permission, string ownedUserRoot, HttpClient httpClient,
        Uri endpoint, TimeProvider? timeProvider = null)
    {
        if (!Path.IsPathFullyQualified(ownedUserRoot)) throw new ArgumentException("An owned absolute root is required.", nameof(ownedUserRoot));
        if (!endpoint.IsAbsoluteUri || !endpoint.AbsolutePath.EndsWith("/v3/gameplay", StringComparison.Ordinal))
            throw new ArgumentException("Expected v3 gameplay endpoint.", nameof(endpoint));
        _permission = permission;
        _directory = Path.Combine(ownedUserRoot, "gameplay-v3", "pending");
        _recoveryDirectory = Path.Combine(ownedUserRoot, "gameplay-v3", "recovery");
        _http = httpClient;
        _endpoint = endpoint;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Atomically checkpoints a mutable partial chunk. Drafts are never transport candidates.</summary>
    public Task<bool> SaveDraftAsync(GameplayTelemetryPacket packet, CancellationToken cancellationToken = default) =>
        SaveAsync(packet, sealedPacket: false, cancellationToken);

    /// <summary>Atomically promotes an immutable packet, removing only its compatible draft.</summary>
    public Task<bool> EnqueueAsync(GameplayTelemetryPacket packet, CancellationToken cancellationToken = default) =>
        SaveAsync(packet, sealedPacket: true, cancellationToken);

    private async Task<bool> SaveAsync(GameplayTelemetryPacket packet, bool sealedPacket, CancellationToken cancellationToken)
    {
        if (!_permission()) return false;
        byte[] bytes;
        try { bytes = GameplayTelemetryWire.Serialize(packet); }
        catch (Exception e) when (e is ArgumentException or JsonException) { RecordError(e); return false; }
        return await SavePacketBytesAsync(packet, bytes, sealedPacket, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SavePacketBytesAsync(GameplayTelemetryPacket packet, byte[] bytes,
        bool sealedPacket, CancellationToken cancellationToken)
    {
        await _diskGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_permission()) return false;
            Directory.CreateDirectory(_directory);
            ExpireOwnedFiles();
            var finalPath = SealedPath(packet.PacketId);
            var draftPath = DraftPath(packet.PacketId);
            var targetPath = sealedPacket ? finalPath : draftPath;

            if (File.Exists(finalPath))
            {
                var existing = await File.ReadAllBytesAsync(finalPath, cancellationToken).ConfigureAwait(false);
                if (!existing.AsSpan().SequenceEqual(bytes))
                    throw new InvalidDataException("Packet identity conflict with immutable sealed content.");
                if (sealedPacket && File.Exists(draftPath))
                {
                    var draft = await File.ReadAllBytesAsync(draftPath, cancellationToken).ConfigureAwait(false);
                    if (!IsCompatibleDraft(draft, bytes))
                        throw new InvalidDataException("Packet identity conflict during draft promotion.");
                    File.Delete(draftPath);
                }
                return true;
            }

            long replacedBytes = 0;
            if (File.Exists(draftPath))
            {
                var existingDraft = await File.ReadAllBytesAsync(draftPath, cancellationToken).ConfigureAwait(false);
                if (!IsCompatibleDraft(existingDraft, bytes))
                {
                    if (!sealedPacket && IsCompatibleDraft(bytes, existingDraft)) return true;
                    throw new InvalidDataException("Packet identity conflict in draft content.");
                }
                if (!sealedPacket && existingDraft.AsSpan().SequenceEqual(bytes)) return true;
                replacedBytes = new FileInfo(draftPath).Length;
            }

            var state = StorageState();
            var alreadyCounted = File.Exists(draftPath);
            var proposedCount = state.PacketCount + (alreadyCounted ? 0 : 1);
            var proposedBytes = checked(state.Bytes - replacedBytes + bytes.LongLength);
            if (proposedCount > MaxPackets || proposedBytes > MaxStorageBytes)
            {
                Interlocked.Increment(ref _capacityRefusalCount);
                throw new IOException($"Gameplay telemetry capacity reached ({state.PacketCount}/{MaxPackets} packets, {state.Bytes}/{MaxStorageBytes} bytes); existing unacknowledged data was preserved.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!_permission()) return false;
            if (!await AtomicWriteAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false)) return false;
            if (sealedPacket && File.Exists(draftPath)) File.Delete(draftPath);
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or OverflowException)
        { RecordError(e); return false; }
        finally { _diskGate.Release(); }
    }

    /// <summary>Promotes crash-left valid drafts verbatim before a new recording is drained.</summary>
    public async Task<int> RecoverDraftsAsync(CancellationToken cancellationToken = default)
    {
        if (!_permission()) return 0;
        await _diskGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_permission() || !Directory.Exists(_directory)) return 0;
            ExpireOwnedFiles();
            var recovered = 0;
            foreach (var draftPath in Directory.GetFiles(_directory, "*.draft.json")
                         .OrderBy(File.GetLastWriteTimeUtc).ThenBy(p => p, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_permission()) return recovered;
                try
                {
                    var draftBytes = await File.ReadAllBytesAsync(draftPath, cancellationToken).ConfigureAwait(false);
                    if (!TryReadPacket(draftBytes, out var draft) || !Path.GetFileName(draftPath).Equals(draft.PacketId + ".draft.json", StringComparison.Ordinal))
                        throw new InvalidDataException("Invalid gameplay draft was preserved for diagnosis.");
                    var finalPath = SealedPath(draft.PacketId);
                    if (File.Exists(finalPath))
                    {
                        var sealedBytes = await File.ReadAllBytesAsync(finalPath, cancellationToken).ConfigureAwait(false);
                        if (!IsCompatibleDraft(draftBytes, sealedBytes))
                            throw new InvalidDataException("Conflicting sealed packet and recovered draft were both preserved.");
                        File.Delete(draftPath);
                        continue;
                    }
                    // Keep the draft's durable timestamp so recovered fragments retain their prior order.
                    File.Move(draftPath, finalPath);
                    recovered++;
                }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
                { RecordError(e); }
            }
            return recovered;
        }
        finally { _diskGate.Release(); }
    }

    /// <summary>Requeues only explicit sealed-packet escrow, byte-for-byte. Uncertain native
    /// observations and gap wrappers remain local and can never enter the wire protocol.</summary>
    public async Task<int> RestorePendingPacketsAsync(CancellationToken cancellationToken = default)
    {
        if (!_permission() || !Directory.Exists(_recoveryDirectory)) return 0;
        var restored = 0;
        foreach (var file in Directory.GetFiles(_recoveryDirectory, "*.packet.recovery.json")
                     .OrderBy(File.GetLastWriteTimeUtc).ThenBy(p => p, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_permission()) return restored;
            var name = Path.GetFileName(file);
            if (name.Length != 32 + ".packet.recovery.json".Length || !Guid.TryParseExact(name[..32], "N", out _)) continue;
            try
            {
                if (_time.GetUtcNow().UtcDateTime - File.GetLastWriteTimeUtc(file) > Retention)
                { File.Delete(file); Interlocked.Increment(ref _expiredFileCount); continue; }
                if (new FileInfo(file).Length > 2 * 1024 * 1024)
                    throw new InvalidDataException("Oversized pending gameplay recovery envelope was preserved.");
                using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false));
                var root = document.RootElement;
                if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("kind").GetString() != "pending-packet")
                    throw new InvalidDataException("Unsupported pending gameplay recovery envelope was preserved.");
                var bytes = root.GetProperty("packetBytes").GetBytesFromBase64();
                if (!TryReadPacket(bytes, out var packet))
                    throw new InvalidDataException("Invalid pending gameplay recovery packet was preserved.");
                if (!await SavePacketBytesAsync(packet, bytes, true, cancellationToken).ConfigureAwait(false)) return restored;
                // Durable hand-off precedes removal. A crash between these operations is
                // harmless: immutable packet identity makes the next restore idempotent.
                if (!_permission()) return restored;
                File.Delete(file);
                restored++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or
                                       InvalidOperationException or FormatException or KeyNotFoundException)
            { RecordError(e); }
        }
        return restored;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!_permission()) return;
        await _transportGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] paths;
            await _diskGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_permission() || !Directory.Exists(_directory)) return;
                ExpireOwnedFiles();
                paths = Directory.GetFiles(_directory, "*.v3.json")
                    .OrderBy(File.GetLastWriteTimeUtc).ThenBy(p => p, StringComparer.Ordinal).ToArray();
            }
            finally { _diskGate.Release(); }
            if (paths.Length == 0) return;

            using var options = new HttpRequestMessage(HttpMethod.Options, _endpoint);
            options.Headers.Add(TelemetryUploader.SchemaHeader, "3");
            cancellationToken.ThrowIfCancellationRequested();
            if (!_permission()) return;
            using var handshake = await _http.SendAsync(options, cancellationToken).ConfigureAwait(false);
            if (!Accepts(handshake)) { RecordError(new InvalidDataException("Gameplay v3 handshake not accepted.")); return; }

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_permission()) return;
                byte[] bytes;
                string packetId;
                await _diskGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!File.Exists(path)) continue;
                    bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                    if (!TryReadPacket(bytes, out var packet) || !Path.GetFileName(path).Equals(packet.PacketId + ".v3.json", StringComparison.Ordinal))
                    { RecordError(new InvalidDataException("Invalid sealed gameplay packet was preserved.")); return; }
                    packetId = packet.PacketId;
                }
                finally { _diskGate.Release(); }

                using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
                request.Headers.Add(TelemetryUploader.SchemaHeader, "3");
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new("application/json");
                cancellationToken.ThrowIfCancellationRequested();
                if (!_permission()) return;
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!_permission()) return;
                if (!Accepts(response)) { RecordError(new InvalidDataException("Gameplay packet not acknowledged.")); return; }
                if (!await HasMatchingAckAsync(response, packetId, cancellationToken).ConfigureAwait(false))
                { RecordError(new InvalidDataException("Mismatched gameplay acknowledgement.")); return; }

                cancellationToken.ThrowIfCancellationRequested();
                if (!_permission()) return;
                await _diskGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!_permission() || !File.Exists(path)) return;
                    var current = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                    if (!current.AsSpan().SequenceEqual(bytes))
                    { RecordError(new InvalidDataException("Acknowledged packet changed before deletion.")); return; }
                    File.Delete(path);
                }
                finally { _diskGate.Release(); }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or JsonException or InvalidOperationException)
        { RecordError(e); }
        finally { _transportGate.Release(); }
    }

    private async Task<bool> AtomicWriteAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temp = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!_permission()) return false;
            File.Move(temp, path, overwrite: true);
            StampInWriteOrder(path);
            return true;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private void StampInWriteOrder(string path)
    {
        var ticks = _time.GetUtcNow().UtcDateTime.Ticks;
        if (Directory.Exists(_directory))
        {
            var previous = Directory.GetFiles(_directory, "*.v3.json").Concat(Directory.GetFiles(_directory, "*.draft.json"))
                .Where(existing => !string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                .Select(File.GetLastWriteTimeUtc).Select(value => value.Ticks).DefaultIfEmpty(0).Max();
            if (previous > _lastWriteUtcTicks) _lastWriteUtcTicks = previous;
        }
        if (ticks <= _lastWriteUtcTicks) ticks = _lastWriteUtcTicks + 1;
        _lastWriteUtcTicks = ticks;
        File.SetLastWriteTimeUtc(path, new DateTime(ticks, DateTimeKind.Utc));
    }

    private void ExpireOwnedFiles()
    {
        if (!_permission() || !Directory.Exists(_directory)) return;
        RecoverOwnedTemporaryFiles();
        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var path in Directory.GetFiles(_directory, "*.v3.json").Concat(Directory.GetFiles(_directory, "*.draft.json")))
        {
            if (now - File.GetLastWriteTimeUtc(path) <= Retention) continue;
            File.Delete(path);
            Interlocked.Increment(ref _expiredFileCount);
        }
    }

    // Only this outbox's GUID-named stages are eligible. Complete crash-left stages
    // are recovered instead of discarded; incomplete stages cannot be a valid packet.
    private void RecoverOwnedTemporaryFiles()
    {
        foreach (var temp in Directory.GetFiles(_directory, "*.json.tmp"))
        {
            if (!_permission()) return;
            var name = Path.GetFileName(temp);
            var suffix = name.EndsWith(".draft.json.tmp", StringComparison.Ordinal) ? ".draft.json.tmp" :
                name.EndsWith(".v3.json.tmp", StringComparison.Ordinal) ? ".v3.json.tmp" : null;
            if (suffix is null || name.Length != 32 + suffix.Length || !Guid.TryParseExact(name[..32], "N", out _)) continue;
            try
            {
                var bytes = File.ReadAllBytes(temp);
                if (!TryReadPacket(bytes, out var packet) || packet.PacketId != name[..32])
                {
                    File.Delete(temp);
                    RecordError(new InvalidDataException("Incomplete gameplay temporary checkpoint was discarded; prior durable content was preserved."));
                    continue;
                }
                var target = temp[..^4];
                if (File.Exists(target))
                {
                    var existing = File.ReadAllBytes(target);
                    if (existing.AsSpan().SequenceEqual(bytes) || suffix == ".draft.json.tmp" && IsCompatibleDraft(bytes, existing))
                    { File.Delete(temp); continue; }
                    if (suffix != ".draft.json.tmp" || !IsCompatibleDraft(existing, bytes))
                    { RecordError(new InvalidDataException("Conflicting temporary gameplay checkpoint was preserved.")); continue; }
                }
                if (!_permission()) return;
                File.Move(temp, target, overwrite: true);
            }
            catch (IOException) { /* An active writer owns the stage; do not touch it. */ }
            catch (UnauthorizedAccessException e) { RecordError(e); }
        }
    }

    private (int PacketCount, long Bytes) StorageState()
    {
        var files = Directory.GetFiles(_directory, "*.v3.json").Concat(Directory.GetFiles(_directory, "*.draft.json"))
            .Select(p => new FileInfo(p)).ToArray();
        var ids = files.Select(f => f.Name.EndsWith(".draft.json", StringComparison.Ordinal)
                ? f.Name[..^".draft.json".Length] : f.Name[..^".v3.json".Length])
            .Distinct(StringComparer.Ordinal).Count();
        var temporaryBytes = Directory.GetFiles(_directory, "*.tmp").Sum(path => new FileInfo(path).Length);
        return (ids, files.Sum(f => f.Length) + temporaryBytes);
    }

    private string SealedPath(string packetId) => Path.Combine(_directory, packetId + ".v3.json");
    private string DraftPath(string packetId) => Path.Combine(_directory, packetId + ".draft.json");

    private static bool TryReadPacket(byte[] bytes, out GameplayTelemetryPacket packet)
    {
        packet = null!;
        if (!GameplayTelemetryWire.IsSafe(bytes)) return false;
        try
        {
            packet = JsonSerializer.Deserialize<GameplayTelemetryPacket>(bytes, GameplayTelemetryWire.JsonOptions)!;
            return packet is not null;
        }
        catch (JsonException) { return false; }
    }

    private static bool IsCompatibleDraft(byte[] draftBytes, byte[] laterBytes)
    {
        if (!TryReadPacket(draftBytes, out var draft) || !TryReadPacket(laterBytes, out var later) ||
            draft.PacketId != later.PacketId || draft.MatchId != later.MatchId || draft.ChunkIndex != later.ChunkIndex ||
            draft.SchemaVersion != later.SchemaVersion || draft.ConsentVersion != later.ConsentVersion ||
            draft.AppVersion != later.AppVersion || draft.MapVersion != later.MapVersion ||
            draft.MapScriptSha256 != later.MapScriptSha256 || draft.ProfileVersion != later.ProfileVersion ||
            draft.Difficulty != later.Difficulty || draft.Events.Length > later.Events.Length) return false;
        for (var i = 0; i < draft.Events.Length; i++)
        {
            var left = JsonSerializer.SerializeToUtf8Bytes(draft.Events[i], GameplayTelemetryWire.JsonOptions);
            var right = JsonSerializer.SerializeToUtf8Bytes(later.Events[i], GameplayTelemetryWire.JsonOptions);
            if (!left.AsSpan().SequenceEqual(right)) return false;
        }
        return true;
    }

    private static async Task<bool> HasMatchingAckAsync(HttpResponseMessage response, string packetId, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var ackBytes = new byte[4097];
        var length = 0;
        while (length < ackBytes.Length)
        {
            var read = await stream.ReadAsync(ackBytes.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            length += read;
        }
        if (length == ackBytes.Length) return false;
        using var ack = JsonDocument.Parse(ackBytes.AsMemory(0, length));
        var body = ack.RootElement;
        return body.ValueKind == JsonValueKind.Object && body.EnumerateObject().Count() == 3 &&
            body.TryGetProperty("schemaVersion", out var schema) && schema.TryGetInt32(out var version) && version == 3 &&
            body.TryGetProperty("packetId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == packetId &&
            body.TryGetProperty("accepted", out var accepted) && accepted.ValueKind == JsonValueKind.True;
    }

    private void RecordError(Exception error)
    {
        LastError = error;
        Interlocked.Increment(ref _errorCount);
    }
    private static bool Accepts(HttpResponseMessage response) => response.IsSuccessStatusCode &&
        Header(response, TelemetryUploader.SchemaHeader, "3") && Header(response, TelemetryUploader.AcceptedHeader, "true") &&
        Header(response, TelemetryUploader.EnabledHeader, "true");
    private static bool Header(HttpResponseMessage response, string name, string value) =>
        response.Headers.TryGetValues(name, out var values) && values.SequenceEqual([value], StringComparer.OrdinalIgnoreCase);
}
