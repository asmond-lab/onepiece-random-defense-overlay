using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OrandOverlay;

internal sealed record Warcraft300Declaration(int Tag, bool IsMap, bool StrictRuntime);
internal sealed class Warcraft300DeclaredScope
{
    internal IReadOnlyDictionary<string, Warcraft300Declaration> Declarations { get; }
    internal string Fingerprint { get; }
    internal Warcraft300DeclaredScope(IReadOnlyDictionary<string, int> map, IEnumerable<KeyValuePair<string, Warcraft300Declaration>> prelude, string policyPin)
    {
        var all = new Dictionary<string, Warcraft300Declaration>(StringComparer.Ordinal);
        foreach (var p in map) all.Add(p.Key, new(p.Value, true, true));
        foreach (var p in prelude) if (!all.TryAdd(p.Key, p.Value)) throw new InvalidDataException("Declared union collision");
        if (all.Count > 20000 || all.Any(p => !Identifier(p.Key) || p.Value.Tag is < 3 or > 13 || p.Value.IsMap && !map.ContainsKey(p.Key)))
            throw new InvalidDataException("Declared union invalid");
        Declarations = new ReadOnlyDictionary<string, Warcraft300Declaration>(all);
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(policyPin + "|" + string.Join(";",
            all.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{p.Value.Tag}:{p.Value.IsMap}:{p.Value.StrictRuntime}")))));
    }
    internal static bool Identifier(string s) => s.Length is >= 1 and <= 255 && (char.IsAsciiLetter(s[0]) || s[0] == '_') && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    internal void ValidateConsumed(string name, uint runtime, uint declared)
    { Validate(name, runtime, declared); if (runtime != declared) throw new InvalidDataException("Consumed runtime type mismatch"); }
    internal void Validate(string name, uint runtime, uint declared)
    {
        if (!Declarations.TryGetValue(name, out var d) || runtime > 13 || declared != d.Tag || d.StrictRuntime && runtime != declared)
            throw new InvalidDataException("Declared scope metadata rejected");
    }
}
internal sealed class Warcraft300SnapshotNode
{
    private readonly byte[] metadata, nameBytes;
    internal string Name { get; }
    internal ulong Address { get; }
    internal ulong NamePointer { get; }
    internal int NameByteCount => nameBytes.Length;
    internal uint RuntimeTag => BitConverter.ToUInt32(metadata, 24);
    internal uint DeclaredTag => BitConverter.ToUInt32(metadata, 28);
    internal Warcraft300SnapshotNode(string name, ulong address, byte[] metadata)
    {
        if (!Warcraft300DeclaredScope.Identifier(name) || metadata.Length != 32 || address < 0x10000 || address > 0x7FFFFFFFFFFF - 63 || (address & 7) != 0)
            throw new InvalidDataException("Snapshot node bounds");
        Name = name; Address = address; this.metadata = (byte[])metadata.Clone();
        NamePointer = BitConverter.ToUInt64(metadata, 16); nameBytes = Encoding.ASCII.GetBytes(name + "\0");
        if (NamePointer < 0x10000 || NamePointer > 0x7FFFFFFFFFFF - (ulong)nameBytes.Length) throw new InvalidDataException("Snapshot name bounds");
    }
    internal void Recheck(Func<ulong, int, byte[]> read)
    {
        if (!metadata.AsSpan().SequenceEqual(read(Address + 24, 32))) throw new InvalidDataException("Snapshot metadata changed");
        // Never follow a newly observed pointer or replace the opening baseline.
        if (!nameBytes.AsSpan().SequenceEqual(read(NamePointer, nameBytes.Length))) throw new InvalidDataException("Snapshot name changed");
    }
}
internal sealed class Warcraft300GlobalsSnapshot
{
    private readonly byte[] ownerHeader, tableHeader;
    internal byte[] OwnerHeaderCopy() => (byte[])ownerHeader.Clone();
    internal byte[] TableHeaderCopy() => (byte[])tableHeader.Clone();
    internal object Context { get; }
    internal ulong Owner { get; }
    internal ulong Table { get; }
    internal ulong Qr { get; }
    internal Guid Invocation { get; } = Guid.NewGuid();
    internal long StartedTimestamp { get; }
    internal string Session { get; }
    internal ulong Module { get; }
    internal ulong World { get; }
    internal Warcraft300Diagnostic.View View { get; }
    internal Warcraft300DeclaredScope Scope { get; }
    internal IReadOnlyList<Warcraft300SnapshotNode> Nodes { get; }
    internal Warcraft300GlobalsSnapshot(long started, string session, ulong module, ulong world, Warcraft300Diagnostic.View view,
        Warcraft300DeclaredScope scope, IEnumerable<Warcraft300SnapshotNode> nodes, byte[] ownerHeader, byte[] tableHeader, object context, ulong owner, ulong table, ulong qr)
    {
        if (ownerHeader.Length != 24 || tableHeader.Length != 72) throw new InvalidDataException("Snapshot headers");
        this.ownerHeader=(byte[])ownerHeader.Clone(); this.tableHeader=(byte[])tableHeader.Clone(); Context=context; Owner=owner; Table=table; Qr=qr;
        StartedTimestamp = started; Session = session; Module = module; World = world; View = view; Scope = scope;
        var copy = nodes.Take(20001).ToArray();
        if (copy.Length != scope.Declarations.Count || copy.Length > 20000 || copy.Select(n => n.Name).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new InvalidDataException("Snapshot incomplete/duplicate");
        var ordered = copy.OrderBy(n => n.Address).ToArray();
        for (int i = 1; i < ordered.Length; i++) if (ordered[i-1].Address + 64 > ordered[i].Address) throw new InvalidDataException("Snapshot overlapping nodes");
        foreach (var n in copy) scope.Validate(n.Name, n.RuntimeTag, n.DeclaredTag);
        Nodes = Array.AsReadOnly(copy);
    }
    internal void RecheckAll(Func<ulong, int, byte[]> read, System.Threading.CancellationToken token)
    { foreach (var node in Nodes) { token.ThrowIfCancellationRequested(); node.Recheck(read); } }
}
