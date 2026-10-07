using System.ComponentModel;

namespace OrandOverlay;

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

    internal static void ValidateProfile(MemoryProfile p, ICollection<string> errors)
    {
        if (p.FileVersion != Version || !p.Sha256.Equals(Hash, StringComparison.OrdinalIgnoreCase) || p.LegacySha256 != "" ||
            p.ModuleName != "Warcraft III.exe" || p.LocatorKind != MemoryLocatorKind.StructuralScan ||
            p.CountOffset != 0xC08 || p.EntriesPointerOffset != 0xC10 || p.OwnerOffset != 0x1C0 ||
            p.OwnerFieldBytes != 4 || p.RawcodeOffset != 0x178 || p.EntryStride != 8 ||
            p.EntryPointerOffset != 0 || p.EntriesAreInline || !p.EntriesContainPointers ||
            p.PointerOffsets.Length != 0 || p.OwnerPointerOffsets.Length != 0 || p.RawcodePointerOffsets.Length != 0 ||
            p.ModuleOffset != 0 || p.Signature != "" || p.RelativeDisplacementOffset != 0 || p.InstructionLength != 0 ||
            p.LocalPlayerRootOffsetA != 0 || p.LocalPlayerRootOffsetB != 0 || p.LocalPlayerRootXorHex != "" ||
            p.LocalPlayerIdOffset != 0 || p.LocalPlayerSlot != 0 || p.UnitClassName != ".?AVCUnit@@")
            errors.Add("Experimental layout requires exact build/hash, DWORD owner and pinned structure; legacy overrides forbidden");
    }

    internal static bool SessionAllows(MemoryProfile p, bool verificationSession) =>
        MemoryProfileValidator.Validate(p).Count == 0 &&
        p.Enabled && (p.KnownExperimental ? verificationSession : (p.Verified || verificationSession));

    internal static ulong DecodeRoot(ulong encoded) => unchecked(
        ((((encoded << 29) | (encoded >> 35)) + 0x5BE06F37FC9B5B29UL) ^ 0x3A11C7B7EF67132BUL) + 0x2D2C27903E7F5D3DUL);

    internal static byte Owner(uint owner) => owner <= 27 ? (byte)owner :
        throw new InvalidDataException("Diagnostic DWORD owner outside 0..27");

    private static byte[] Exact(Func<ulong, int, byte[]> read, ulong address, int size)
    {
        if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(address)) throw new InvalidDataException("Invalid diagnostic pointer");
        var bytes = read(address, size);
        if (bytes.Length != size) throw new InvalidDataException("Incomplete diagnostic read");
        return bytes;
    }
    private static ulong Q(Func<ulong, int, byte[]> read, ulong a) => BitConverter.ToUInt64(Exact(read, a, 8));
    private static uint D(Func<ulong, int, byte[]> read, ulong a) => BitConverter.ToUInt32(Exact(read, a, 4));
    private static void Type(Func<ulong, int, byte[]> read, ulong a, ulong b, long rva)
    {
        if (Q(read, a) != AddressMath.Add(b, rva)) throw new InvalidDataException("Diagnostic primary vtable mismatch");
    }
    internal readonly record struct View(ulong Root, ushort Slot, ulong Player);
    private static View ReadViewOnce(Func<ulong, int, byte[]> read, ulong b)
    {
        var root = DecodeRoot(Q(read, AddressMath.Add(b, EncodedRootRva)));
        Type(read, root, b, GameVtable);
        if (D(read, AddressMath.Add(root, 0x2698)) != 28) throw new InvalidDataException("CGameWar3 table count must be 28");
        var slot = BitConverter.ToUInt16(Exact(read, AddressMath.Add(root, 0x262C), 2));
        if (slot > 23) throw new InvalidDataException("CURRENT-VIEW slot outside 0..23");
        var player = Q(read, AddressMath.Add(root, 0x26A0 + slot * 8)); // INLINE, not pointer-to-array
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
        var ui = Q(read, AddressMath.Add(b, GameUiGlobalA));
        if (ui != Q(read, AddressMath.Add(b, GameUiGlobalB)))
            throw new InvalidDataException("Diagnostic GameUI globals disagree");
        Type(read, ui, b, GameUiVtable);
        return ui;
    }
    internal sealed record Unit(ulong Address, byte Owner, uint Rawcode, Warcraft300HandleStamp Allocation);
    internal readonly record struct UnregisteredEntry(int Index, ulong Address, ulong Vtable, uint Handle, uint Serial);
    internal sealed record Inventory(View CurrentView, int Count, int Owned, int Foreign, Dictionary<uint, int> Rawcodes)
    {
        // Keep every frame slot, including aliases, separate from unique physical units.
        internal IReadOnlyList<ulong> EntryAddresses { get; init; } = Array.Empty<ulong>();
        internal IReadOnlyList<Unit> Units { get; init; } = Array.Empty<Unit>();
        internal IReadOnlyList<UnregisteredEntry> UnregisteredEntries { get; init; } = Array.Empty<UnregisteredEntry>();
        internal bool SameWorldEntries(Inventory other) => CurrentView == other.CurrentView && Count == other.Count &&
            Owned == other.Owned && Foreign == other.Foreign && EntryAddresses.SequenceEqual(other.EntryAddresses) &&
            Units.SequenceEqual(other.Units) &&
            UnregisteredEntries.SequenceEqual(other.UnregisteredEntries);
    }
    internal static Inventory ReadInventory(Func<ulong, int, byte[]> read, ulong b, ulong root, MemoryProfile p,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!p.KnownExperimental || MemoryProfileValidator.Validate(p).Count != 0)
            throw new InvalidDataException("Invalid diagnostic profile");

        byte[] Read(ulong address, int size)
        {
            token.ThrowIfCancellationRequested();
            var bytes = read(address, size);
            token.ThrowIfCancellationRequested();
            return bytes;
        }

        try { return ReadInventoryOnce(Read, b, root, p, token); }
        catch (SnapshotMutationException first)
        {
            token.ThrowIfCancellationRequested();
            try { return ReadInventoryOnce(Read, b, root, p, token, first); }
            catch (SnapshotMutationException second)
            {
                throw new InvalidDataException("Diagnostic inventory changed during both snapshot attempts", second);
            }
        }
    }

    private readonly record struct SnapshotContext(View View, ulong Ui);
    private sealed class SnapshotMutationException(SnapshotContext context, List<Unit> units,
        List<UnregisteredEntry> unregistered) : InvalidOperationException
    {
        internal SnapshotContext Context { get; } = context;
        internal Dictionary<ulong, Unit> Units { get; } = units.ToDictionary(unit => unit.Address);
        internal Dictionary<ulong, UnregisteredEntry> Unregistered { get; } = unregistered
            .GroupBy(entry => entry.Address).ToDictionary(group => group.Key, group => group.First());
    }

    private static SnapshotContext ReadContext(Func<ulong, int, byte[]> read, ulong b, ulong root)
    {
        var context = new SnapshotContext(ReadView(read, b), ReadUi(read, b));
        Type(read, root, b, FrameVtable);
        if (Q(read, AddressMath.Add(root, 0x40)) != context.Ui)
            throw new InvalidDataException("Diagnostic world frame belongs to another UI");
        return context;
    }

    private static Inventory ReadInventoryOnce(Func<ulong, int, byte[]> read, ulong b, ulong root, MemoryProfile p,
        CancellationToken token, SnapshotMutationException? retryFence = null)
    {
        var context = ReadContext(read, b, root);
        if (retryFence is not null && context != retryFence.Context)
            throw new InvalidDataException("Diagnostic world/view changed between snapshot attempts");
        var view = context.View;
        var count = D(read, AddressMath.Add(root, 0xC08));
        if (count < p.MinimumUnitObjects || count > p.MaximumUnits) throw new InvalidDataException("Diagnostic frame count outside bounds");
        var entries = Q(read, AddressMath.Add(root, 0xC10));
        var vector = Exact(read, entries, checked((int)count * 8));
        var entryAddresses = new ulong[checked((int)count)];
        var identities = new Dictionary<ulong, UnregisteredEntry>();
        var registered = new Dictionary<ulong, Unit>();
        var counts = new Dictionary<uint, int>();
        var units = new List<Unit>();
        var unregistered = new List<UnregisteredEntry>();
        UnregisteredEntry Identity(int index, ulong address)
        {
            token.ThrowIfCancellationRequested();
            if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(address) || address > 0x7FFFFFFFFFFFUL - 0x1F)
                throw new InvalidDataException("Invalid diagnostic identity header span");
            var vtable = Q(read, address);
            if (vtable != AddressMath.Add(b, UnitVtable)) throw new InvalidDataException("Diagnostic primary vtable mismatch");
            token.ThrowIfCancellationRequested();
            var handle = D(read, AddressMath.Add(address, 0x18));
            token.ThrowIfCancellationRequested();
            var serial = D(read, AddressMath.Add(address, 0x1C));
            token.ThrowIfCancellationRequested();
            return new(index, address, vtable, handle, serial);
        }
        var owned = 0;
        var rawcodesChanged = false;
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var unit = BitConverter.ToUInt64(vector, i * 8);
            entryAddresses[i] = unit;
            var identity = Identity(i, unit);
            if (identities.TryGetValue(unit, out var firstIdentity))
            {
                if ((firstIdentity with { Index = i }) != identity)
                    throw new InvalidDataException("Diagnostic aliased CUnit identity changed between frame slots");
            }
            else identities.Add(unit, identity);
            Unit? previous = null;
            if (retryFence is not null)
            {
                retryFence.Units.TryGetValue(unit, out previous);
                if (previous is not null && (identity.Vtable != previous.Allocation.Vtable ||
                    identity.Handle != previous.Allocation.RawHandle || identity.Serial != previous.Allocation.Serial))
                    throw new InvalidDataException("Diagnostic CUnit identity changed between snapshot attempts");
                if (retryFence.Unregistered.TryGetValue(unit, out var excluded) &&
                    (identity.Vtable != excluded.Vtable || identity.Handle != excluded.Handle || identity.Serial != excluded.Serial))
                    throw new InvalidDataException("Unregistered world identity changed between snapshot attempts");
            }
            // An exact unregistered identity cannot supply a card or allocation stamp.
            // Preserve and recheck its vector position; this says nothing about gameplay life.
            if (identity.Handle == uint.MaxValue && identity.Serial == uint.MaxValue)
            {
                unregistered.Add(identity);
                continue;
            }
            var allocation = Warcraft300HandleValidator.Read(read, b, unit, token);
            if (identity.Vtable != allocation.Vtable || identity.Handle != allocation.RawHandle || identity.Serial != allocation.Serial)
                throw new InvalidDataException("Diagnostic CUnit identity changed before allocation validation");
            var owner = Owner(D(read, AddressMath.Add(unit, 0x1C0)));
            if (previous is not null && (allocation != previous.Allocation || owner != previous.Owner))
                throw new InvalidDataException("Diagnostic CUnit ownership/allocation changed between snapshot attempts");
            var rawcode = D(read, AddressMath.Add(unit, 0x178));
            // Every alias still validates its identity and full registry stamp. Only
            // counting is deduplicated; a changed allocation or owner never retries.
            if (registered.TryGetValue(unit, out var firstUnit))
            {
                if (allocation != firstUnit.Allocation || owner != firstUnit.Owner)
                    throw new InvalidDataException("Diagnostic aliased CUnit ownership/allocation changed between frame slots");
                rawcodesChanged |= rawcode != firstUnit.Rawcode;
                continue;
            }
            var observed = new Unit(unit, owner, rawcode, allocation);
            registered.Add(unit, observed);
            units.Add(observed);
            if (owner != view.Slot) continue;
            owned++;
            counts[rawcode] = counts.GetValueOrDefault(rawcode) + 1;
        }
        foreach (var unit in units)
        {
            token.ThrowIfCancellationRequested();
            Type(read, unit.Address, b, UnitVtable);
            if (Owner(D(read, AddressMath.Add(unit.Address, 0x1C0))) != unit.Owner)
                throw new InvalidDataException("Diagnostic CUnit owner changed");
            rawcodesChanged |= D(read, AddressMath.Add(unit.Address, 0x178)) != unit.Rawcode;
            if (unit.Allocation != Warcraft300HandleValidator.Read(read, b, unit.Address, token))
                throw new InvalidDataException("Diagnostic CUnit allocated handle generation changed");
        }
        foreach (var entry in unregistered)
            if (Identity(entry.Index, entry.Address) != entry)
                throw new InvalidDataException("Unregistered world entry changed during inventory read");
        var closingCount = D(read, AddressMath.Add(root, 0xC08));
        if (closingCount < p.MinimumUnitObjects || closingCount > p.MaximumUnits)
            throw new InvalidDataException("Diagnostic closing frame count outside bounds");
        var closingEntries = Q(read, AddressMath.Add(root, 0xC10));
        if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(closingEntries))
            throw new InvalidDataException("Invalid diagnostic closing entries pointer");
        var vectorChanged = closingCount != count || closingEntries != entries ||
            !vector.AsSpan().SequenceEqual(Exact(read, entries, vector.Length));
        if (ReadContext(read, b, root) != context)
            throw new InvalidDataException("Diagnostic frame/view changed");
        // Retry only a validated same-context mutation. Identity, ownership, allocation,
        // and world/view failures must never be hidden by a second successful read.
        if (rawcodesChanged || vectorChanged) throw new SnapshotMutationException(context, units, unregistered);
        return new(view, (int)count, owned, units.Count - owned, counts)
        { EntryAddresses = Array.AsReadOnly(entryAddresses), Units = units.AsReadOnly(), UnregisteredEntries = unregistered.AsReadOnly() };
    }
}
