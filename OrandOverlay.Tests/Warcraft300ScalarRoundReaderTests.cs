using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;
using Reader = OrandOverlay.Warcraft300ScalarRoundReader;

namespace OrandOverlay.Tests;

public sealed class Warcraft300ScalarRoundReaderTests
{
    private sealed class Fixture
    {
        internal const ulong Module = 0x140000000, Instance = 0x200000000,
            Owner = 0x210000000, Table = 0x220000000, Pb = 0x230000000, Eb = 0x230000100,
            PbName = 0x240000000, EbName = 0x240000100;
        internal readonly DateTimeOffset Now = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
        internal readonly Dictionary<ulong, byte> Memory = new();
        internal Reader.ResolvedScalarNodes Nodes;
        internal int Calls, Bytes;
        internal string Context = "synthetic-world-generation-1";
        internal void Put(ulong address, byte[] data)
        { for (var i = 0; i < data.Length; i++) Memory[address + (ulong)i] = data[i]; }
        internal void Q(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        internal void D(ulong address, int value) => Put(address, BitConverter.GetBytes(value));
        internal byte[] Raw(ulong address, int length) => Enumerable.Range(0, length)
            .Select(i => Memory.GetValueOrDefault(address + (ulong)i)).ToArray();
        internal byte[] Read(ulong address, int length) { Calls++; Bytes += length; return Raw(address, length); }
        internal Fixture(int pb = 12, int eb = 13)
        {
            Q(Owner, Instance); Q(Owner + 8, Module + 0x13F3070); Q(Owner + 16, Table);
            Q(Table, Module + 0x2809B78); D(Table + 8, 24);
            Q(Table + 16, Eb + 24); Q(Table + 24, Pb);
            Q(Pb + 24, Table + 16); Q(Pb + 32, Eb); Q(Pb + 40, PbName);
            Q(Eb + 24, Pb + 24); Q(Eb + 32, Table + 17); Q(Eb + 40, EbName);
            D(Pb + 48, 4); D(Pb + 52, 4); D(Eb + 48, 4); D(Eb + 52, 4);
            Put(PbName, new byte[] { (byte)'p', (byte)'b', 0 });
            Put(EbName, new byte[] { (byte)'E', (byte)'b', 0 });
            D(Pb + 56, pb); D(Eb + 56, eb);
            Nodes = new(Module, Instance, Owner, Table, Raw(Owner, 24), Raw(Table, 72),
                new[] { new Reader.ScalarNode("pb", Pb, Raw(Pb + 24, 32)),
                    new Reader.ScalarNode("Eb", Eb, Raw(Eb + 24, 32)) }, Context, Now)
            {
                CallerValidated300MetadataAndContext = true,
                FileVersion = Reader.BinaryVersion, FileSha256 = Reader.BinarySha256,
                SourceJassSha256 = Reader.JassSha256,
                Proof = new() { ExperimentalLayoutVerified = true, EvidenceId = "SYNTHETIC ONLY",
                    WorldContextToken = Context }
            };
        }
        internal Reader.Observation Run(Func<ulong, int, byte[]>? read = null,
            Func<string>? context = null, Func<DateTimeOffset>? clock = null, CancellationToken token = default) =>
            Reader.Read(read ?? Read, Nodes, Context, context ?? (() => Context), clock ?? (() => Now), token);
    }

    [Theory] [InlineData(1, 2)] [InlineData(12, 13)] [InlineData(65, 66)]
    public void StableTypedPairIsSourceEvidenceOnly(int pb, int eb)
    {
        var f = new Fixture(pb, eb); var result = f.Run();
        Assert.Equal(Reader.Outcome.SourceRoundObserved, result.State);
        Assert.Equal(pb, result.Pb); Assert.Equal(eb, result.Eb);
        Assert.Equal(16, result.ReadCalls); Assert.Equal(348, result.ReadBytes);
        Assert.Equal(result.ReadCalls, f.Calls); Assert.Equal(result.ReadBytes, f.Bytes);
    }
    [Fact] public void ZeroRoundWithCompletedWriterRelationIsUnknownNotPositive()
    { Assert.Equal(Reader.Outcome.PreRoundUnknown, new Fixture(0, 1).Run().State); }

    [Theory] [InlineData(0, 0)] [InlineData(-1, 0)] [InlineData(66, 67)] [InlineData(65, 65)]
    [InlineData(1, 1)] [InlineData(1, 3)] [InlineData(0, 2)] [InlineData(12, -1)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void InvalidPairsAreNotClampedOrPublished(int pb, int eb)
    { Reject(new Fixture(pb, eb).Run()); }

    [Fact] public void DefaultLayoutProofCannotReadAnyMemory()
    {
        var f = new Fixture(); f.Nodes = f.Nodes with { Proof = new() };
        Reject(f.Run()); Assert.Equal(0, f.Calls);
        Assert.False(new Reader.LayoutProof().ExperimentalLayoutVerified);
    }
    [Theory] [InlineData("caller")] [InlineData("version")] [InlineData("binary")]
    [InlineData("source")] [InlineData("proof-context")] [InlineData("proof-id")]
    [InlineData("payload")] [InlineData("width")] [InlineData("stale")] [InlineData("future")]
    public void MissingPinsOrExternalProofFailBeforeReads(string kind)
    {
        var f = new Fixture();
        f.Nodes = kind switch
        {
            "caller" => f.Nodes with { CallerValidated300MetadataAndContext = false },
            "version" => f.Nodes with { FileVersion = "2.0.4" },
            "binary" => f.Nodes with { FileSha256 = "wrong" },
            "source" => f.Nodes with { SourceJassSha256 = "wrong" },
            "proof-context" => f.Nodes with { Proof = f.Nodes.Proof with { WorldContextToken = "old" } },
            "proof-id" => f.Nodes with { Proof = f.Nodes.Proof with { EvidenceId = "" } },
            "payload" => f.Nodes with { Proof = f.Nodes.Proof with { NativePayloadOffset = 48 } },
            "width" => f.Nodes with { Proof = f.Nodes.Proof with { IntegerWidth = 8 } },
            "stale" => f.Nodes with { MetadataObservedAt = f.Now.AddSeconds(-2) },
            _ => f.Nodes with { MetadataObservedAt = f.Now.AddSeconds(1) }
        };
        Reject(f.Run()); Assert.Equal(0, f.Calls);
    }
    [Theory] [InlineData("owner")] [InlineData("table")] [InlineData("type")]
    [InlineData("declared-type")] [InlineData("name")] [InlineData("link")]
    public void ClearedOrChangedMetadataFailsClosed(string field)
    {
        var f = new Fixture();
        switch (field)
        {
            case "owner": f.Q(Fixture.Owner, 0); break;
            case "table": f.Q(Fixture.Table, 0); break;
            case "type": f.D(Fixture.Pb + 48, 0); break;
            case "declared-type": f.D(Fixture.Eb + 52, 12); break;
            case "name": f.Put(Fixture.PbName, new byte[3]); break;
            case "link": f.Q(Fixture.Pb + 32, 0); break;
        }
        Reject(f.Run());
    }
    [Fact] public void WrongPinnedTypeCannotPassEvenIfLiveMetadataMatches()
    {
        var f = new Fixture(); var bad = f.Nodes.Nodes[0].Metadata.ToArray();
        BitConverter.GetBytes(12).CopyTo(bad, 24);
        f.Nodes = f.Nodes with { Nodes = new[] { f.Nodes.Nodes[0] with { Metadata = bad }, f.Nodes.Nodes[1] } };
        Reject(f.Run()); Assert.Equal(0, f.Calls);
    }
    [Theory] [InlineData("missing")] [InlineData("duplicate-name")] [InlineData("duplicate-address")]
    public void MissingOrDuplicateResolvedNodesRejected(string kind)
    {
        var f = new Fixture(); var first = f.Nodes.Nodes[0]; var second = f.Nodes.Nodes[1];
        f.Nodes = f.Nodes with { Nodes = kind == "missing" ? new[] { first } :
            new[] { first, kind == "duplicate-name" ? second with { Name = "pb" } : second with { Address = first.Address } } };
        Reject(f.Run()); Assert.Equal(0, f.Calls);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void EachScalarIsBracketed(bool changePb)
    {
        var f = new Fixture(); var hits = 0; var address = (changePb ? Fixture.Pb : Fixture.Eb) + 56;
        byte[] Read(ulong a, int n) { if (a == address && ++hits == 2) f.D(a, 44); return f.Read(a, n); }
        Reject(f.Run(Read));
    }
    [Fact] public void MetadataIsCheckedAgainAfterPayload()
    {
        var f = new Fixture(); var hits = 0;
        byte[] Read(ulong a, int n)
        { if (a == Fixture.Owner && ++hits == 2) f.Q(a, 0); return f.Read(a, n); }
        Reject(f.Run(Read));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ContextChangesBeforeOrAfterReject(bool after)
    {
        var f = new Fixture(); var calls = 0;
        Reject(f.Run(context: () => ++calls == 1 && after ? f.Context : "new-generation"));
        if (!after) Assert.Equal(0, f.Calls);
    }
    [Fact] public void ContextTokenMustMatchResolvedMetadata()
    {
        var f = new Fixture(); f.Nodes = f.Nodes with { WorldContextToken = "old-generation" };
        Reject(f.Run()); Assert.Equal(0, f.Calls);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void CancellationNeverPublishesOrThrows(bool during)
    {
        var f = new Fixture(); using var cts = new CancellationTokenSource();
        if (!during) cts.Cancel();
        byte[] Read(ulong a, int n) { cts.Cancel(); return f.Read(a, n); }
        var result = f.Run(Read, token: cts.Token);
        Assert.Equal(Reader.Outcome.Cancelled, result.State); Assert.Null(result.Pb); Assert.Null(result.Eb);
        Assert.InRange(f.Calls, 0, 1);
    }
    [Fact] public void ShortReadsAndAdapterExceptionsNeverEscape()
    {
        Reject(new Fixture().Run((_, _) => Array.Empty<byte>()));
        var result = new Fixture().Run((_, _) => throw new InvalidOperationException("fixture"));
        Assert.Equal(Reader.Outcome.ReadFailure, result.State); Assert.Null(result.Pb);
    }
    [Theory] [InlineData(-1)] [InlineData(101)]
    public void InvalidObservationClockRejected(int milliseconds)
    {
        var f = new Fixture(); var calls = 0;
        Reject(f.Run(clock: () => ++calls == 1 ? f.Now : f.Now.AddMilliseconds(milliseconds)));
    }
    private const string Source2321 = "55D0FFB9921433F45A9244CB946BDD27DCD2552A3550D30C4617C2EACCB94E97";
    private static Fixture ModernFixture(int round = 12, int next = 13)
    {
        var f = new Fixture(round, next);
        f.Put(Fixture.PbName, new byte[] { (byte)'A', (byte)'g', 0 });
        f.Put(Fixture.EbName, new byte[] { (byte)'l', (byte)'g', 0 });
        f.Nodes = f.Nodes with { SourceJassSha256 = Source2321,
            Nodes = new[] { f.Nodes.Nodes[0] with { Name = "Ag" }, f.Nodes.Nodes[1] with { Name = "lg" } } };
        return f;
    }
    [Theory] [InlineData(1, 2)] [InlineData(12, 13)]
    public void Source2321UsesItsOwnTypedNames(int round, int next)
    {
        var f = ModernFixture(round, next); var result = f.Run();
        Assert.Equal(Reader.Outcome.SourceRoundObserved, result.State);
        Assert.Equal(round, result.Pb); Assert.Equal(next, result.Eb);
        Assert.Equal(16, result.ReadCalls); Assert.Equal(348, result.ReadBytes);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void SourceAndNameVersionsCannotBeMixed(bool modernNames)
    {
        var f = modernNames ? ModernFixture() : new Fixture();
        f.Nodes = f.Nodes with { SourceJassSha256 = modernNames ? Reader.JassSha256 : Source2321 };
        Reject(f.Run()); Assert.Equal(0, f.Calls);
    }
    [Fact] public void Source2321StillRequiresExternalProof()
    {
        var f = ModernFixture(); f.Nodes = f.Nodes with { Proof = new() };
        Reject(f.Run()); Assert.Equal(0, f.Calls);
    }
    [Fact] public void Source2321ChangedNamesAndValuesFailClosed()
    {
        var f = ModernFixture(); f.Put(Fixture.PbName, new byte[3]); Reject(f.Run());
        Reject(ModernFixture(12, 14).Run());
        Assert.Equal(Reader.Outcome.PreRoundUnknown, ModernFixture(0, 1).Run().State);
    }
    private static void Reject(Reader.Observation result)
    { Assert.Equal(Reader.Outcome.Rejected, result.State); Assert.Null(result.Pb); Assert.Null(result.Eb); }
}
