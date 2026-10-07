using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed partial class Warcraft300DiagnosticTests
{
    private static InventoryReadTrace.Identity TraceId(MemoryProfile profile) => new(
        Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, "2.323", Map2323SourceContract.JassSha256,
        profile.ProfileId, profile.ProfileRevision, InventoryReadTrace.ProfileHash(profile), "synthetic-session", Fixture.B);

    private static Fixture TraceFixture(Fixture? input = null)
    {
        var f = input ?? new Fixture();
        const ulong k = 0x290000000, keyA = 0xFEDCBA9876543210, keyB = 0x8000000000000001;
        const byte byteA = 0xD3, byteB = 0xA7;
        unchecked
        {
            var a = BitOperations.RotateRight(Fixture.Ui, 23) ^ 0x5A64008D1F97DB7AUL ^ byteA;
            a -= 0xFA3CC4C012B9EF8EUL + 0xF4339B63841E7E5EUL;
            var encodedUi = (BitOperations.RotateRight(a, 12) + 0x52E7B4144FB4B0E9UL) ^ keyA;
            var w = (Fixture.Frame ^ keyB ^ byteB) - 0xBBAD6A13B280A99CUL;
            w ^= 0xC93E2E7D3C27237DUL;
            var encodedWorld = BitOperations.RotateRight(BitOperations.RotateRight(w, 13) - 0x9BE33DE0422B8111UL, 24);
            f.Q(Fixture.B + 0x2F56EF8, k); f.Q(k + 0x1B4, keyA); f.Q(k + 0x19A, keyB);
            f.Q(Fixture.B + 0x2FDD260, encodedUi);
            f.Put(Fixture.B + 0x2E8DC69, [byteA]); f.Put(Fixture.B + 0x2E8DE3E, [byteB]);
            f.Q(Fixture.Ui + 0x6A8, encodedWorld);
        }
        return f;
    }
    private static Fixture RegisteredFrame(int count)
    {
        var f = TraceFixture();
        f.D(Fixture.Frame + 0xC08, (uint)count);
        f.D(0x260000030, (uint)count);
        for (var i = 0; i < count; i++)
        {
            var unit = Fixture.Unit + (ulong)i * 0x2000;
            var slot = 0x270000000UL + (ulong)i * 16;
            var record = 0x280000000UL + (ulong)i * 0x1000;
            f.Q(Fixture.Array + (ulong)i * 8, unit);
            f.Q(unit, Fixture.B + 0x2792E78);
            f.D(unit + 0x18, (uint)i);
            f.D(unit + 0x1C, (uint)(i + 5));
            f.D(unit + 0x1C0, i == 0 ? 6U : 27U);
            f.D(unit + 0x178, i == 0 ? 0x68303031U : 0xABCDEF12U);
            f.D(slot, 0xFFFFFFFE);
            f.Q(slot + 8, record);
            f.D(record + 0x18, 0x2B61676C);
            f.D(record + 0x24, (uint)(i + 5));
            f.Q(record + 0x90, unit);
        }
        return f;
    }
    private static byte[] Repack(InventoryReadTrace.Payload payload)
    {
        var raw = JsonSerializer.SerializeToUtf8Bytes(payload);
        return JsonSerializer.SerializeToUtf8Bytes(new { Schema = 1, Sha256 = Convert.ToHexString(SHA256.HashData(raw)),
            Payload = Convert.ToBase64String(raw) });
    }
    [Theory]
    [InlineData(335, false)] [InlineData(335, true)]
    [InlineData(512, false)] [InlineData(512, true)]
    public void MeasureRealReaderDelegateBudget(int count, bool retry)
    {
        var f = RegisteredFrame(count); var profile = Profile();
        var calls = new List<InventoryReadTrace.Call>();
        var rawReads = 0;
        byte[] Read(ulong address, int length)
        {
            var data = f.Read(address, length);
            if (retry && address == Fixture.Unit + 0x178 && ++rawReads == 2) data[0] ^= 8;
            calls.Add(new(address, length, (byte[])data.Clone(), null));
            return data;
        }
        var locator = Warcraft300WorldLocator.Read(Read, Fixture.B, default);
        var inventory = Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, locator.World, profile);
        Assert.Equal(locator, Warcraft300WorldLocator.Read(Read, Fixture.B, default));
        Assert.Equal(count, inventory.Count);
        Assert.Equal(1, inventory.Owned);
        Assert.Equal(count - 1, inventory.Foreign);
        var measuredCalls = calls.Count;
        var bytes = calls.Sum(call => call.Bytes!.Length);
        var encoded = Repack(new InventoryReadTrace.Payload(1, TraceId(profile), true, null, calls.ToArray()));
        calls.Clear(); rawReads = 0;
        var (file, capture) = InventoryReadTrace.Capture(Read, TraceId(profile), profile);
        Assert.True(capture.Complete);
        Assert.Null(capture.Failure);
        Assert.Equal(measuredCalls, capture.Calls);
        Assert.Equal(bytes, capture.Bytes);
        Assert.Equal(encoded.Length, file.Length);
        Assert.Equal(count, InventoryReadTrace.Replay(file, profile).Inventory!.Count);
        Console.WriteLine($"real-reader synthetic registered={count} retry={retry} calls={measuredCalls} returnedBytes={bytes} serializedBytes={file.Length}");
    }
    [Fact] public void CountCapCannotTurnACompletedNativeReadIntoACompleteTrace()
    {
        var f = RegisteredFrame(1024); var p = Profile(); var rawReads = 0; var liveCalls = 0;
        byte[] Read(ulong address, int length)
        {
            liveCalls++;
            var data = f.Read(address, length);
            if (address == Fixture.Unit + 0x178 && ++rawReads == 2) data[0] ^= 8;
            return data;
        }
        var (file, result) = InventoryReadTrace.Capture(Read, TraceId(p), p);
        Assert.Equal(1024, result.Inventory!.Count);
        Assert.True(liveCalls > InventoryReadTrace.MaximumCalls);
        Assert.Equal(InventoryReadTrace.MaximumCalls, result.Calls);
        Assert.False(result.Complete);
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Decode(file, p));
    }
    [Fact] public void SerializedCountByteAndAllocationBoundariesRejectBeforeReplay()
    {
        var p = Profile(); var id = TraceId(p);
        var chunk = new InventoryReadTrace.Call(Fixture.B, 65536, new byte[65536], null);
        var exact = new InventoryReadTrace.Payload(1, id, true, null,
            Enumerable.Repeat(chunk, InventoryReadTrace.MaximumBytes / 65536).ToArray());
        Assert.Equal(InventoryReadTrace.MaximumBytes, InventoryReadTrace.Decode(Repack(exact), p).Calls.Sum(c => c.Bytes!.Length));
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Decode(Repack(exact with
            { Calls = [..exact.Calls, new InventoryReadTrace.Call(Fixture.B, 1, [0], null)] }), p));
        var shortRead = new InventoryReadTrace.Call(Fixture.B, 8, new byte[8], null);
        var maximum = exact with { Calls = Enumerable.Repeat(shortRead, InventoryReadTrace.MaximumCalls).ToArray() };
        Assert.Equal(InventoryReadTrace.MaximumCalls, InventoryReadTrace.Decode(Repack(maximum), p).Calls.Length);
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Decode(Repack(maximum with
            { Calls = [..maximum.Calls, shortRead] }), p));
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Decode(new byte[InventoryReadTrace.MaximumFileBytes + 1], p));
        var oversizedPayload = new { Schema = 1, Sha256 = new string('A', 64),
            Payload = new string('A', ((InventoryReadTrace.MaximumPayloadBytes + 2) / 3) * 4 + 1) };
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Decode(JsonSerializer.SerializeToUtf8Bytes(oversizedPayload), p));
    }
    [Fact] public void RealReaderReplayPreservesCountsRepeatedReadsAndNeverCallsLiveMemory()
    {
        var f = TraceFixture(); var p = Profile();
        var (file, captured) = InventoryReadTrace.Capture(f.Read, TraceId(p), p);
        Assert.True(captured.Complete); Assert.Null(captured.Failure);
        Assert.Equal(1, captured.Inventory!.Owned);
        var trace = InventoryReadTrace.Decode(file, p);
        Assert.Contains(trace.Calls.GroupBy(x => (x.Address, x.Length)), g => g.Count() > 1);
        var replay = InventoryReadTrace.Replay(file, p);
        Assert.Equal(captured.Calls, replay.Calls);
        Assert.Equal(1, replay.Inventory!.Rawcodes[0x68303031]);
        Assert.Equal(6, replay.Inventory.CurrentView.Slot);
    }
    [Fact] public void AliasesUnknownAndCompetingOwnerUseTheRealReader()
    {
        var f = TraceFixture(WithRegisteredAliases(secondUnit: true));
        f.D(Fixture.Unit + 0x1C0, 27);
        f.D(SecondRegistered + 0x178, 0xABCDEF12);
        var p = Profile();
        var (file, capture) = InventoryReadTrace.Capture(f.Read, TraceId(p), p);
        Assert.Null(capture.Failure);
        var inventory = InventoryReadTrace.Replay(file, p).Inventory!;
        Assert.Equal(3, inventory.Count);
        Assert.Equal(1, inventory.Owned);
        Assert.Equal(1, inventory.Foreign);
        Assert.Equal(0xABCDEF12U, Assert.Single(inventory.Rawcodes).Key);
    }
    [Fact] public void MissingNativeReadFailureIsPreservedAndNeverFilledWithZeros()
    {
        var f = TraceFixture(); var p = Profile();
        byte[] Read(ulong address, int size) => address == Fixture.B + 0x2F56EF8
            ? throw new IOException("synthetic missing region") : f.Read(address, size);
        var (file, capture) = InventoryReadTrace.Capture(Read, TraceId(p), p);
        Assert.Equal(nameof(InvalidDataException), capture.Failure); // locator wraps inaccessible native I/O
        Assert.Equal(nameof(IOException), Assert.Single(InventoryReadTrace.Decode(file, p).Calls).Failure);
        Assert.Equal(nameof(InvalidDataException), InventoryReadTrace.Replay(file, p).Failure);
    }
    [Fact] public void ChangedInputIsNotCollapsedAndMutationFailsThroughTheReader()
    {
        var f = TraceFixture(); var p = Profile(); var hits = 0;
        byte[] Read(ulong address, int size)
        {
            var data = f.Read(address, size);
            if (address == Fixture.Array && ++hits % 2 == 0) data[0] ^= 8;
            return data;
        }
        var (file, capture) = InventoryReadTrace.Capture(Read, TraceId(p), p);
        Assert.Equal(nameof(InvalidDataException), capture.Failure);
        Assert.Equal(4, hits);
        var vector = InventoryReadTrace.Decode(file, p).Calls.Where(c => c.Address == Fixture.Array).ToArray();
        Assert.Equal(4, vector.Length); Assert.NotEqual(vector[0].Bytes, vector[1].Bytes);
        Assert.Equal(nameof(InvalidDataException), InventoryReadTrace.Replay(file, p).Failure);
    }
    [Fact] public void FailedTraceCannotAcceptWrongOrMissingTerminalRead()
    {
        var f = TraceFixture(); var p = Profile(); var vectorReads = 0;
        byte[] Read(ulong address, int length)
        {
            var value = f.Read(address, length);
            if (address == Fixture.Array && ++vectorReads % 2 == 0) value[0] ^= 8;
            return value;
        }
        var (file, capture) = InventoryReadTrace.Capture(Read, TraceId(p), p);
        Assert.Equal(nameof(InvalidDataException), capture.Failure);
        var original = InventoryReadTrace.Decode(file, p);
        Assert.True(original.Calls.Length > 3);
        var wrongFinal = (InventoryReadTrace.Call[])original.Calls.Clone();
        wrongFinal[^1] = wrongFinal[^1] with { Address = wrongFinal[^1].Address + 8 };
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(Repack(original with { Calls = wrongFinal }), p));
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(Repack(original with { Calls = original.Calls[..^1] }), p));
        Assert.Equal(nameof(InvalidDataException), InventoryReadTrace.Replay(file, p).Failure);
    }
    [Fact] public void MissingOutOfOrderAndChangedValuesRejectEvenWithValidChecksum()
    {
        var f = TraceFixture(); var p = Profile();
        var (file, _) = InventoryReadTrace.Capture(f.Read, TraceId(p), p);
        var original = InventoryReadTrace.Decode(file, p);
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(Repack(original with { Calls = original.Calls[..^1] }), p));
        var swapped = (InventoryReadTrace.Call[])original.Calls.Clone(); (swapped[0], swapped[1]) = (swapped[1], swapped[0]);
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(Repack(original with { Calls = swapped }), p));
        var changed = (InventoryReadTrace.Call[])original.Calls.Clone();
        changed[0] = changed[0] with { Bytes = new byte[8] };
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(Repack(original with { Calls = changed }), p));
    }
    [Fact] public void ExplicitCaptureOverheadIsMeasuredAgainstUninstrumentedReader()
    {
        var f = TraceFixture(); var p = Profile(); var id = TraceId(p);
        const int iterations = 50;
        var baseline = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var locator = Warcraft300WorldLocator.Read(f.Read, Fixture.B, default);
            _ = Warcraft300Diagnostic.ReadInventory(f.Read, Fixture.B, locator.World, p);
            Assert.Equal(locator, Warcraft300WorldLocator.Read(f.Read, Fixture.B, default));
        }
        baseline.Stop();
        var captured = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
            Assert.True(InventoryReadTrace.Capture(f.Read, id, p).Result.Complete);
        captured.Stop();
        Console.WriteLine($"synthetic 50 reads: uninstrumented={baseline.Elapsed.TotalMilliseconds:F3}ms explicit-capture={captured.Elapsed.TotalMilliseconds:F3}ms; not live cadence");
    }
    [Fact] public void OfflineCliReadsSyntheticTraceWithoutProcessOrProfileDiscovery()
    {
        var f = TraceFixture();
        var p = JsonSerializer.Deserialize<MemoryProfile[]>(Program.ProfileJson)!.Single();
        var id = TraceId(p);
        var (file, _) = InventoryReadTrace.Capture(f.Read, id, p);
        var path = Path.Combine(AppContext.BaseDirectory, "synthetic-inventory-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllBytes(path, file);
            Assert.Equal(0, InventoryTraceCommand.Run(["replay-inventory", "--input", path, "--session", id.Session]));
            Assert.Equal(2, InventoryTraceCommand.Run(["replay-inventory", "--input", path, "--session", "wrong-session"]));
        }
        finally { File.Delete(path); }
    }
    [Theory] [InlineData(1, false)] [InlineData(335, true)]
    public async Task ValidSyntheticTraceReplaysInActualOfflineSubprocess(int count, bool retry)
    {
        var f = count == 1 ? TraceFixture() : RegisteredFrame(count);
        var p = JsonSerializer.Deserialize<MemoryProfile[]>(Program.ProfileJson)!.Single();
        var id = TraceId(p); var rawReads = 0;
        byte[] Read(ulong address, int length)
        {
            var data = f.Read(address, length);
            if (retry && address == Fixture.Unit + 0x178 && ++rawReads == 2) data[0] ^= 8;
            return data;
        }
        var (file, capture) = InventoryReadTrace.Capture(Read, id, p);
        Assert.True(capture.Complete);
        if (retry) Assert.True(capture.Calls > 1024);
        var evidencePath = Environment.GetEnvironmentVariable(count == 1
            ? "INVENTORY_TRACE_SYNTHETIC_FILE" : "INVENTORY_TRACE_REPRESENTATIVE_FILE");
        var path = evidencePath ?? Path.Combine(AppContext.BaseDirectory, "synthetic-replay-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllBytes(path, file);
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "RandyPickLiveValidation.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                ArgumentList = { "replay-inventory", "--input", path, "--session", id.Session }
            })!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = await process.StandardError.ReadToEndAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("", stderr);
            using var report = JsonDocument.Parse(stdout);
            Assert.Equal("offline-replay", report.RootElement.GetProperty("Evidence").GetString());
            Assert.True(report.RootElement.GetProperty("Complete").GetBoolean());
            Assert.Equal(1, report.RootElement.GetProperty("Owned").GetInt32());
            Assert.Equal(count, report.RootElement.GetProperty("FrameCount").GetInt32());
            Assert.Equal(capture.Calls, report.RootElement.GetProperty("Calls").GetInt32());
            Assert.False(report.RootElement.GetProperty("Verified").GetBoolean());
            Console.WriteLine($"valid synthetic subprocess exit={process.ExitCode} count={count} calls={capture.Calls} bytes={file.Length} path={path}");
        }
        finally { if (evidencePath is null) File.Delete(path); }
    }
    [Fact] public void BadIdentityLimitsIncompleteAndCorruptFilesReject()
    {
        var f = TraceFixture(); var p = Profile(); var id = TraceId(p);
        var (file, _) = InventoryReadTrace.Capture(f.Read, id, p);
        var payload = InventoryReadTrace.Decode(file, p);
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(file, p, id with { Session = "another" }));
        var otherBounds = Profile("\"MinimumUnitObjects\":1=\"MinimumUnitObjects\":2");
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(file, otherBounds));
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(Repack(payload with { Identity = id with { Version = "older" } }), p));
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(Repack(payload with { Complete = false }), p));
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(Repack(payload with { Calls = [new(Fixture.B, 65537, new byte[1], null)] }), p));
        var corrupt = (byte[])file.Clone(); corrupt[^10] ^= 1;
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(corrupt, p));
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(file[..^1], p));
        long ticks = 0;
        var (capped, result) = InventoryReadTrace.Capture(f.Read, id, p, () => ticks += 10, 10);
        Assert.False(result.Complete);
        Assert.Throws<InvalidDataException>(() => InventoryReadTrace.Replay(capped, p));
    }
}
