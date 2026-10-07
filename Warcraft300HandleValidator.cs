using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;

namespace OrandOverlay;

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
