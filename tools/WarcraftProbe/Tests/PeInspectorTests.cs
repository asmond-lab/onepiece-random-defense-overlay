using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace WarcraftProbe.Tests;

public sealed class PeInspectorTests
{
    [Fact]
    public void ValidPe_ReportsExactHeaderValuesAndByteHashes()
    {
        byte[] b = Fixture();
        var info = PeInspector.Analyze(b, @"C:\private\build\Warcraft III.exe");
        Assert.Equal("Warcraft III.exe", info.FileName);
        Assert.Equal((long)b.Length, info.FileSize);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant(), info.Sha256);
        Assert.Equal((ushort)0x8664, info.Machine);
        Assert.Equal(Base, info.PreferredBase);
        Assert.Equal(0x5000u, info.ImageSize);
        Assert.Equal(0x400u, info.HeaderSize);
        Assert.Equal(0x1000u, info.EntryPointRva);
        Assert.Equal(0xf1234567u, info.TimeDateStamp);
        Assert.Equal(3, info.Sections.Length);
        foreach (var section in info.Sections)
            Assert.Equal(Convert.ToHexString(SHA256.HashData(b.AsSpan((int)section.RawOffset, (int)section.RawSize))).ToLowerInvariant(), section.Sha256);
    }

    [Fact]
    public void NoResources_ReturnsNullVersionDespiteVersionLookingStrings()
    {
        byte[] b = Fixture(); PutAscii(b, At(0x2100), "Warcraft 3.0.9.12345 VS_VERSION_INFO");
        Assert.Null(PeInspector.Analyze(b, "war3.exe").FileVersion);
    }

    [Fact]
    public void ProperRtVersion_ReadsOnlyFixedFileVersion()
    {
        byte[] b = Fixture(); Version(b);
        Assert.Equal("1.36.2.21000", PeInspector.Analyze(b, "war3.exe").FileVersion);
    }

    [Fact]
    public void VersionLookingPayloadUnderAnotherResourceType_IsNotVersion()
    {
        byte[] b = Fixture(); Version(b); W32(b, At(0x3010), 10);
        Assert.Null(PeInspector.Analyze(b, "war3.exe").FileVersion);
    }

    [Fact]
    public void ConflictingVersionLanguages_ReturnUnknown()
    {
        byte[] b = Fixture(); Version(b);
        W16(b, At(0x304e), 2);
        W32(b, At(0x3058), 1041); W32(b, At(0x305c), 0x70);
        W32(b, At(0x3070), 0x3100); W32(b, At(0x3074), 92);
        b.AsSpan(At(0x3080), 92).CopyTo(b.AsSpan(At(0x3100), 92));
        W32(b, At(0x3100) + 48, 2u << 16);
        Assert.Null(PeInspector.Analyze(b, "war3.exe").FileVersion);
    }

    [Theory]
    [InlineData(0)] [InlineData(63)] [InlineData(0x90)] [InlineData(0x180)] [InlineData(0x27ff)]
    public void TruncatedImage_IsRejected(int length)
        => Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(Fixture()[..length], "war3.exe"));

    [Theory]
    [InlineData(0x3c, 0xfffffff0u)]
    [InlineData(Optional + 56, 0xfffff000u)]
    [InlineData(SectionTable + 20, 0xfffffe00u)]
    [InlineData(SectionTable + 16, 0xfffffe00u)]
    [InlineData(Optional + 128, 0xfffffff0u)]
    public void OverflowingHeaderRanges_AreRejected(int at, uint value)
    {
        byte[] b = Fixture(); W32(b, at, value);
        Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(b, "war3.exe"));
    }

    [Fact]
    public void PreferredVaOverflow_IsRejected()
    {
        byte[] b = Fixture(); W64(b, Optional + 24, ulong.MaxValue - 0x100);
        Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(b, "war3.exe"));
    }

    [Theory]
    [InlineData(SectionTable + 40 + 20, 0x400u)]
    [InlineData(SectionTable + 40 + 12, 0x1000u)]
    public void OverlappingSections_AreRejected(int at, uint value)
    {
        byte[] b = Fixture(); W32(b, at, value);
        Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(b, "war3.exe"));
    }

    [Fact]
    public void ImageLargerThan256MiB_IsRejectedBeforeParsing()
        => Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(new byte[256 * 1024 * 1024 + 1], "war3.exe"));

    [Theory]
    [InlineData(0)] [InlineData(97)]
    public void InvalidSectionCount_IsRejected(int count)
    {
        byte[] b = Fixture(); W16(b, 0x86, (ushort)count);
        Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(b, "war3.exe"));
    }

    [Fact]
    public void Pe32OrNonAmd64_IsRejected()
    {
        byte[] b = Fixture(); W16(b, Optional, 0x10b);
        Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(b, "war3.exe"));
        b = Fixture(); W16(b, 0x84, 0xaa64);
        Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(b, "war3.exe"));
    }

    [Theory]
    [InlineData(0x3014, 0x80000200u)]
    [InlineData(0x3034, 0x80000000u)]
    [InlineData(0x3060, 0x31f0u)]
    [InlineData(0x3064, 0xfffffff0u)]
    public void EscapingOrCyclicResourcePaths_AreRejected(uint rva, uint value)
    {
        byte[] b = Fixture(); Version(b); W32(b, At(rva), value);
        Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(b, "war3.exe"));
    }

    [Fact]
    public void MalformedVersionBlock_IsRejected()
    {
        byte[] b = Fixture(); Version(b); W16(b, At(0x3080), 16);
        Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(b, "war3.exe"));
    }

    [Theory]
    [InlineData("")] [InlineData("..")] [InlineData("bad\0name.exe")]
    public void InvalidFileName_IsRejected(string fileName)
        => Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(Fixture(), fileName));

    [Fact]
    public void NativeStrings_RecordAllMatchesAndNeverClaimFunctionRvas()
    {
        byte[] b = Fixture(); PutAscii(b, At(0x2100), "GetWidgetLife"); PutAscii(b, At(0x2140), "GetWidgetLife");
        var info = PeInspector.Analyze(b, "war3.exe");
        var a = Find(info, "native-name-string", "GetWidgetLife");
        Assert.Equal(new uint[] { 0x2100, 0x2140 }, a.Rvas); Assert.Equal("ambiguous", a.Status);
        Assert.DoesNotContain(info.Anchors, a => a.Kind.Contains("function", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(info.Anchors.SelectMany(a => a.Rvas), r => r is >= 0x1000 and < 0x1200);
    }

    [Fact]
    public void NativeNames_RequireExactTerminatedReadonlyDataStrings()
    {
        byte[] b = Fixture();
        PutAscii(b, At(0x2100), "XGetWidgetLife"); PutAscii(b, At(0x2140), "GetWidgetLifeSuffix");
        PutAscii(b, At(0x1000), "GetWidgetLife"); PutAscii(b, At(0x4000), "GetWidgetLife");
        Encoding.ASCII.GetBytes("GetWidgetLife").CopyTo(b, At(0x3ff3));
        Assert.Empty(Find(PeInspector.Analyze(b, "war3.exe"), "native-name-string", "GetWidgetLife").Rvas);
    }

    [Fact]
    public void MoreThan256NativeMatches_IsExplicitBoundsFailure()
    {
        byte[] b = Fixture();
        for (int i = 0; i < 257; i++) PutAscii(b, At(0x2000) + i * 16, "GetWidgetLife");
        var ex = Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(b, "war3.exe"));
        Assert.Contains("256-match", ex.Message);
    }

    [Fact]
    public void Rtti_ValidatesDescriptorLocatorHierarchyAndThreeCodePointers()
    {
        byte[] b = Fixture(); Rtti(b);
        var info = PeInspector.Analyze(b, "war3.exe");
        Assert.Equal(new uint[] { 0x4000 }, Find(info, "msvc-type-descriptor", "CUnit").Rvas);
        Assert.Equal(new uint[] { 0x2200 }, Find(info, "msvc-complete-object-locator", "CUnit").Rvas);
        Assert.Equal(new uint[] { 0x2308 }, Find(info, "msvc-vtable", "CUnit").Rvas);
        Assert.Equal("resolved", Find(info, "msvc-vtable", "CUnit").Status);
    }

    [Fact]
    public void Rtti_MultipleTablesAndDescriptorsAreNotFirstMatchWins()
    {
        byte[] b = Fixture(); Rtti(b);
        b.AsSpan(At(0x4000), 32).CopyTo(b.AsSpan(At(0x4080), 32));
        b.AsSpan(At(0x2200), 24).CopyTo(b.AsSpan(At(0x2380), 24));
        W32(b, At(0x2380) + 20, 0x2380);
        W64(b, At(0x2400), Base + 0x2380);
        for (int i = 0; i < 3; i++) W64(b, At(0x2408) + i * 8, Base + 0x1000u + (uint)i);
        var info = PeInspector.Analyze(b, "war3.exe");
        Assert.Equal(new uint[] { 0x4000, 0x4080 }, Find(info, "msvc-type-descriptor", "CUnit").Rvas);
        Assert.Equal("ambiguous", Find(info, "msvc-type-descriptor", "CUnit").Status);
        Assert.Equal(new uint[] { 0x2308, 0x2408 }, Find(info, "msvc-vtable", "CUnit").Rvas);
        Assert.Contains("ambiguous", Find(info, "msvc-vtable", "CUnit").Status);
    }

    [Fact]
    public void Rtti_FewerThanThreeCodePointersStaysUnresolvedAndPreservesCandidate()
    {
        byte[] b = Fixture(); Rtti(b); W64(b, At(0x2318), Base + 0x2100);
        var info = PeInspector.Analyze(b, "war3.exe");
        Assert.Empty(Find(info, "msvc-vtable", "CUnit").Rvas);
        Assert.Equal("unresolved", Find(info, "msvc-vtable", "CUnit").Status);
        Assert.Equal(new uint[] { 0x2308 }, Find(info, "msvc-vtable-candidate", "CUnit").Rvas);
    }

    [Fact]
    public void Rtti_OneVerifiedAndOneUnresolvedTableIsAmbiguous()
    {
        byte[] b = Fixture(); Rtti(b); W64(b, At(0x2400), Base + 0x2200);
        var info = PeInspector.Analyze(b, "war3.exe");
        Assert.Single(Find(info, "msvc-vtable", "CUnit").Rvas);
        Assert.Equal("ambiguous-candidates", Find(info, "msvc-vtable", "CUnit").Status);
        Assert.Equal(2, Find(info, "msvc-vtable-candidate", "CUnit").Rvas.Length);
    }

    [Theory]
    [InlineData(0x2200, 0u)] [InlineData(0x2214, 0x2204u)]
    [InlineData(0x2210, 0xfffffff0u)] [InlineData(0x2248, 257u)]
    [InlineData(0x224c, 0xfffffff0u)] [InlineData(0x2260, 0xfffffff0u)]
    public void Rtti_InvalidLocatorOrHierarchyDoesNotResolve(uint rva, uint value)
    {
        byte[] b = Fixture(); Rtti(b); W32(b, At(rva), value);
        Assert.Empty(Find(PeInspector.Analyze(b, "war3.exe"), "msvc-vtable", "CUnit").Rvas);
    }

    [Fact]
    public void Rtti_PreferredBaseNormalizationDoesNotUseRuntimeAslrAddresses()
    {
        byte[] a = Fixture(); Rtti(a);
        byte[] b = Fixture(); Rtti(b);
        ulong newBase = 0x7ff600000000;
        W64(b, Optional + 24, newBase);
        W64(b, At(0x4000), newBase + 0x2080); W64(b, At(0x2300), newBase + 0x2200);
        for (int i = 0; i < 3; i++) W64(b, At(0x2308) + i * 8, newBase + 0x1000u + (uint)i);
        Assert.Equal(Find(PeInspector.Analyze(a, "a.exe"), "msvc-vtable", "CUnit").Rvas,
            Find(PeInspector.Analyze(b, "b.exe"), "msvc-vtable", "CUnit").Rvas);
        W64(b, At(0x2308), Base + 0x1000);
        Assert.Empty(Find(PeInspector.Analyze(b, "b.exe"), "msvc-vtable", "CUnit").Rvas);
    }

    [Fact]
    public void MoreThan256VtableCandidates_IsExplicitBoundsFailure()
    {
        byte[] b = Fixture(); Rtti(b);
        for (int i = 0; i < 257; i++) W64(b, At(0x2800) + i * 8, Base + 0x2200);
        Assert.Contains("256-match", Assert.Throws<BadImageFormatException>(() => PeInspector.Analyze(b, "war3.exe")).Message);
    }

    [Fact]
    public void Analyze_DoesNotModifyInputAndHashesTrailingBytes()
    {
        byte[] b = Fixture(), copy = (byte[])b.Clone();
        var first = PeInspector.Analyze(b, "war3.exe"); Assert.Equal(copy, b);
        byte[] appended = new byte[b.Length + 1]; b.CopyTo(appended, 0); appended[^1] = 42;
        Assert.NotEqual(first.Sha256, PeInspector.Analyze(appended, "war3.exe").Sha256);
    }

    private const ulong Base = 0x140000000;
    private const int Optional = 0x98, SectionTable = 0x188;
    private static AnchorInfo Find(ImageInfo info, string kind, string name)
        => Assert.Single(info.Anchors, a => a.Kind == kind && a.Name == name);
    private static int At(uint rva) => rva < 0x2000 ? (int)rva - 0x1000 + 0x400 :
        rva < 0x4000 ? (int)rva - 0x2000 + 0x600 : (int)rva - 0x4000 + 0x2600;
    private static void W16(byte[] b, int p, ushort x) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p, 2), x);
    private static void W32(byte[] b, int p, uint x) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p, 4), x);
    private static void W64(byte[] b, int p, ulong x) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(p, 8), x);
    private static void PutAscii(byte[] b, int p, string text) => Encoding.ASCII.GetBytes(text + "\0").CopyTo(b, p);
    private static byte[] Fixture()
    {
        byte[] b = new byte[0x2800];
        W16(b, 0, 0x5a4d); W32(b, 0x3c, 0x80); W32(b, 0x80, 0x4550);
        W16(b, 0x84, 0x8664); W16(b, 0x86, 3); W32(b, 0x88, 0xf1234567);
        W16(b, 0x94, 240); W16(b, 0x96, 0x22);
        W16(b, Optional, 0x20b); W32(b, Optional + 16, 0x1000); W32(b, Optional + 20, 0x1000);
        W64(b, Optional + 24, Base); W32(b, Optional + 32, 0x1000); W32(b, Optional + 36, 0x200);
        W32(b, Optional + 56, 0x5000); W32(b, Optional + 60, 0x400); W16(b, Optional + 68, 3);
        W64(b, Optional + 72, 0x100000); W64(b, Optional + 80, 0x1000);
        W64(b, Optional + 88, 0x100000); W64(b, Optional + 96, 0x1000); W32(b, Optional + 108, 16);
        Section(b, 0, ".text", 0x1000, 0x200, 0x400, 0x60000020);
        Section(b, 1, ".rdata", 0x2000, 0x2000, 0x600, 0x40000040);
        Section(b, 2, ".data", 0x4000, 0x200, 0x2600, 0xc0000040);
        b.AsSpan(0x400, 0x200).Fill(0xc3);
        return b;
    }
    private static void Section(byte[] b, int i, string name, uint rva, uint size, uint raw, uint flags)
    {
        int p = SectionTable + i * 40; PutAscii(b, p, name);
        W32(b, p + 8, size); W32(b, p + 12, rva); W32(b, p + 16, size); W32(b, p + 20, raw); W32(b, p + 36, flags);
    }
    private static void Rtti(byte[] b)
    {
        W64(b, At(0x4000), Base + 0x2080); PutAscii(b, At(0x4010), ".?AVCUnit@@");
        W32(b, At(0x2200), 1); W32(b, At(0x220c), 0x4000);
        W32(b, At(0x2210), 0x2240); W32(b, At(0x2214), 0x2200);
        W32(b, At(0x2248), 1); W32(b, At(0x224c), 0x2260);
        W32(b, At(0x2260), 0x2280); W32(b, At(0x2280), 0x4000); W32(b, At(0x2298), 0x2240);
        W64(b, At(0x2300), Base + 0x2200);
        for (int i = 0; i < 3; i++) W64(b, At(0x2308) + i * 8, Base + 0x1000u + (uint)i);
    }
    private static void Version(byte[] b)
    {
        W32(b, Optional + 128, 0x3000); W32(b, Optional + 132, 0x200);
        W16(b, At(0x300e), 1); W32(b, At(0x3010), 16); W32(b, At(0x3014), 0x80000020);
        W16(b, At(0x302e), 1); W32(b, At(0x3030), 1); W32(b, At(0x3034), 0x80000040);
        W16(b, At(0x304e), 1); W32(b, At(0x3050), 1033); W32(b, At(0x3054), 0x60);
        W32(b, At(0x3060), 0x3080); W32(b, At(0x3064), 92);
        int p = At(0x3080); W16(b, p, 92); W16(b, p + 2, 52);
        Encoding.Unicode.GetBytes("VS_VERSION_INFO\0").CopyTo(b, p + 6);
        W32(b, p + 40, 0xfeef04bd); W32(b, p + 44, 0x10000);
        W32(b, p + 48, (1u << 16) | 36); W32(b, p + 52, (2u << 16) | 21000);
    }
}
