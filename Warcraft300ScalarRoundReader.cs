using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace OrandOverlay;

// SOURCE ONLY. No discovery, process API, cache, activation, or gameplay model dependency.
internal static class Warcraft300ScalarRoundReader
{
    internal const string BinaryVersion = "3.0.0.24268";
    internal const string BinarySha256 = "BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12";
    internal const string JassSha256 = "6FDFC64BF8AD9463F5B5C8A351FFA7E1875D6CF9B51210129539375FA77A6C7C";
    internal const string Jass2321Sha256 = "55D0FFB9921433F45A9244CB946BDD27DCD2552A3550D30C4617C2EACCB94E97";
    internal const int MaximumReadCalls = 16;
    internal const int MaximumReadBytes = 348;
    internal static readonly TimeSpan MaximumDuration = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan MaximumMetadataAge = TimeSpan.FromSeconds(1);

    // Inputs must come from a complete, unique current-instance globals traversal, not
    // a two-name search. Metadata is node bytes [24,56), excluding native payload.
    internal sealed record ScalarNode(string Name, ulong Address, byte[] Metadata);
    internal sealed record LayoutProof
    {
        internal bool ExperimentalLayoutVerified { get; init; } = false;
        internal string EvidenceId { get; init; } = "";
        internal string WorldContextToken { get; init; } = "";
        internal int NativePayloadOffset { get; init; } = 56;
        internal int IntegerWidth { get; init; } = 4;
    }
    internal sealed record ResolvedScalarNodes(
        ulong ModuleBase, ulong Instance, ulong OwnerAggregate, ulong DataTable,
        byte[] OwnerHeader, byte[] TableHeader, IReadOnlyList<ScalarNode> Nodes,
        string WorldContextToken, DateTimeOffset MetadataObservedAt)
    {
        internal bool CallerValidated300MetadataAndContext { get; init; } = false;
        internal string FileVersion { get; init; } = "";
        internal string FileSha256 { get; init; } = "";
        internal string SourceJassSha256 { get; init; } = "";
        internal LayoutProof Proof { get; init; } = new();
    }
    internal enum Outcome { Rejected, Cancelled, ReadFailure, PreRoundUnknown, SourceRoundObserved }
    // Source pair only. No phase, end-state, life, map-identity, or approval assertion.
    internal sealed record Observation(Outcome State, int? Pb, int? Eb, string Reason,
        string WorldContextToken, DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
        int ReadCalls, int ReadBytes);

    internal static Observation Read(Func<ulong, int, byte[]> read, ResolvedScalarNodes nodes,
        string worldContextToken, Func<string> readWorldContextToken,
        Func<DateTimeOffset> utcNow, CancellationToken cancellationToken = default)
    {
        var calls = 0;
        var bytesRead = 0;
        var started = default(DateTimeOffset);
        var finished = default(DateTimeOffset);
        var watch = Stopwatch.StartNew();
        Observation Result(Outcome state, string reason, int? pb = null, int? eb = null) =>
            new(state, pb, eb, reason, worldContextToken, started, finished, calls, bytesRead);
        void Check()
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(watch.Elapsed <= MaximumDuration, "Read duration exceeded.");
        }
        try
        {
            Check();
            Require(read is not null && nodes is not null && readWorldContextToken is not null && utcNow is not null,
                "Missing adapter input.");
            started = utcNow!(); finished = started;
            Require(!string.IsNullOrWhiteSpace(worldContextToken) &&
                nodes!.WorldContextToken == worldContextToken && nodes.CallerValidated300MetadataAndContext,
                "Caller-validated current 3.0 metadata/context required.");
            Require(nodes.FileVersion == BinaryVersion && nodes.FileSha256 == BinarySha256 &&
                nodes.SourceJassSha256 is JassSha256 or Jass2321Sha256, "Exact binary/source pins required.");
            // Versioned source names only. This does not discover nodes or prove the live map.
            var previousName = nodes.SourceJassSha256 == Jass2321Sha256 ? "Ag" : "pb";
            var nextName = nodes.SourceJassSha256 == Jass2321Sha256 ? "lg" : "Eb";
            var proof = nodes.Proof;
            Require(proof is not null && proof.ExperimentalLayoutVerified &&
                !string.IsNullOrWhiteSpace(proof.EvidenceId) && proof.WorldContextToken == worldContextToken &&
                proof.NativePayloadOffset == 56 && proof.IntegerWidth == 4,
                "Experimental native scalar layout proof missing or incompatible.");
            Require(nodes.MetadataObservedAt <= started && started - nodes.MetadataObservedAt <= MaximumMetadataAge,
                "Stale/future metadata timestamp.");
            Require(readWorldContextToken!() == worldContextToken, "World context changed.");
            Require(nodes.Nodes is not null && nodes.Nodes.Count == 2, "Exactly two resolved scalar nodes required.");
            ScalarNode? pbNode = null, ebNode = null;
            foreach (var node in nodes.Nodes!)
            {
                Require(node is not null && node.Metadata is not null && node.Metadata.Length == 32,
                    "Missing scalar metadata.");
                var copy = node! with { Metadata = (byte[])node.Metadata.Clone() };
                Require((copy.Address & 7) == 0, "Unaligned scalar node.");
                Range(copy.Address, 60);
                Require(U32(copy.Metadata, 24) == 4 && U32(copy.Metadata, 28) == 4,
                    "Scalar runtime/declaration type must both be integer (4).");
                Range(U64(copy.Metadata, 0), 1); Range(U64(copy.Metadata, 8), 1);
                Range(U64(copy.Metadata, 16), 3);
                if (copy.Name == previousName && pbNode is null) pbNode = copy;
                else if (copy.Name == nextName && ebNode is null) ebNode = copy;
                else throw new InvalidDataException("Duplicate or unexpected scalar name.");
            }
            Require(pbNode is not null && ebNode is not null && pbNode.Address != ebNode.Address,
                "Missing or duplicate scalar address.");
            Require(nodes.OwnerHeader is not null && nodes.OwnerHeader.Length == 24 &&
                nodes.TableHeader is not null && nodes.TableHeader.Length == 72, "Missing owner/table headers.");
            var owner = (byte[])nodes.OwnerHeader!.Clone();
            var table = (byte[])nodes.TableHeader!.Clone();
            Range(nodes.ModuleBase, 1); Range(nodes.Instance, 1);
            Require(U64(owner, 0) == nodes.Instance && U64(owner, 8) == Add(nodes.ModuleBase, 0x13F3070) &&
                U64(owner, 16) == nodes.DataTable && U64(table, 0) == Add(nodes.ModuleBase, 0x2809B78) &&
                U32(table, 8) == 24, "Wrong 3.0 owner/table header.");
            byte[] Bytes(ulong address, int length)
            {
                Check(); Range(address, length);
                Require(++calls <= MaximumReadCalls && (bytesRead += length) <= MaximumReadBytes,
                    "Read budget exceeded.");
                var data = read!(address, length);
                Check();
                Require(data is not null && data.Length == length, "Incomplete read.");
                return (byte[])data!.Clone();
            }
            void Same(ulong address, byte[] expected) =>
                Require(expected.AsSpan().SequenceEqual(Bytes(address, expected.Length)), "Metadata/header changed.");
            void NodeMetadata(ScalarNode node)
            {
                Same(Add(node.Address, 24), node.Metadata);
                var name = Bytes(U64(node.Metadata, 16), 3);
                Require(name[0] == (byte)node.Name[0] && name[1] == (byte)node.Name[1] && name[2] == 0,
                    "Scalar name changed.");
            }
            void Metadata()
            {
                Same(nodes.OwnerAggregate, owner); Same(nodes.DataTable, table);
                NodeMetadata(pbNode!); NodeMetadata(ebNode!);
            }
            // +56 is read only AFTER explicit external layout verification and typed metadata.
            int Scalar(ScalarNode node) => BinaryPrimitives.ReadInt32LittleEndian(Bytes(Add(node.Address, 56), 4));
            Metadata();
            var pb = Scalar(pbNode!); var eb = Scalar(ebNode!);
            var ebAgain = Scalar(ebNode!); var pbAgain = Scalar(pbNode!);
            Metadata();
            Require(pb == pbAgain && eb == ebAgain, "Scalar values changed during bracket.");
            Require(readWorldContextToken!() == worldContextToken, "World context changed.");
            finished = utcNow!();
            Require(finished >= started && finished - started <= MaximumDuration &&
                finished - nodes.MetadataObservedAt <= MaximumMetadataAge, "Invalid/stale observation timestamps.");
            Check();
            Require(pb >= 0 && pb <= 65 && eb >= 0 && eb <= 66, "Source pair outside authored bounds; not clamped.");
            // Initial 0/0 has no completed-writer relation and is rejected, never positive evidence.
            Require(eb == pb + 1, "Sequential source-pair relation mismatch.");
            if (pb == 0)
                return Result(Outcome.PreRoundUnknown, "Pre-round/unknown; no positive round or phase evidence.", pb, eb);
            return Result(Outcome.SourceRoundObserved, "Stable diagnostic source pair only; not phase or end-state.", pb, eb);
        }
        catch (OperationCanceledException) { return Result(Outcome.Cancelled, "Cancelled; no pair published."); }
        catch (InvalidDataException error) { return Result(Outcome.Rejected, error.Message); }
        catch (Exception) { return Result(Outcome.ReadFailure, "Adapter read/context/clock failed; no pair published."); }
    }

    private static uint U32(byte[] b, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset, 4));
    private static ulong U64(byte[] b, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(offset, 8));
    private static ulong Add(ulong a, ulong b)
    {
        Require(a <= 0x7FFFFFFFFFFF && b <= 0x7FFFFFFFFFFF - a, "Address overflow.");
        var result = a + b; Range(result, 1); return result;
    }
    private static void Range(ulong address, int length) => Require(length > 0 && address >= 0x10000 &&
        address <= 0x7FFFFFFFFFFF && (ulong)(length - 1) <= 0x7FFFFFFFFFFF - address, "Invalid read range.");
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException(reason);
    }
}
