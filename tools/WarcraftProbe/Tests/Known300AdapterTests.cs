using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using Xunit;

namespace WarcraftProbe.Tests;

public sealed class Known300AdapterTests
{
    internal static ImageInfo Image() => new("copy.exe",1,Known300Adapter.Hash,Known300Adapter.Version,0x8664,0x140000000,0x4000000,0x400,0,0,Array.Empty<SectionInfo>(),Array.Empty<AnchorInfo>());
    private static readonly DateTimeOffset Epoch=DateTimeOffset.Parse("2020-01-01T00:00:00Z");
    private static StructureInfo Capture(Func<ulong,int,byte[]> read, CancellationToken ct=default, Func<TimeSpan>? elapsed=null, Action? pin=null) =>
        Known300Adapter.Capture(Image(),Fixture.B,123,Epoch,read,()=>{},pin??(()=>{}),ct,elapsed);
    private static void Empty(StructureInfo s)
    {
        Assert.Equal("Unknown",s.Status); Assert.Empty(s.Rawcodes); Assert.Null(s.CurrentViewSlot); Assert.Null(s.ObservedObjectCount);
        Assert.Null(s.CurrentViewObjectCount); Assert.Null(s.OtherViewObjectCount); Assert.Null(s.WorldToken); Assert.Null(s.UiToken); Assert.Null(s.VmToken); Assert.NotNull(s.Reason);
    }
    [Fact] public void FullTypedAllocatedFixtureCapturesRawCountsWithoutGameplayClaims()
    {
        var f=new Fixture(); f.D(Fixture.Unit+0x178,0x30303068); var result=Capture(f.Read);
        Assert.Equal("Observed",result.Status); Assert.Equal(1,result.ObservedObjectCount); Assert.Equal(1,result.CurrentViewObjectCount);
        Assert.Equal(new RawcodeCount("h000",1),Assert.Single(result.Rawcodes)); Assert.Equal(64,result.WorldToken!.Length);
        Assert.False(result.LocalIdentityVerified); Assert.False(result.AliveVerified); Assert.False(result.CompleteGameplayInventoryVerified); Assert.False(result.CraftabilityVerified); Assert.False(result.GameplayReady);
    }
    [Theory] [InlineData("hash")] [InlineData("version")] [InlineData("machine")]
    public void UnknownBuildHasZeroStructureReadsOrPinCallbacks(string field)
    {
        var i=Image(); i=field=="hash"?i with{Sha256=new string('A',64)}:field=="version"?i with{FileVersion="3.0.0.24269"}:i with{Machine=0x14c};
        Empty(Known300Adapter.Capture(i,Fixture.B,123,Epoch,(_,_)=>throw new Exception("read"),()=>throw new Exception("budget"),()=>throw new Exception("pin")));
    }
    [Theory] [InlineData("rawcode")] [InlineData("owner")] [InlineData("vector")] [InlineData("vm")]
    [InlineData("world")] [InlineData("serial")] [InlineData("slot")]
    public void ChangedContextAlwaysClearsAllCountsAndTokens(string field)
    {
        var f=new Fixture(); var hits=0;
        var target=field switch { "rawcode"=>Fixture.Unit+0x178,"owner"=>Fixture.Unit+0x1C0,"vector"=>Fixture.Array,"vm"=>Fixture.Game+0x25D0,"world"=>Fixture.B+0x2F56EF8,"serial"=>Fixture.Unit+0x1C,_=>Fixture.Game+0x262C};
        byte[] Read(ulong a,int n) { var b=f.Read(a,n); if(a==target && ++hits==2)b[0]^=8;return b; }
        Empty(Capture(Read)); Assert.Equal(2,hits);
    }
    [Theory] [InlineData(0)] [InlineData(32769)]
    public void CountOutsideOneTo32768RejectedBeforeVectorRead(int count)
    {
        var f=new Fixture();f.D(Fixture.Frame+0xC08,(uint)count);int vectors=0;
        Empty(Capture((a,n)=>{if(a==Fixture.Array)vectors++;return f.Read(a,n);}));Assert.Equal(0,vectors);
    }
    [Theory] [InlineData("allocated")] [InlineData("serial")] [InlineData("type")] [InlineData("backref")]
    [InlineData("state30")] [InlineData("state83")] [InlineData("owner")] [InlineData("vmtype")] [InlineData("dualglobals")]
    public void InvalidAllocationOrContextHasNoExportedValues(string field)
    {
        var f=new Fixture();
        switch(field)
        {
            case "allocated":f.D(0x270000000,0);break;
            case "serial":f.D(0x280000024,6);break;
            case "type":f.D(0x280000018,0);break;
            case "backref":f.Q(0x280000090,Fixture.Unit+8);break;
            case "state30":f.Q(0x280000030,1);break;
            case "state83":f.Put(0x280000083,new byte[]{1});break;
            case "owner":f.D(Fixture.Unit+0x1C0,256);break;
            case "vmtype":f.Q(0x2A0000000,0);break;
            case "dualglobals":f.Q(Fixture.B+0x2F85360,Fixture.Ui+8);break;
        }
        Empty(Capture(f.Read));
    }
    [Fact] public void DuplicateEntriesRejectedEvenIfEveryPointerHasValidAllocation()
    {
        var f=new Fixture();f.D(Fixture.Frame+0xC08,2);f.Q(Fixture.Array+8,Fixture.Unit);Empty(Capture(f.Read));
    }
    [Fact] public void ThreeSecondEqualityIncludesWorldLocatorAndFinalPin()
    {
        var f=new Fixture();var t=TimeSpan.Zero;int reads=0;
        Empty(Capture((a,n)=>{reads++;t=TimeSpan.FromSeconds(3);return f.Read(a,n);},elapsed:()=>t));Assert.Equal(1,reads);
        t=TimeSpan.Zero;int pins=0;
        Empty(Capture(f.Read,elapsed:()=>t,pin:()=>{if(++pins==2)t=TimeSpan.FromSeconds(3);}));Assert.Equal(2,pins);
    }
    [Fact] public void PartialCancelledOrMissingReadNeverPublishes()
    {
        Empty(Capture((_,n)=>new byte[n-1])); Empty(Capture((_,_)=>throw new WindowsCollector.UnreadableException()));
        using var ct=new CancellationTokenSource();ct.Cancel();int reads=0;Empty(Capture((_,n)=>{reads++;return new byte[n];},ct.Token));Assert.Equal(0,reads);
    }
    [Fact] public void ReplayReadsConsumeSameNativeByteBudget()
    {
        var f=new Fixture();var budget=new WindowsCollector.Budget(()=>TimeSpan.Zero);long actual=0;
        byte[] Read(ulong a,int n)=>WindowsCollector.ReadExact(a,n,x=>new(x,(ulong)n,0x1000,4,0x20000),(x,s)=>{actual+=s;return f.Read(x,s);},budget);
        var r=Capture(Read);Assert.Equal("Observed",r.Status);Assert.Equal(actual,budget.Bytes);Assert.True(actual>164);
        budget.Charge((int)(WindowsCollector.MaximumReadBytes-budget.Bytes));Empty(Capture(Read));Assert.Equal(WindowsCollector.MaximumReadBytes,budget.Bytes);
    }
    [Fact] public void TokensUseProcessEpochAndAddressesAndRawcodesAreLowByteFirst()
    {
        var token=Known300Adapter.ContextToken(123,Epoch,0x200000000);
        Assert.NotEqual(token,Known300Adapter.ContextToken(124,Epoch,0x200000000));Assert.NotEqual(token,Known300Adapter.ContextToken(123,Epoch.AddTicks(1),0x200000000));
        Assert.NotEqual(token,Known300Adapter.ContextToken(123,Epoch,0x200000008));Assert.Equal("h000",Known300Adapter.RawcodeLabel(0x30303068));Assert.Equal("0x00000001",Known300Adapter.RawcodeLabel(1));
    }
    private sealed class Fixture
    {
        public const ulong B=0x140000000, Game=0x200000000, Player=0x210000000, Frame=0x220000000, Array=0x230000000, Unit=0x240000000, Ui=0x250000000;
        private readonly Dictionary<ulong, byte> bytes = new();
        public void Put(ulong a, byte[] data) { for(var i=0;i<data.Length;i++) bytes[a+(ulong)i]=data[i]; }
        public void Q(ulong a, ulong v) => Put(a,BitConverter.GetBytes(v));
        public void D(ulong a, uint v) => Put(a,BitConverter.GetBytes(v));
        public byte[] Read(ulong a,int n) => Enumerable.Range(0,n).Select(i=>bytes.TryGetValue(a+(ulong)i,out var value) ? value : throw new InvalidDataException("Missing fixture byte")).ToArray();
        public Fixture()
        {
            var rotated=unchecked(((Game-0x2D2C27903E7F5D3DUL)^0x3A11C7B7EF67132BUL)-0x5BE06F37FC9B5B29UL);
            Q(B+0x2E9AD00,(rotated>>29)|(rotated<<35));
            Q(Game,B+0x26C8C70); D(Game+0x2698,28); Put(Game+0x262C,BitConverter.GetBytes((ushort)6));
            Q(Game+0x26A0+6*8,Player); Q(Player,B+0x26C87D8);
            Q(B+0x2F5EF00,Ui); Q(B+0x2F85360,Ui); Q(Ui,B+0x275ED08); Q(Frame+0x40,Ui);
            Q(Frame,B+0x2764A20); D(Frame+0xC08,1); Q(Frame+0xC10,Array); Q(Array,Unit);
            Q(Unit,B+0x2792E78); D(Unit+0x1C0,6); D(Unit+0x178,0x68303031);
            Q(B+0x2F807F0,0x260000000); Q(0x260000018,0x270000000); D(0x260000030,1);
            D(Unit+0x18,0); D(Unit+0x1C,5); D(0x270000000,0xFFFFFFFE); Q(0x270000008,0x280000000);
            D(0x280000018,0x2B61676C); D(0x280000024,5); Q(0x280000090,Unit);
            Q(0x280000030,0); Put(0x280000083,new byte[]{0});
            Q(Game+0x25D0,0x2A0000000); Q(Game+0x25E0,0x2B0000000);
            Q(0x2A0000000,B+0x27ECBC0); Q(0x2B0000000,B+0x27ECC40);
            const ulong keyA=0xFEDCBA9876543210, keyB=0x8000000000000001, k=0x290000000;
            const byte byteA=0xD3,byteB=0xA7;
            Q(B+0x2F56EF8,k); Q(k+0x1B4,keyA); Q(k+0x19A,keyB);
            Put(B+0x2E8DC69,new[]{byteA}); Put(B+0x2E8DE3E,new[]{byteB});
            unchecked
            {
                var a=BitOperations.RotateRight(Ui,23)^0x5A64008D1F97DB7AUL^byteA;
                a-=0xFA3CC4C012B9EF8EUL; a-=0xF4339B63841E7E5EUL;
                Q(B+0x2FDD260,(BitOperations.RotateRight(a,12)+0x52E7B4144FB4B0E9UL)^keyA);
                var w=(Frame^keyB^byteB)-0xBBAD6A13B280A99CUL; w^=0xC93E2E7D3C27237DUL;
                Q(Ui+0x6A8,BitOperations.RotateRight(BitOperations.RotateRight(w,13)-0x9BE33DE0422B8111UL,24));
            }
        }
        public Warcraft300Diagnostic.Inventory Run() => Warcraft300Diagnostic.ReadInventory(Read,B,Frame,default);
    }
}

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

public sealed class Warcraft300HandleValidatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimaryAndSecondaryHaveExactUnsignedAbi(bool alternate)
    {
        var memory = new Memory(alternate);
        var stamp = memory.Validate();
        Assert.Equal(Memory.Module, stamp.ModuleBase);
        Assert.Equal(Memory.Unit, stamp.Unit);
        Assert.Equal(Memory.Module + 0x2792E78, stamp.Vtable);
        Assert.Equal(Memory.Module + 0x2F807F0, stamp.RegistryGlobal);
        Assert.Equal(Memory.Registry, stamp.Registry);
        Assert.Equal(alternate ? 0x80000003U : 3U, stamp.RawHandle);
        Assert.Equal(0xF1234567U, stamp.Serial);
        Assert.Equal(stamp.Serial, stamp.RecordSerial);
        Assert.Equal(alternate, stamp.Alternate);
        Assert.Equal(3U, stamp.Index);
        Assert.Equal(Memory.Table, stamp.Table);
        Assert.Equal(4U, stamp.TableLimit);
        Assert.Equal(Memory.Table + 48, stamp.Slot);
        Assert.Equal(0xFFFFFFFEU, stamp.Marker);
        Assert.Equal(Memory.Record, stamp.Record);
        Assert.Equal(0x2B61676CU, stamp.TypeId);
        Assert.Equal(Memory.Unit, stamp.BackReference);
        Assert.Equal(0UL, stamp.State30);
        Assert.Equal((byte)0xA4, stamp.State83);
        Assert.Equal(stamp, memory.Validate());
        Assert.Contains((Memory.Unit + 0x18, 4), memory.Calls);
        Assert.Contains((Memory.Unit + 0x1C, 4), memory.Calls);
        Assert.Contains((Memory.Record + 0x18, 4), memory.Calls);
        Assert.Contains((Memory.Record + 0x24, 4), memory.Calls);
        Assert.All(memory.Calls, call => Assert.Contains(call.Length, new[] { 1, 4, 8 }));
        Assert.DoesNotContain(memory.Calls, c => c.Address == Memory.Registry + (alternate ? 0x18UL : 0x50UL));
        Assert.Equal(13, memory.Calls.Count / 2);
    }

    [Theory]
    [InlineData("serial")]
    [InlineData("marker")]
    [InlineData("type")]
    [InlineData("backref")]
    [InlineData("state30")]
    [InlineData("state83")]
    [InlineData("vtable")]
    public void RejectsMismatches(string field)
    {
        var memory = new Memory();
        switch (field)
        {
            case "serial": memory.Put32(Memory.Record + 0x24, 7); break;
            case "marker": memory.Put32(Memory.Table + 48, uint.MaxValue); break;
            case "type": memory.Put32(Memory.Record + 0x18, 0x6C67612B); break;
            case "backref": memory.Put64(Memory.Record + 0x90, Memory.Unit + 8); break;
            case "state30": memory.Put64(Memory.Record + 0x30, 0x100000000); break;
            case "state83": memory.Put8(Memory.Record + 0x83, 0xA5); break;
            case "vtable": memory.Put64(Memory.Unit, Memory.Module + 0x2792E80); break;
        }
        Assert.Throws<InvalidDataException>(() => memory.Validate());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecycledSameAddressRequiresMatchingNewSerialAndChangesStamp(bool alternate)
    {
        var memory = new Memory(alternate);
        var before = memory.Validate();
        memory.Put32(Memory.Record + 0x24, 0xF1234568);
        Assert.Throws<InvalidDataException>(() => memory.Validate());
        memory.Put32(Memory.Unit + 0x1C, 0xF1234568);
        var after = memory.Validate();
        Assert.Equal(before.Unit, after.Unit);
        Assert.Equal(before.Record, after.Record);
        Assert.NotEqual(before, after);
    }

    [Theory]
    [InlineData("registry")]
    [InlineData("table")]
    [InlineData("record")]
    [InlineData("limit")]
    [InlineData("handle")]
    [InlineData("state83")]
    public void AcceptedChangesProduceDifferentComparisonStamps(string field)
    {
        var memory = new Memory();
        var before = memory.Validate();
        switch (field)
        {
            case "registry":
                memory.Put64(Memory.Module + 0x2F807F0, Memory.Registry + 0x1000);
                memory.Put64(Memory.Registry + 0x1018, Memory.Table);
                memory.Put32(Memory.Registry + 0x1030, 4);
                break;
            case "table":
                memory.Put64(Memory.Registry + 0x18, Memory.Table + 0x1000);
                memory.Put32(Memory.Table + 0x1030, 0xFFFFFFFE);
                memory.Put64(Memory.Table + 0x1038, Memory.Record);
                break;
            case "record":
                memory.Put64(Memory.Table + 56, Memory.Record + 0x1000);
                memory.SeedRecord(Memory.Record + 0x1000);
                break;
            case "limit": memory.Put32(Memory.Registry + 0x30, 5); break;
            case "handle":
                memory.Put32(Memory.Unit + 0x18, 2);
                memory.Put32(Memory.Table + 32, 0xFFFFFFFE);
                memory.Put64(Memory.Table + 40, Memory.Record);
                break;
            case "state83": memory.Put8(Memory.Record + 0x83, 0xA6); break;
        }
        Assert.NotEqual(before, memory.Validate());
        Assert.Equal((byte)0xA4, before.State83);
    }

    [Theory]
    [InlineData(0U, 0U, false)]
    [InlineData(4U, 4U, false)]
    [InlineData(4U, 5U, false)]
    [InlineData(16777217U, 3U, false)]
    [InlineData(4294967295U, 3U, false)]
    [InlineData(16777216U, 16777216U, false)]
    [InlineData(16777216U, 2147483647U, false)]
    [InlineData(1U, 0U, true)]
    [InlineData(16777216U, 16777215U, true)]
    public void SelectedTableLimitsAreUnsignedAndBounded(uint limit, uint index, bool accepted)
    {
        foreach (var alternate in new[] { false, true })
        {
            var memory = new Memory(alternate);
            memory.Put32(Memory.Unit + 0x18, index | (alternate ? 0x80000000U : 0));
            memory.Put32(Memory.Registry + (alternate ? 0x68UL : 0x30UL), limit);
            var slot = Memory.Table + 16UL * index;
            memory.Put32(slot, 0xFFFFFFFE);
            memory.Put64(slot + 8, Memory.Record);
            if (accepted) Assert.Equal(index, memory.Validate().Index);
            else
            {
                Assert.Throws<InvalidDataException>(() => memory.Validate());
                Assert.DoesNotContain(memory.Calls, c => c.Address == slot);
            }
        }
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(65535UL)]
    [InlineData(0x800000000000UL)]
    [InlineData(ulong.MaxValue)]
    public void ImplausibleRootsAndPointersAreRejectedBeforeDereference(ulong bad)
    {
        var memory = new Memory();
        Assert.Throws<InvalidDataException>(() => Warcraft300HandleValidator.Read(memory.Read, bad, Memory.Unit, default));
        Assert.Empty(memory.Calls);
        Assert.Throws<InvalidDataException>(() => Warcraft300HandleValidator.Read(memory.Read, Memory.Module, bad, default));
        Assert.Empty(memory.Calls);
        foreach (var pointerField in new[] { Memory.Module + 0x2F807F0, Memory.Registry + 0x18, Memory.Table + 56 })
        {
            memory = new Memory();
            memory.Put64(pointerField, bad);
            Assert.Throws<InvalidDataException>(() => memory.Validate());
            Assert.DoesNotContain(memory.Calls, c => c.Address == bad);
        }
    }

    [Theory]
    [InlineData("module")]
    [InlineData("unit")]
    [InlineData("registry")]
    [InlineData("table")]
    [InlineData("record")]
    public void CompleteSpansMustFitCanonicalUserAddressSpace(string field)
    {
        const ulong top = 0x7FFFFFFFFFFF;
        var memory = new Memory();
        var module = Memory.Module;
        var unit = Memory.Unit;
        switch (field)
        {
            case "module": module = top - 0x10; break;
            case "unit": unit = top - 0x10; break;
            case "registry": memory.Put64(Memory.Module + 0x2F807F0, top - 0x10); break;
            // Selected first slot would fit, but the declared full table span would not.
            case "table":
                memory.Put32(Memory.Unit + 0x18, 0);
                memory.Put64(Memory.Registry + 0x18, top - 31);
                break;
            case "record": memory.Put64(Memory.Table + 56, top - 0x90); break;
        }
        Assert.Throws<InvalidDataException>(() => Warcraft300HandleValidator.Read(memory.Read, module, unit, default));
        Assert.All(memory.Calls, c => Assert.True(c.Address + (ulong)c.Length - 1 <= top));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void EveryReadRejectsNullShortOrOversizedResponses(int change)
    {
        for (var failAt = 1; failAt <= 13; failAt++)
        {
            var memory = new Memory();
            var reads = 0;
            byte[] Read(ulong address, int length)
            {
                reads++;
                if (reads == failAt) return change == -1 ? null! : new byte[length + (change == 0 ? -1 : 1)];
                return memory.Read(address, length);
            }
            Assert.Throws<InvalidDataException>(() => Warcraft300HandleValidator.Read(Read, Memory.Module, Memory.Unit, default));
            Assert.Equal(failAt, reads);
        }
    }

    [Fact]
    public void CancellationBeforeReadDoesNotCallDelegate()
    {
        var memory = new Memory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Warcraft300HandleValidator.Read(memory.Read, Memory.Module, Memory.Unit, cancellation.Token));
        Assert.Empty(memory.Calls);
    }

    [Fact]
    public void CancellationDuringEveryReadIncludingLastPropagatesWithoutStamp()
    {
        for (var cancelAt = 1; cancelAt <= 13; cancelAt++)
        {
            var memory = new Memory();
            using var cancellation = new CancellationTokenSource();
            byte[] Read(ulong address, int length)
            {
                var bytes = memory.Read(address, length);
                if (memory.Calls.Count == cancelAt) cancellation.Cancel();
                return bytes;
            }
            var error = Assert.Throws<OperationCanceledException>(() => Warcraft300HandleValidator.Read(Read, Memory.Module, Memory.Unit, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(cancelAt, memory.Calls.Count);
        }
    }

    [Fact]
    public void ReaderFailuresPropagateAndNullDelegateIsRejected()
    {
        var error = new IOException("synthetic read failure");
        Assert.Same(error, Assert.Throws<IOException>(() => Warcraft300HandleValidator.Read((_, _) => throw error, Memory.Module, Memory.Unit, default)));
        Assert.Throws<ArgumentNullException>(() => Warcraft300HandleValidator.Read(null!, Memory.Module, Memory.Unit, default));
    }

    private sealed class Memory
    {
        internal const ulong Module = 0x140000000;
        internal const ulong Registry = 0x200000;
        internal const ulong Table = 0x300000;
        internal const ulong Record = 0x400000;
        internal const ulong Unit = 0x500000;
        private readonly Dictionary<ulong, byte> _bytes = new();
        internal List<(ulong Address, int Length)> Calls { get; } = new();

        internal Memory(bool alternate = false)
        {
            Put64(Module + 0x2F807F0, Registry);
            Put64(Unit, Module + 0x2792E78);
            Put32(Unit + 0x18, alternate ? 0x80000003U : 3U);
            Put32(Unit + 0x1C, 0xF1234567);
            Put64(Registry + (alternate ? 0x50UL : 0x18UL), Table);
            Put32(Registry + (alternate ? 0x68UL : 0x30UL), 4);
            Put32(Table + 48, 0xFFFFFFFE);
            Put64(Table + 56, Record);
            SeedRecord(Record);
        }

        internal void SeedRecord(ulong record)
        {
            Put32(record + 0x24, 0xF1234567);
            Put32(record + 0x18, 0x2B61676C);
            Put64(record + 0x90, Unit);
            Put64(record + 0x30, 0);
            Put8(record + 0x83, 0xA4);
        }

        internal Warcraft300HandleStamp Validate() => Warcraft300HandleValidator.Read(Read, Module, Unit, default);
        internal byte[] Read(ulong address, int length)
        {
            Calls.Add((address, length));
            // Missing bytes fail the fixture: any extra read, fallback, or wrong ABI is visible.
            return Enumerable.Range(0, length).Select(i => _bytes[checked(address + (ulong)i)]).ToArray();
        }
        internal void Put8(ulong address, byte value) => _bytes[address] = value;
        internal void Put32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        internal void Put64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        private void Put(ulong address, byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i++) _bytes[checked(address + (ulong)i)] = bytes[i];
        }
    }
}
