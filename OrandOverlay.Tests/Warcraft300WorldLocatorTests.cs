using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Warcraft300WorldLocatorTests
{
    private sealed class Fixture
    {
        internal const ulong B = 0x140000000, DefaultK = 0x210000000,
            DefaultUi = 0x250000000, DefaultWorld = 0x270000000;
        internal readonly Dictionary<ulong, byte> Bytes = new();
        internal readonly List<(ulong Address, int Count)> Reads = new();
        internal readonly ulong K, Ui, World, KeyA, KeyB, EncodedUi, EncodedWorld;
        internal readonly byte ByteA, ByteB;

        // Independent oracle: construct ciphertext BACKWARDS from chosen plaintext
        // pointers with arbitrary-precision modular arithmetic and RIGHT rotations.
        private static readonly BigInteger Mask = (BigInteger.One << 64) - 1;
        private static BigInteger Mod(BigInteger n) => n & Mask;
        private static BigInteger Ror(BigInteger n, int bits) =>
            Mod((Mod(n) >> bits) | (Mod(n) << (64 - bits)));
        internal Fixture(ulong keyA = 0xFEDCBA9876543210, ulong keyB = 0x8000000000000001,
            byte byteA = 0xD3, byte byteB = 0xA7, ulong k = DefaultK,
            ulong ui = DefaultUi, ulong world = DefaultWorld)
        {
            K = k; Ui = ui; World = world; KeyA = keyA; KeyB = keyB; ByteA = byteA; ByteB = byteB;
            var a = Ror(ui, 23) ^ 0x5A64008D1F97DB7AUL ^ byteA;
            a = Mod(a - 0xFA3CC4C012B9EF8EUL - 0xF4339B63841E7E5EUL);
            EncodedUi = (ulong)(Mod(Ror(a, 12) + 0x52E7B4144FB4B0E9UL) ^ keyA);
            var w = Mod(new BigInteger(world ^ keyB ^ byteB) - 0xBBAD6A13B280A99CUL);
            w ^= 0xC93E2E7D3C27237DUL;
            EncodedWorld = (ulong)Ror(Mod(Ror(w, 13) - 0x9BE33DE0422B8111UL), 24);
            Q(B + 0x2F56EF8, K); Q(K + 0x1B4, keyA); Q(K + 0x19A, keyB);
            Q(B + 0x2FDD260, EncodedUi);
            Bytes[B + 0x2E8DC69] = byteA; Bytes[B + 0x2E8DE3E] = byteB;
            Q(B + 0x2F5EF00, ui); Q(B + 0x2F85360, ui);
            Q(ui, B + 0x275ED08); Q(ui + 0x6A8, EncodedWorld);
            Q(world, B + 0x2764A20); Q(world + 0x40, ui);
        }
        internal void Q(ulong a, ulong value)
        {
            var bytes = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
            for (var i = 0; i < 8; i++) Bytes[unchecked(a + (ulong)i)] = bytes[i];
        }
        internal byte[] Read(ulong a, int n)
        {
            Reads.Add((a, n));
            // No padding of uninitialized addresses: any widened/read-around access fails.
            return Enumerable.Range(0, n).Select(i => Bytes.TryGetValue(a + (ulong)i, out var v)
                ? v : throw new Win32Exception(299)).ToArray();
        }
        internal Warcraft300WorldLocator.Context Run() => Warcraft300WorldLocator.Read(Read, B, default);
        internal (ulong Address, int Count)[] Pass => new[]
        {
            (B + 0x2F56EF8, 8), (K + 0x1B4, 8), (K + 0x19A, 8),
            (B + 0x2FDD260, 8), (B + 0x2E8DC69, 1), (B + 0x2E8DE3E, 1),
            (Ui, 8), (B + 0x2F5EF00, 8), (B + 0x2F85360, 8),
            (Ui + 0x6A8, 8), (World, 8), (World + 0x40, 8)
        };
    }

    [Theory]
    [InlineData(0UL, 0UL, (byte)0, (byte)0)]
    [InlineData(ulong.MaxValue, ulong.MaxValue, (byte)255, (byte)255)]
    [InlineData(0x8000000000000000UL, 1UL, (byte)128, (byte)1)]
    [InlineData(0xFEDCBA9876543210UL, 0x0123456789ABCDEFUL, (byte)211, (byte)167)]
    public void InverseFixturesDecodeWithFullWidthWrappingAndRotations(ulong ka, ulong kb, byte ba, byte bb)
    {
        var f = new Fixture(ka, kb, ba, bb);
        var result = f.Run();
        Assert.Equal(new Warcraft300WorldLocator.Context(f.K, ka, kb, f.EncodedUi,
            f.EncodedWorld, ba, bb, f.Ui, f.World), result);
        Assert.Equal(f.Pass.Concat(f.Pass), f.Reads);
        Assert.Equal(24, f.Reads.Count);
        Assert.Equal(164, f.Reads.Sum(r => r.Count));
        Assert.Equal(2UL, (f.K + 0x19A) % 8); // Unaligned qword, not a word/DWORD/aligned qword.
    }

    [Fact]
    public void EveryByteValueIsUnsignedAndExactlyOneByteWide()
    {
        for (var i = 0; i <= 255; i++)
        {
            var f = new Fixture(byteA: (byte)i, byteB: (byte)(255 - i));
            // Adjacent poisoned bytes must not participate in the decoder.
            f.Q(Fixture.B + 0x2E8DC6A, ulong.MaxValue);
            f.Q(Fixture.B + 0x2E8DE3F, ulong.MaxValue);
            Assert.Equal(f.World, f.Run().World);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void EveryFieldAndValidationIsRereadAndChangesRejected(int field)
    {
        var f = new Fixture(); var target = f.Pass[field].Address; var hits = 0;
        byte[] Read(ulong a, int n)
        {
            var bytes = f.Read(a, n);
            if (a == target && ++hits == 2) bytes[0] ^= 8;
            return bytes;
        }
        Assert.Throws<InvalidDataException>(() => Warcraft300WorldLocator.Read(Read, Fixture.B, default));
        Assert.Equal(2, hits);
    }

    [Theory]
    [InlineData("keyA")] [InlineData("keyB")] [InlineData("keyRoot")]
    [InlineData("uiRoot")] [InlineData("worldRoot")] [InlineData("byteA")] [InlineData("byteB")]
    public void CoherentSecondContextStillRejectedEvenWhenBothPassesAreValid(string changed)
    {
        var first = new Fixture();
        var second = new Fixture(
            keyA: changed == "keyA" ? 7UL : first.KeyA,
            keyB: changed == "keyB" ? 9UL : first.KeyB,
            byteA: changed == "byteA" ? (byte)11 : first.ByteA,
            byteB: changed == "byteB" ? (byte)13 : first.ByteB,
            k: first.K + (changed == "keyRoot" ? 0x1000UL : 0),
            ui: first.Ui + (changed == "uiRoot" ? 0x1000UL : 0),
            world: first.World + (changed == "worldRoot" ? 0x1000UL : 0));
        Assert.Equal(second.World, second.Run().World);
        var pass = 0;
        byte[] Read(ulong a, int n)
        {
            if (a == Fixture.B + 0x2F56EF8) pass++;
            return (pass == 1 ? first : second).Read(a, n);
        }
        var error = Assert.Throws<InvalidDataException>(() => Warcraft300WorldLocator.Read(Read, Fixture.B, default));
        Assert.Contains("changed between reads", error.Message);
    }

    [Theory]
    [InlineData("uiType")] [InlineData("worldType")] [InlineData("parent")]
    [InlineData("globalA")] [InlineData("globalB")] [InlineData("bothGlobals")]
    public void WrongTypeParentOrIndependentGlobalsRejected(string kind)
    {
        var f = new Fixture();
        switch (kind)
        {
            case "uiType": f.Q(f.Ui, Fixture.B + 0x275ED10); break;
            case "worldType": f.Q(f.World, Fixture.B + 0x2764A28); break;
            case "parent": f.Q(f.World + 0x40, f.Ui + 8); break;
            case "globalA": f.Q(Fixture.B + 0x2F5EF00, f.Ui + 8); break;
            case "globalB": f.Q(Fixture.B + 0x2F85360, f.Ui + 8); break;
            case "bothGlobals":
                f.Q(Fixture.B + 0x2F5EF00, f.Ui + 8); f.Q(Fixture.B + 0x2F85360, f.Ui + 8); break;
        }
        Assert.Throws<InvalidDataException>(() => f.Run());
    }

    [Theory]
    [InlineData(0UL)] [InlineData(0xFFFFUL)] [InlineData(0x800000000000UL)] [InlineData(ulong.MaxValue)]
    public void InvalidKeyRootRejectedBeforeDereference(ulong k)
    {
        var f = new Fixture(); f.Q(Fixture.B + 0x2F56EF8, k);
        Assert.Throws<InvalidDataException>(() => f.Run());
        Assert.Single(f.Reads);
    }

    [Theory]
    [InlineData(0UL)] [InlineData(0xFFFFUL)] [InlineData(0x800000000000UL)] [InlineData(ulong.MaxValue)]
    [InlineData(0x7FFFFFFFFFFCUL)]
    public void InvalidModuleOrCrossBoundaryReadRejectedWithoutMemoryAccess(ulong b)
    {
        var f = new Fixture();
        Assert.Throws<InvalidDataException>(() => Warcraft300WorldLocator.Read(f.Read, b, default));
        Assert.Empty(f.Reads);
    }

    [Theory]
    [InlineData(0UL)] [InlineData(0xFFFFUL)] [InlineData(0x800000000000UL)]
    [InlineData(0x7FFFFFFFFFFCUL)]
    public void DecodedInvalidAndZeroMenuPointersAreUnavailableNotForcedRoots(ulong pointer)
    {
        var invalidUi = new Fixture(ui: pointer);
        Assert.Throws<InvalidDataException>(() => invalidUi.Run());
        Assert.DoesNotContain(invalidUi.Reads, r => r.Address == pointer);
        var invalidWorld = new Fixture(world: pointer);
        Assert.Throws<InvalidDataException>(() => invalidWorld.Run());
        Assert.DoesNotContain(invalidWorld.Reads, r => r.Address == pointer);
    }

    [Fact]
    public void KeyFieldReadMayNotCrossUserAddressCeiling()
    {
        var f = new Fixture(); f.Q(Fixture.B + 0x2F56EF8, 0x7FFFFFFFFFFCUL - 0x1B4);
        Assert.Throws<InvalidDataException>(() => f.Run());
        Assert.Single(f.Reads);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void ShortReadAtEverySiteFailsClosed(int field)
    {
        var f = new Fixture(); var target = f.Pass[field].Address;
        byte[] Read(ulong a, int n) => a == target ? new byte[n - 1] : f.Read(a, n);
        Assert.Throws<InvalidDataException>(() => Warcraft300WorldLocator.Read(Read, Fixture.B, default));
    }

    [Fact]
    public void OversizedAndNullReadResultsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Warcraft300WorldLocator.Read((_, n) => new byte[n + 1], Fixture.B, default));
        Assert.Throws<InvalidDataException>(() => Warcraft300WorldLocator.Read((_, _) => null!, Fixture.B, default));
    }

    [Fact]
    public void NoAccessIsUnavailableAndNeverTriggersDiscoveryFallback()
    {
        var calls = 0; var error = new Win32Exception(5);
        var rejected = Assert.Throws<InvalidDataException>(() => Warcraft300WorldLocator.Read((_, _) =>
        { calls++; throw error; }, Fixture.B, default));
        Assert.Same(error, rejected.InnerException); Assert.Equal(1, calls);
    }

    [Fact]
    public void CancelledBeforeReadMakesNoMemoryAccess()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel(); var f = new Fixture();
        Assert.Throws<OperationCanceledException>(() => Warcraft300WorldLocator.Read(f.Read, Fixture.B, cts.Token));
        Assert.Empty(f.Reads);
    }

    [Theory] [InlineData(1)] [InlineData(12)] [InlineData(24)]
    public void CancellationDuringEitherPassDoesNotReturnAContext(int cancelAt)
    {
        using var cts = new CancellationTokenSource(); var f = new Fixture();
        byte[] Read(ulong a, int n)
        {
            var bytes = f.Read(a, n); if (f.Reads.Count == cancelAt) cts.Cancel(); return bytes;
        }
        Assert.Throws<OperationCanceledException>(() => Warcraft300WorldLocator.Read(Read, Fixture.B, cts.Token));
        Assert.Equal(cancelAt, f.Reads.Count);
    }
}
