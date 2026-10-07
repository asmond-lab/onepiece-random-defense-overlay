using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

// Only the locator + inventory reader's delegate calls are recorded. Process discovery,
// module binding, archive checks, map loading and OS queries remain outside this boundary.
internal static class InventoryReadTrace
{
    internal const int MaximumCalls = 49152, MaximumBytes = 1024 * 1024;
    internal const int MaximumFileBytes = 16 * 1024 * 1024;
    internal const int MaximumPayloadBytes = 8 * 1024 * 1024;
    internal static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };
    internal sealed record Identity(string Version, string ExecutableHash, string MapVersion, string MapHash,
        string ProfileId, long ProfileRevision, string ProfileHash, string Session, ulong ModuleBase);
    internal sealed record Call(ulong Address, int Length, byte[]? Bytes, string? Failure);
    internal sealed record Payload(int Schema, Identity Identity, bool Complete, string? Failure,
        Call[] Calls);
    private sealed record Envelope(int Schema, string Sha256, string Payload);
    private sealed class TraceCoverageException(string message) : Exception(message);
    internal sealed record Result(Warcraft300Diagnostic.Inventory? Inventory, string? Failure, int Calls, int Bytes,
        bool Complete, double ElapsedMilliseconds);

    private static Warcraft300Diagnostic.Inventory Read(Func<ulong, int, byte[]> read, ulong module, MemoryProfile profile)
    {
        var locator = Warcraft300WorldLocator.Read(read, module, default);
        var inventory = Warcraft300Diagnostic.ReadInventory(read, module, locator.World, profile);
        if (locator != Warcraft300WorldLocator.Read(read, module, default))
            throw new InvalidDataException("Diagnostic world context changed");
        return inventory;
    }
    private static string Failure(Exception error) => error switch
    {
        InvalidDataException => nameof(InvalidDataException),
        IOException => nameof(IOException),
        Win32Exception => nameof(Win32Exception),
        OverflowException => nameof(OverflowException),
        InvalidOperationException => nameof(InvalidOperationException),
        UnauthorizedAccessException => nameof(UnauthorizedAccessException),
        _ => throw error
    };
    private static Exception Restore(string type) => type switch
    {
        nameof(InvalidDataException) => new InvalidDataException("Recorded native read failure"),
        nameof(IOException) => new IOException("Recorded native read failure"),
        nameof(Win32Exception) => new Win32Exception("Recorded native read failure"),
        nameof(OverflowException) => new OverflowException("Recorded native read failure"),
        nameof(InvalidOperationException) => new InvalidOperationException("Recorded native read failure"),
        nameof(UnauthorizedAccessException) => new UnauthorizedAccessException("Recorded native read failure"),
        _ => throw new InvalidDataException("Unknown trace failure type")
    };
    internal static string ProfileHash(MemoryProfile profile) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(profile)));
    private static void CheckIdentity(Identity id, MemoryProfile profile)
    {
        if (id.Version != Warcraft300Diagnostic.Version || id.ExecutableHash != Warcraft300Diagnostic.Hash ||
            id.MapHash != (id.MapVersion switch
            {
                "2.320" => Map2320GrowthSource.JassSha256,
                "2.322" => Map2322SourceContract.JassSha256,
                "2.323" => Map2323SourceContract.JassSha256,
                _ => ""
            }) ||
            id.ProfileId != profile.ProfileId || id.ProfileRevision != profile.ProfileRevision ||
            id.ProfileHash != ProfileHash(profile) ||
            string.IsNullOrWhiteSpace(id.Session) || id.Session.Length > 256 ||
            !ReadOnlyProcessMemory.IsPlausibleUserAddress(id.ModuleBase) ||
            !profile.KnownExperimental || MemoryProfileValidator.Validate(profile).Count != 0 ||
            !Warcraft300Diagnostic.SessionAllows(profile, true))
            throw new InvalidDataException("Trace identity/profile mismatch");
    }
    internal static (byte[] File, Result Result) Capture(Func<ulong, int, byte[]> live, Identity id,
        MemoryProfile profile, Func<long>? timestamp = null, long? frequency = null)
    {
        CheckIdentity(id, profile);
        var clock = timestamp ?? Stopwatch.GetTimestamp;
        var hz = frequency ?? Stopwatch.Frequency;
        if (hz <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        var started = clock();
        bool complete = true;
        int bytes = 0;
        var calls = new List<Call>();
        byte[] RecordingRead(ulong address, int length)
        {
            // Cap before allocating a copy; losing capture never changes a live read.
            bool canStore = complete && clock() - started >= 0 && clock() - started < 5 * hz &&
                length is > 0 and <= 65536 && calls.Count < MaximumCalls && length <= MaximumBytes - bytes;
            if (!canStore) complete = false;
            try
            {
                var data = live(address, length);
                if (canStore)
                {
                    if (data is null || data.Length > length) { complete = false; }
                    else { calls.Add(new(address, length, (byte[])data.Clone(), null)); bytes += data.Length; }
                }
                return data!;
            }
            catch (Exception e) when (e is InvalidDataException or IOException or Win32Exception or OverflowException or InvalidOperationException or UnauthorizedAccessException)
            {
                if (canStore) calls.Add(new(address, length, null, Failure(e)));
                throw;
            }
        }
        Warcraft300Diagnostic.Inventory? inventory = null;
        string? failure = null;
        try { inventory = Read(RecordingRead, id.ModuleBase, profile); }
        catch (Exception e) when (e is InvalidDataException or IOException or Win32Exception or OverflowException or InvalidOperationException or UnauthorizedAccessException)
        { failure = Failure(e); }
        var elapsed = (double)(clock() - started) * 1000 / hz;
        if (elapsed < 0 || elapsed >= MaximumDuration.TotalMilliseconds) complete = false;
        var payload = new Payload(1, id, complete, failure, calls.ToArray());
        return (Encode(payload), new(inventory, failure, calls.Count, bytes, complete, elapsed));
    }
    private static byte[] Encode(Payload payload)
    {
        var raw = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        if (raw.Length > MaximumPayloadBytes) throw new InvalidDataException("Trace size cap");
        var file = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, Convert.ToHexString(SHA256.HashData(raw)), Convert.ToBase64String(raw)), Json);
        if (file.Length > MaximumFileBytes) throw new InvalidDataException("Trace file cap");
        return file;
    }
    private static void NoDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate trace property");
                NoDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var entry in value.EnumerateArray()) NoDuplicateProperties(entry);
    }
    internal static Payload Decode(ReadOnlySpan<byte> file, MemoryProfile profile, Identity? expected = null)
    {
        try
        {
            if (file.Length is 0 or > MaximumFileBytes) throw new InvalidDataException("Trace file bounds");
            using var outer = JsonDocument.Parse(file.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            NoDuplicateProperties(outer.RootElement);
            var envelope = outer.RootElement.Deserialize<Envelope>(Json) ?? throw new InvalidDataException();
            if (envelope.Schema != 1 || envelope.Sha256?.Length != 64 || envelope.Payload is null ||
                envelope.Payload.Length > ((MaximumPayloadBytes + 2) / 3) * 4)
                throw new InvalidDataException("Trace envelope schema");
            var raw = Convert.FromBase64String(envelope.Payload);
            if (raw.Length > MaximumPayloadBytes ||
                !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(envelope.Sha256), SHA256.HashData(raw)))
                throw new InvalidDataException("Trace checksum");
            using var inner = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 16 });
            NoDuplicateProperties(inner.RootElement);
            var trace = inner.RootElement.Deserialize<Payload>(Json) ?? throw new InvalidDataException();
            if (trace.Schema != 1 || trace.Identity is null || trace.Calls is null || !trace.Complete ||
                trace.Calls.Length is 0 or > MaximumCalls) throw new InvalidDataException("Incomplete trace");
            CheckIdentity(trace.Identity, profile);
            if (expected is not null && trace.Identity != expected) throw new InvalidDataException("Trace session mismatch");
            if (trace.Failure is not null) _ = Restore(trace.Failure);
            int total = 0;
            foreach (var call in trace.Calls)
            {
                if (call is null || !ReadOnlyProcessMemory.IsPlausibleUserAddress(call.Address) ||
                    call.Length is < 1 or > 65536 || call.Address > 0x7FFFFFFFFFFFUL - (ulong)(call.Length - 1) ||
                    (call.Bytes is null) == (call.Failure is null) ||
                    call.Bytes is { Length: > 65536 } || (call.Bytes?.Length ?? 0) > call.Length)
                    throw new InvalidDataException("Trace read bounds");
                if (call.Failure is not null) _ = Restore(call.Failure);
                total = checked(total + (call.Bytes?.Length ?? 0));
                if (total > MaximumBytes) throw new InvalidDataException("Trace byte cap");
            }
            return trace;
        }
        catch (Exception e) when (e is JsonException or FormatException or OverflowException or ArgumentException)
        { throw new InvalidDataException("Malformed trace", e); }
    }
    internal static Result Replay(ReadOnlySpan<byte> file, MemoryProfile profile, Identity? expected = null)
    {
        var trace = Decode(file, profile, expected);
        int index = 0, bytes = 0;
        byte[] RecordedRead(ulong address, int length)
        {
            if (index >= trace.Calls.Length) throw new TraceCoverageException("Trace missing read");
            var call = trace.Calls[index++];
            if (call.Address != address || call.Length != length) throw new TraceCoverageException("Trace read order mismatch");
            if (call.Failure is not null) throw Restore(call.Failure);
            bytes += call.Bytes!.Length;
            return (byte[])call.Bytes.Clone();
        }
        Warcraft300Diagnostic.Inventory? inventory = null;
        string? failure = null;
        try { inventory = Read(RecordedRead, trace.Identity.ModuleBase, profile); }
        catch (TraceCoverageException e) { throw new InvalidDataException(e.Message, e); }
        catch (Exception e) when (e is InvalidDataException or IOException or Win32Exception or OverflowException or InvalidOperationException or UnauthorizedAccessException)
        { failure = Failure(e); }
        if (index != trace.Calls.Length || failure != trace.Failure || (failure is null && inventory is null))
            throw new InvalidDataException("Trace outcome/coverage mismatch");
        return new(inventory, failure, index, bytes, true, 0);
    }
}
