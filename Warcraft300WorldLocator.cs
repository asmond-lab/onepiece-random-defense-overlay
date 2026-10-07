using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Numerics;
using System.Threading;

namespace OrandOverlay;

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
        if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(address))
            throw new InvalidDataException("Diagnostic world locator unavailable: invalid pointer");
    }

    private static byte[] Exact(Func<ulong, int, byte[]> read, ulong address, int count, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Pointer(address);
        // Validate the entire read span, including an unaligned eight-byte key.
        Pointer(AddressMath.Add(address, count - 1));
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
            if (Q(p) != AddressMath.Add(b, rva))
                throw new InvalidDataException("Diagnostic world locator primary vtable mismatch");
        }

        var k = Q(AddressMath.Add(b, 0x2F56EF8));
        Pointer(k);
        var keyA = Q(AddressMath.Add(k, 0x1B4));
        var keyB = Q(AddressMath.Add(k, 0x19A)); // Deliberately unaligned; do not round or widen.
        var encodedUi = Q(AddressMath.Add(b, 0x2FDD260));
        var byteA = Byte(AddressMath.Add(b, 0x2E8DC69));
        var byteB = Byte(AddressMath.Add(b, 0x2E8DE3E));
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
        var globalA = Q(AddressMath.Add(b, Warcraft300Diagnostic.GameUiGlobalA));
        var globalB = Q(AddressMath.Add(b, Warcraft300Diagnostic.GameUiGlobalB));
        if (globalA != ui || globalB != ui)
            throw new InvalidDataException("Diagnostic world locator UI globals disagree");
        var encodedWorld = Q(AddressMath.Add(ui, 0x6A8));
        ulong world;
        unchecked
        {
            var v = BitOperations.RotateLeft(encodedWorld, 24) + 0x9BE33DE0422B8111UL;
            v = BitOperations.RotateLeft(v, 13) ^ 0xC93E2E7D3C27237DUL;
            v += 0xBBAD6A13B280A99CUL;
            world = v ^ keyB ^ byteB;
        }
        Type(world, Warcraft300Diagnostic.FrameVtable);
        if (Q(AddressMath.Add(world, 0x40)) != ui)
            throw new InvalidDataException("Diagnostic world locator frame belongs to another UI");
        // Every field and every structural assertion is read again by the second pass.
        // This detects observed changes, not an atomic snapshot or an ABA-proof lock.
        return new(k, keyA, keyB, encodedUi, encodedWorld, byteA, byteB, ui, world);
    }
}
