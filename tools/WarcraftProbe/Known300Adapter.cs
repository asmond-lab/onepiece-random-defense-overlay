using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace WarcraftProbe;

/// <summary>Exact known-build, current-view allocation diagnostics only; never gameplay inventory.</summary>
internal static class Known300Adapter
{
    internal const string Hash = "BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12";
    internal const string Version = "3.0.0.24268";
    internal const string Profile = "Warcraft30024268Diagnostic";
    internal static bool Allows(ImageInfo image) => image.Sha256.Equals(Hash, StringComparison.OrdinalIgnoreCase) && image.FileVersion == Version && image.Machine == 0x8664;
    internal static bool IsPointer(ulong p) => p >= 0x10000 && p <= 0x7FFFFFFFFFFF;
    internal static ulong Add(ulong p, long offset)
    {
        try { var a = offset >= 0 ? checked(p + (ulong)offset) : checked(p - (ulong)checked(-offset)); if (!IsPointer(a)) throw new InvalidDataException("Structure address outside user range"); return a; }
        catch (OverflowException ex) { throw new InvalidDataException("Structure address overflow", ex); }
    }
    internal static StructureInfo Unknown(string reason, double elapsed = 0) => new(Profile, "Unknown", elapsed, null, null, null, null, Array.Empty<RawcodeCount>(), null, null, null, reason);
    internal static string RawcodeLabel(uint raw)
    {
        var b = BitConverter.GetBytes(raw);
        return b.All(c => c >= 0x20 && c <= 0x7e) ? Encoding.ASCII.GetString(b) : "0x" + raw.ToString("X8");
    }
    internal static string ContextToken(int pid, DateTimeOffset start, params ulong[] addresses)
    {
        var bytes = new byte[12 + addresses.Length * 8]; BinaryPrimitives.WriteInt32LittleEndian(bytes, pid);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(4), start.UtcDateTime.Ticks);
        for (var i = 0; i < addresses.Length; i++) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(12 + i * 8), addresses[i]);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    private readonly record struct Vm(ulong Root, ulong A, ulong B);
    private static Vm ReadVm(Func<ulong, int, byte[]> read, ulong module, ulong root)
    {
        ulong Q(ulong a) { var b = read(a, 8); WindowsCollector.Need(b is not null && b.Length == 8, "Partial VM pin"); return BitConverter.ToUInt64(b!); }
        Vm Once()
        {
            WindowsCollector.Need(Q(root) == Add(module, Warcraft300Diagnostic.GameVtable), "VM owner root type mismatch");
            var a = Q(Add(root, 0x25D0)); var b = Q(Add(root, 0x25E0));
            WindowsCollector.Need(IsPointer(a) && IsPointer(b), "VM root unavailable");
            WindowsCollector.Need(Q(a) == Add(module, 0x27ECBC0) && Q(b) == Add(module, 0x27ECC40), "VM root type mismatch"); return new(root, a, b);
        }
        var first = Once(); WindowsCollector.Need(first == Once(), "VM roots changed"); return first;
    }
    internal static StructureInfo Capture(ImageInfo image, ulong module, int pid, DateTimeOffset start,
        Func<ulong, int, byte[]> read, Action budgetCheck, Action pin, CancellationToken token = default, Func<TimeSpan>? elapsed = null, Action<Action?>? setReadCheck = null)
    {
        if (!Allows(image)) return Unknown("Unknown build: no structure reads");
        var clock = Stopwatch.StartNew(); elapsed ??= () => clock.Elapsed;
        void Check() { token.ThrowIfCancellationRequested(); budgetCheck(); var t = elapsed(); WindowsCollector.Need(t >= TimeSpan.Zero && t < TimeSpan.FromSeconds(3), "Structure deadline reached"); }
        byte[] Exact(ulong a, int n) { Check(); WindowsCollector.Range(a, n); var result = read(a, n); Check(); WindowsCollector.Need(result is not null && result.Length == n, "Partial structure read"); return result!; }
        try
        {
            setReadCheck?.Invoke(Check); Check(); pin(); Check(); // The three-second budget includes the world locator and every replay.
            var world = Warcraft300WorldLocator.Read(Exact, module, token);
            var view = Warcraft300Diagnostic.ReadView(Exact, module);
            var vm = ReadVm(Exact, module, view.Root);
            var inventory = Warcraft300Diagnostic.ReadInventory(Exact, module, world.World, token);
            WindowsCollector.Need(inventory.CurrentView == view, "View changed before inventory");
            WindowsCollector.Need(world == Warcraft300WorldLocator.Read(Exact, module, token), "World context changed");
            WindowsCollector.Need(view == Warcraft300Diagnostic.ReadView(Exact, module), "View context changed");
            WindowsCollector.Need(vm == ReadVm(Exact, module, view.Root), "VM context changed");
            pin(); Check();
            var rows = inventory.Rawcodes.OrderBy(p => p.Key).Select(p => new RawcodeCount(RawcodeLabel(p.Key), p.Value)).ToArray();
            var result = new StructureInfo(Profile, "Observed", elapsed().TotalMilliseconds, view.Slot, inventory.Count, inventory.Owned, inventory.Foreign,
                rows, ContextToken(pid, start, view.Root, world.World), ContextToken(pid, start, world.Ui), ContextToken(pid, start, vm.A, vm.B), null);
            Check(); return result;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or Win32Exception or UnauthorizedAccessException or OperationCanceledException or OverflowException or InvalidOperationException or ArgumentException)
        {
            var reason = ex is OperationCanceledException ? "Cancelled" : ex is InvalidDataException ? ex.Message : "Structure read unavailable";
            return Unknown(reason, elapsed().TotalMilliseconds);
        }
        finally { setReadCheck?.Invoke(null); }
    }
}

// Diagnostic CURRENT-VIEW identity is deliberately not an immutable local-player identity.
internal static class Warcraft300Diagnostic
{
    internal const string LayoutName = "Warcraft30024268Diagnostic";
    internal const string Version = "3.0.0.24268";
    internal const string Hash = "BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12";
    internal const long EncodedRootRva = 0x2E9AD00;
    internal const long GameVtable = 0x26C8C70, PlayerVtable = 0x26C87D8;
    internal const long FrameVtable = 0x2764A20, UnitVtable = 0x2792E78;
    internal const long GameUiVtable = 0x275ED08, GameUiGlobalA = 0x2F5EF00, GameUiGlobalB = 0x2F85360;

    internal static ulong DecodeRoot(ulong encoded) => unchecked(
        ((((encoded << 29) | (encoded >> 35)) + 0x5BE06F37FC9B5B29UL) ^ 0x3A11C7B7EF67132BUL) + 0x2D2C27903E7F5D3DUL);

    internal static byte Owner(uint owner) => owner <= 27 ? (byte)owner :
        throw new InvalidDataException("Diagnostic DWORD owner outside 0..27");

    private static byte[] Exact(Func<ulong, int, byte[]> read, ulong address, int size)
    {
        if (!Known300Adapter.IsPointer(address)) throw new InvalidDataException("Invalid diagnostic pointer");
        var bytes = read(address, size);
        if (bytes.Length != size) throw new InvalidDataException("Incomplete diagnostic read");
        return bytes;
    }
    private static ulong Q(Func<ulong, int, byte[]> read, ulong a) => BitConverter.ToUInt64(Exact(read, a, 8));
    private static uint D(Func<ulong, int, byte[]> read, ulong a) => BitConverter.ToUInt32(Exact(read, a, 4));
    private static void Type(Func<ulong, int, byte[]> read, ulong a, ulong b, long rva)
    {
        if (Q(read, a) != Known300Adapter.Add(b, rva)) throw new InvalidDataException("Diagnostic primary vtable mismatch");
    }
    internal readonly record struct View(ulong Root, ushort Slot, ulong Player);
    private static View ReadViewOnce(Func<ulong, int, byte[]> read, ulong b)
    {
        var root = DecodeRoot(Q(read, Known300Adapter.Add(b, EncodedRootRva)));
        Type(read, root, b, GameVtable);
        if (D(read, Known300Adapter.Add(root, 0x2698)) != 28) throw new InvalidDataException("CGameWar3 table count must be 28");
        var slot = BitConverter.ToUInt16(Exact(read, Known300Adapter.Add(root, 0x262C), 2));
        if (slot > 23) throw new InvalidDataException("CURRENT-VIEW slot outside 0..23");
        var player = Q(read, Known300Adapter.Add(root, 0x26A0 + slot * 8)); // INLINE, not pointer-to-array
        Type(read, player, b, PlayerVtable);
        return new(root, slot, player);
    }
    internal static View ReadView(Func<ulong, int, byte[]> read, ulong b)
    {
        var before = ReadViewOnce(read, b);
        if (before != ReadViewOnce(read, b)) throw new InvalidDataException("CURRENT-VIEW root/slot/player changed");
        return before;
    }
    private static ulong ReadUi(Func<ulong, int, byte[]> read, ulong b)
    {
        var ui = Q(read, Known300Adapter.Add(b, GameUiGlobalA));
        if (ui != Q(read, Known300Adapter.Add(b, GameUiGlobalB)))
            throw new InvalidDataException("Diagnostic GameUI globals disagree");
        Type(read, ui, b, GameUiVtable);
        return ui;
    }
    internal sealed record Unit(ulong Address, byte Owner, uint Rawcode, Warcraft300HandleStamp Allocation);
    internal sealed record Inventory(View CurrentView, int Count, int Owned, int Foreign, Dictionary<uint, int> Rawcodes)
    {
        internal IReadOnlyList<Unit> Units { get; init; } = Array.Empty<Unit>();
    }
    internal static Inventory ReadInventory(Func<ulong, int, byte[]> read, ulong b, ulong root, CancellationToken token = default)
    {
        var view = ReadView(read, b);
        var ui = ReadUi(read, b);
        Type(read, root, b, FrameVtable);
        if (Q(read, Known300Adapter.Add(root, 0x40)) != ui)
            throw new InvalidDataException("Diagnostic world frame belongs to another UI");
        var count = D(read, Known300Adapter.Add(root, 0xC08));
        if (count < 1 || count > 32768) throw new InvalidDataException("Diagnostic frame count outside bounds");
        var entries = Q(read, Known300Adapter.Add(root, 0xC10));
        var vector = Exact(read, entries, checked((int)count * 8));
        var seen = new HashSet<ulong>();
        var counts = new Dictionary<uint, int>();
        var units = new List<Unit>();
        var owned = 0;
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var unit = BitConverter.ToUInt64(vector, i * 8);
            if (!seen.Add(unit)) throw new InvalidDataException("Duplicate diagnostic CUnit pointer");
            var allocation = Warcraft300HandleValidator.Read(read, b, unit, token);
            var owner = Owner(D(read, Known300Adapter.Add(unit, 0x1C0)));
            var rawcode = D(read, Known300Adapter.Add(unit, 0x178));
            units.Add(new(unit, owner, rawcode, allocation));
            if (owner != view.Slot) continue;
            owned++;
            counts[rawcode] = counts.GetValueOrDefault(rawcode) + 1;
        }
        foreach (var unit in units)
        {
            token.ThrowIfCancellationRequested();
            Type(read, unit.Address, b, UnitVtable);
            if (Owner(D(read, Known300Adapter.Add(unit.Address, 0x1C0))) != unit.Owner ||
                D(read, Known300Adapter.Add(unit.Address, 0x178)) != unit.Rawcode ||
                unit.Allocation != Warcraft300HandleValidator.Read(read, b, unit.Address, token))
                throw new InvalidDataException("Diagnostic CUnit/allocated handle generation changed");
        }
        Type(read, root, b, FrameVtable);
        if (D(read, Known300Adapter.Add(root, 0xC08)) != count || Q(read, Known300Adapter.Add(root, 0xC10)) != entries ||
            !vector.AsSpan().SequenceEqual(Exact(read, entries, vector.Length)) || view != ReadView(read, b) ||
            ui != ReadUi(read, b) || Q(read, Known300Adapter.Add(root, 0x40)) != ui)
            throw new InvalidDataException("Diagnostic frame/view changed");
        return new(view, (int)count, owned, (int)count - owned, counts) { Units = units.AsReadOnly() };
    }
}

// Pure diagnostic locator for the caller's exact-version/hash-gated 3.0.0.24268 session.
// This discovers a typed world frame, NOT local-player identity or alive-unit evidence.
// There is no heap scan, absolute-address fallback, or game-function execution.
internal static class Warcraft300WorldLocator
{
    internal sealed record Context(ulong K, ulong KeyA, ulong KeyB, ulong EncodedUi,
        ulong EncodedWorld, byte ByteA, byte ByteB, ulong Ui, ulong World);

    internal static Context Read(Func<ulong, int, byte[]> read, ulong moduleBase, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(read);
        token.ThrowIfCancellationRequested();
        Pointer(moduleBase);
        var before = Once(read, moduleBase, token);
        var after = Once(read, moduleBase, token);
        token.ThrowIfCancellationRequested();
        if (before != after) throw new InvalidDataException("Diagnostic world locator changed between reads");
        return before;
    }

    private static void Pointer(ulong address)
    {
        if (!Known300Adapter.IsPointer(address))
            throw new InvalidDataException("Diagnostic world locator unavailable: invalid pointer");
    }

    private static byte[] Exact(Func<ulong, int, byte[]> read, ulong address, int count, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Pointer(address);
        // Validate the entire read span, including an unaligned eight-byte key.
        Pointer(Known300Adapter.Add(address, count - 1));
        byte[] bytes;
        try { bytes = read(address, count); }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        { throw new InvalidDataException("Diagnostic world locator unavailable: inaccessible memory", ex); }
        token.ThrowIfCancellationRequested();
        if (bytes is null || bytes.Length != count)
            throw new InvalidDataException("Diagnostic world locator unavailable: incomplete read");
        return bytes;
    }

    private static Context Once(Func<ulong, int, byte[]> read, ulong b, CancellationToken token)
    {
        ulong Q(ulong a) => BinaryPrimitives.ReadUInt64LittleEndian(Exact(read, a, 8, token));
        byte Byte(ulong a) => Exact(read, a, 1, token)[0];
        void Type(ulong p, long rva)
        {
            if (Q(p) != Known300Adapter.Add(b, rva))
                throw new InvalidDataException("Diagnostic world locator primary vtable mismatch");
        }

        var k = Q(Known300Adapter.Add(b, 0x2F56EF8));
        Pointer(k);
        var keyA = Q(Known300Adapter.Add(k, 0x1B4));
        var keyB = Q(Known300Adapter.Add(k, 0x19A)); // Deliberately unaligned; do not round or widen.
        var encodedUi = Q(Known300Adapter.Add(b, 0x2FDD260));
        var byteA = Byte(Known300Adapter.Add(b, 0x2E8DC69));
        var byteB = Byte(Known300Adapter.Add(b, 0x2E8DE3E));
        ulong ui;
        unchecked
        {
            var v = BitOperations.RotateLeft((encodedUi ^ keyA) - 0x52E7B4144FB4B0E9UL, 12);
            v += 0xF4339B63841E7E5EUL;
            v += 0xFA3CC4C012B9EF8EUL;
            v ^= byteA;
            v ^= 0x5A64008D1F97DB7AUL;
            ui = BitOperations.RotateLeft(v, 23);
        }
        // Zero/invalid decoded roots (including menus) are Unavailable, never forced roots.
        Type(ui, Warcraft300Diagnostic.GameUiVtable);
        var globalA = Q(Known300Adapter.Add(b, Warcraft300Diagnostic.GameUiGlobalA));
        var globalB = Q(Known300Adapter.Add(b, Warcraft300Diagnostic.GameUiGlobalB));
        if (globalA != ui || globalB != ui)
            throw new InvalidDataException("Diagnostic world locator UI globals disagree");
        var encodedWorld = Q(Known300Adapter.Add(ui, 0x6A8));
        ulong world;
        unchecked
        {
            var v = BitOperations.RotateLeft(encodedWorld, 24) + 0x9BE33DE0422B8111UL;
            v = BitOperations.RotateLeft(v, 13) ^ 0xC93E2E7D3C27237DUL;
            v += 0xBBAD6A13B280A99CUL;
            world = v ^ keyB ^ byteB;
        }
        Type(world, Warcraft300Diagnostic.FrameVtable);
        if (Q(Known300Adapter.Add(world, 0x40)) != ui)
            throw new InvalidDataException("Diagnostic world locator frame belongs to another UI");
        // Every field and every structural assertion is read again by the second pass.
        // This detects observed changes, not an atomic snapshot or an ABA-proof lock.
        return new(k, keyA, keyB, encodedUi, encodedWorld, byteA, byteB, ui, world);
    }
}

/// <summary>
/// Warcraft 3.0.0 allocation/registry validation only. This does not establish gameplay
/// life, ownership, or snapshot coherence. The caller must compare separate Read stamps
/// around its owner/rawcode/vector reads and independently validate the surrounding context.
/// No process access, native calls, heap discovery, or profile activation occurs here.
/// </summary>
internal static class Warcraft300HandleValidator
{
    internal const ulong RegistryGlobalRva = 0x2F807F0;
    internal const ulong UnitVtableRva = 0x2792E78;
    internal const uint UnitTypeId = 0x2B61676C;
    // Same defensive ceiling as the bounded UnitIdentity probe, not a claim about native
    // capacity. It bounds the selected table span to 256 MiB; no table is bulk-read.
    internal const uint MaximumTableLimit = 16_777_216;
    private const ulong MinimumAddress = 0x10000;
    private const ulong MaximumAddress = 0x00007FFFFFFFFFFF;

    /// <summary>
    /// Returns a value-comparable, registry-valid observation or throws InvalidDataException.
    /// Cancellation and I/O exceptions propagate; arithmetic overflow is wrapped in
    /// InvalidDataException. No failure is converted into a stamp.
    /// The raw handle and both serials are independent four-byte unsigned values.
    /// </summary>
    internal static Warcraft300HandleStamp Read(Func<ulong, int, byte[]> read,
        ulong moduleBase, ulong unit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(read);
        token.ThrowIfCancellationRequested();
        try
        {
            Range(moduleBase, 1);
            Range(unit, 0x20);
            var registryGlobal = checked(moduleBase + RegistryGlobalRva);
            var expectedVtable = checked(moduleBase + UnitVtableRva);
            Range(expectedVtable, 8);
            var vtable = U64(unit);
            Require(vtable == expectedVtable, "CUnit vtable mismatch.");
            var handle = U32(checked(unit + 0x18));
            var serial = U32(checked(unit + 0x1C));
            var registry = U64(registryGlobal);
            Range(registry, 0x6C);
            var alternate = (handle & 0x80000000U) != 0;
            var index = handle & 0x7FFFFFFFU;
            var limit = U32(checked(registry + (alternate ? 0x68UL : 0x30UL)));
            Require(limit > 0 && limit <= MaximumTableLimit && index < limit,
                "Handle index or table limit is outside safety bounds.");
            var table = U64(checked(registry + (alternate ? 0x50UL : 0x18UL)));
            Range(table, checked(16UL * limit));
            var slot = checked(table + checked(16UL * index));
            var marker = U32(slot);
            Require(marker == 0xFFFFFFFEU, "Handle slot is not allocated.");
            var record = U64(checked(slot + 8));
            Range(record, 0x98);
            var recordSerial = U32(checked(record + 0x24));
            Require(recordSerial == serial, "Handle serial mismatch.");
            var typeId = U32(checked(record + 0x18));
            Require(typeId == UnitTypeId, "Registry type mismatch.");
            var backReference = U64(checked(record + 0x90));
            Require(backReference == unit, "Registry back-reference mismatch.");
            var state30 = U64(checked(record + 0x30));
            Require(state30 == 0, "Registry state at +0x30 rejected.");
            var state83 = Bytes(checked(record + 0x83), 1)[0];
            Require((state83 & 1) == 0, "Registry state at +0x83 rejected.");
            token.ThrowIfCancellationRequested();
            return new(moduleBase, unit, vtable, registryGlobal, registry, handle, serial,
                table, limit, slot, marker, record, recordSerial, typeId, backReference,
                state30, state83);
        }
        catch (OverflowException error)
        {
            throw new InvalidDataException("Registry address arithmetic overflow.", error);
        }

        byte[] Bytes(ulong address, int length)
        {
            token.ThrowIfCancellationRequested();
            Range(address, checked((ulong)length));
            var bytes = read(address, length);
            token.ThrowIfCancellationRequested();
            Require(bytes is not null && bytes.Length == length, "Registry read must return exactly the requested bytes.");
            return bytes!;
        }
        uint U32(ulong address) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(address, 4));
        ulong U64(ulong address) => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(address, 8));
    }

    private static void Range(ulong address, ulong length)
    {
        Require(length > 0 && address >= MinimumAddress && address <= MaximumAddress,
            "Implausible registry address.");
        Require(checked(address + length - 1) <= MaximumAddress, "Registry span exceeds user address bounds.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}

/// <summary>
/// One allocation/registry observation, not an atomic snapshot or a gameplay-state verdict.
/// All identity and accepted state bytes participate in value equality, including state83
/// bits other than bit zero. A matching pair still cannot exclude an intervening ABA change.
/// </summary>
internal readonly record struct Warcraft300HandleStamp(
    ulong ModuleBase, ulong Unit, ulong Vtable, ulong RegistryGlobal, ulong Registry,
    uint RawHandle, uint Serial, ulong Table, uint TableLimit, ulong Slot, uint Marker,
    ulong Record, uint RecordSerial, uint TypeId, ulong BackReference, ulong State30, byte State83)
{
    internal bool Alternate => (RawHandle & 0x80000000U) != 0;
    internal uint Index => RawHandle & 0x7FFFFFFFU;
}
