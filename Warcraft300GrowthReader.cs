using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OrandOverlay;

// Empirical diagnostic VM layout, NOT production approval, gameplay-life proof, or
// immutable local-player identity. The caller must bracket this with full world stamps.
// Full discovery requires ALL readable private regions and throws on incomplete enumeration.
// The app can explicitly revalidate a previously discovered context instead. That mode does
// not discover newly appearing duplicate owners and never claims current owner-set uniqueness.
internal enum Warcraft300OwnerDiscoveryMode { FullDiscovery, RevalidateKnownContext }

internal sealed class Warcraft300GrowthReader
{
    internal const int DefaultDiscoveryBlockBytes = 64 * 1024;
    internal const int LargeDiscoveryBlockBytes = 4 * 1024 * 1024;
    private const ulong ScanLimit = 8UL * 1024 * 1024 * 1024;
    private const long MetadataLimit = 8L * 1024 * 1024;
    private Cache? cached;
    internal Warcraft300GlobalsSnapshot? CompletedSnapshot { get; private set; }
    internal void Reset() { cached = null; CompletedSnapshot = null; }

    internal Warcraft300GrowthObservation Read(Func<ulong, int, byte[]> read,
        Func<IEnumerable<MemoryRegion>> regions, ulong moduleBase,
        Warcraft300Diagnostic.View expectedView, ulong world, string sessionKey,
        IReadOnlyDictionary<string, int> expectedGlobals, CancellationToken token = default,
        int discoveryBlockBytes = DefaultDiscoveryBlockBytes,
        Action<Warcraft300SourceScopeAudit>? sourceScopeObserver = null, Warcraft300DeclaredScope? declaredScope = null,
        Func<ulong, byte[], int, int>? readDiscoveryBlock = null, Action? discoveryCheckpoint = null,
        Map2320GrowthSource? source = null, Func<long>? timestamp = null,
        Warcraft300OwnerDiscoveryMode ownerDiscoveryMode = Warcraft300OwnerDiscoveryMode.FullDiscovery,
        IReadOnlySet<string>? activityArrayNames = null)
    {
        CompletedSnapshot = null;
        timestamp ??= Stopwatch.GetTimestamp;
        try
        {
            ArgumentNullException.ThrowIfNull(read);
            ArgumentNullException.ThrowIfNull(regions);
            ArgumentNullException.ThrowIfNull(expectedGlobals);
            // Explicit bulk-discovery sizing only. Validate before ContextNow or ANY target read.
            Require(discoveryBlockBytes == DefaultDiscoveryBlockBytes || discoveryBlockBytes == LargeDiscoveryBlockBytes,
                "Unsupported diagnostic discovery block size.");
            Require(ownerDiscoveryMode is Warcraft300OwnerDiscoveryMode.FullDiscovery or Warcraft300OwnerDiscoveryMode.RevalidateKnownContext,
                "Unsupported owner discovery mode.");
            Require(!string.IsNullOrWhiteSpace(sessionKey), "Missing session identity.");
            token.ThrowIfCancellationRequested();
            var expected = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in expectedGlobals)
            {
                token.ThrowIfCancellationRequested();
                Require(expected.Count < 20000 && Identifier(pair.Key) && pair.Value >= 0 && pair.Value <= 13,
                    "Invalid source declarations.");
                Require(expected.TryAdd(pair.Key, pair.Value), "Duplicate source declaration.");
            }
            if (source is not null) Require(source.Globals.Count == expected.Count && source.Globals.All(p => expected.TryGetValue(p.Key, out var t) && t == p.Value), "Versioned source declaration mismatch.");
            if (activityArrayNames is not null)
                Require((source?.MapVersion is "2.321" or "2.322" or "2.323") && activityArrayNames.Count is > 0 and <= 64 &&
                    activityArrayNames.All(name => expected.TryGetValue(name, out var tag) && tag == 9),
                    "Activity arrays require exact selected-source integer declarations.");
            var growthName = source?.GrowthName ?? "QR";
            Require(expected.Count > 0 && expected.TryGetValue(growthName, out var qrTag) && qrTag == 12,
                "Pinned source declarations must include the growth unit-array tag 12.");
            // Canonical declaration metadata is part of cache identity, not a live-map hash claim.
            var expectedFingerprint = string.Join(";", expected.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + ":" + pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var sourceFingerprint = source is null ? "unversioned" :
                $"{source.MapVersion}:{source.GrowthName}:{source.PreviousName}:{source.NextName}:{source.TimerName}";
            long metadata = 0;
            byte[] Bytes(ulong address, int length)
            {
                token.ThrowIfCancellationRequested();
                Range(address, checked((ulong)length));
                metadata = checked(metadata + length);
                Require(metadata <= MetadataLimit, "Diagnostic metadata budget exceeded.");
                var bytes = read(address, length);
                token.ThrowIfCancellationRequested();
                Require(bytes is not null && bytes.Length == length, "Incomplete diagnostic metadata read.");
                return bytes!;
            }
            ulong Q(ulong a) => U64(Bytes(a, 8));
            uint D(ulong a) => U32(Bytes(a, 4));
            void Typed(ulong a, ulong rva) => Require(Q(a) == Add(moduleBase, rva), "Diagnostic vtable mismatch.");
            void Same(ulong a, byte[] old) => Require(old.AsSpan().SequenceEqual(Bytes(a, old.Length)), "Diagnostic inputs changed.");
            Context ContextNow()
            {
                Range(moduleBase, 1);
                // Guard all additions used inside the existing diagnostic view decoder.
                Add(moduleBase, 0x2E9AD07);
                var view = Warcraft300Diagnostic.ReadView(Bytes, moduleBase);
                Require(view == expectedView && view.Slot <= 3, "Wrong CURRENT-VIEW or non-user slot.");
                var ui = Q(Add(moduleBase, 0x2F5EF00));
                Require(ui == Q(Add(moduleBase, 0x2F85360)), "GameUI globals disagree.");
                Typed(ui, 0x275ED08);
                Typed(world, 0x2764A20);
                Require(Q(Add(world, 0x40)) == ui, "World belongs to another UI.");
                var instance = Q(Add(view.Root, 0x25D0));
                var script = Q(Add(view.Root, 0x25E0));
                var ih = Bytes(instance, 48);
                var sh = Bytes(script, 16);
                Require(U64(ih) == Add(moduleBase, 0x27ECBC0) && U64(sh) == Add(moduleBase, 0x27ECC40), "Wrong VM instance/script type.");
                var manager = Q(Add(view.Root, 0x2620));
                var registry = Q(Add(moduleBase, 0x2F807F0));
                Range(manager, 0x2A0); Range(registry, 0x6C);
                return new(sessionKey, moduleBase, view, world, ui, instance, script, manager, registry,
                    Convert.ToHexString(ih), Convert.ToHexString(sh), expectedFingerprint, sourceFingerprint);
            }
            if (declaredScope is not null && (declaredScope.Declarations.Count(p => p.Value.IsMap) != expected.Count ||
                expected.Any(p => !declaredScope.Declarations.TryGetValue(p.Key, out var d) || !d.IsMap || d.Tag != p.Value || !d.StrictRuntime)))
                throw new InvalidDataException("Scope/map contract mismatch");
            var before = ContextNow();
            if (cached is not null && cached.Context != before) Reset();
            ulong aggregate, table;
            // Scope audits keep their original complete-discovery contract even if the caller
            // requests app reuse. Reuse checks the known owner, not the whole current owner set.
            var reusedContext = ownerDiscoveryMode == Warcraft300OwnerDiscoveryMode.RevalidateKnownContext &&
                sourceScopeObserver is null && declaredScope is null && cached is not null;
            var actualDiscoveryMode = reusedContext ? Warcraft300OwnerDiscoveryMode.RevalidateKnownContext : Warcraft300OwnerDiscoveryMode.FullDiscovery;
            if (reusedContext)
            {
                aggregate = cached!.Aggregate;
                table = cached.Table;
            }
            else
            {
                var matches = new Dictionary<ulong, ulong>();
                var discoveryBuffer = readDiscoveryBlock is null ? null : new byte[discoveryBlockBytes];
                ulong scanned = 0, previousEnd = 0;
                var discoveryStarted = timestamp();
                void CheckScan()
                {
                    token.ThrowIfCancellationRequested();
                    Require(timestamp() >= discoveryStarted &&
                        Stopwatch.GetElapsedTime(discoveryStarted, timestamp()) < TimeSpan.FromSeconds(32),
                        "Incomplete discovery: time cap.");
                    discoveryCheckpoint?.Invoke();
                    token.ThrowIfCancellationRequested();
                    Require(timestamp() >= discoveryStarted &&
                        Stopwatch.GetElapsedTime(discoveryStarted, timestamp()) < TimeSpan.FromSeconds(32),
                        "Incomplete discovery: time cap.");
                }
                using var iterator = regions().GetEnumerator();
                while (true)
                {
                    CheckScan();
                    var more = iterator.MoveNext();
                    CheckScan();
                    if (!more) break; // The supplied strict enumerator owns OS completion proof.
                    var region = iterator.Current;
                    Range(region.BaseAddress, region.Size);
                    Require(region.Size <= ScanLimit - scanned, "Incomplete discovery: region exceeds remaining byte cap.");
                    var end = Add(region.BaseAddress, region.Size);
                    Require(region.BaseAddress >= previousEnd, "Overlapping/unordered discovery regions.");
                    previousEnd = end;
                    for (var a = region.BaseAddress; a < end;)
                    {
                        CheckScan();
                        var n = (int)Math.Min((ulong)discoveryBlockBytes, end - a);
                        scanned = checked(scanned + (ulong)n);
                        Require(scanned <= ScanLimit, "Incomplete discovery: byte cap.");
                        var block = discoveryBuffer ?? read(a, n);
                        var actual = discoveryBuffer is null ? block?.Length ?? 0 : readDiscoveryBlock!(a, discoveryBuffer, n);
                        CheckScan();
                        Require(block is not null && actual == n,
                            $"Incomplete discovery: short read at 0x{a:X} ({actual}/{n}).");
                        var lanes = AlignedDiscoveryLanes(block!.AsSpan(0, n), a, out var firstOffset);
                        for (var offset = 0; offset < lanes.Length;)
                        {
                            var hit = lanes[offset..].IndexOf(before.Instance);
                            if (hit < 0) break;
                            var lane = offset + hit;
                            offset = lane + 1;
                            var i = firstOffset + lane * 8;
                            var candidate = Add(a, (ulong)i);
                            var header = Bytes(candidate, 24); // Deliberately crosses scan chunk boundaries.
                            Require(U64(header) == before.Instance, "Instance reference changed during discovery.");
                            if (U64(header, 8) != Add(moduleBase, 0x13F3070)) continue;
                            var t = U64(header, 16);
                            if (Q(t) != Add(moduleBase, 0x2809B78)) continue;
                            matches[candidate] = t;
                            Require(matches.Count <= 16, "Too many owner aggregates.");
                        }
                        // Overlap seven bytes so an unaligned region start cannot lose a QWORD.
                        a = Add(a, (ulong)((ulong)n == end - a ? n : n - 7));
                    }
                }
                CheckScan();
                Require(matches.Count == 1, "Missing or ambiguous current-instance owner aggregate.");
                aggregate = matches.First().Key; table = matches.First().Value;
            }
            // Full discovery may take up to 32s. Both modes start a new sample here; no
            // cached values, old timestamps or a previous zero QR slot can renew freshness.
            Require(before == ContextNow(), "Diagnostic roots/headers/session view changed.");
            var sampleStarted = timestamp();
            if (cached is not null)
            {
                Require(cached.Aggregate == aggregate && cached.Table == table, "Cached owner/table identity changed.");
                Same(aggregate, cached.OwnerHeader); Same(table, cached.TableHeader);
            }
            var ownerHeader = Bytes(aggregate, 24);
            var tableHeader = Bytes(table, 72);
            Require(U64(ownerHeader) == before.Instance && U64(ownerHeader, 8) == Add(moduleBase, 0x13F3070) &&
                U64(ownerHeader, 16) == table && U64(tableHeader) == Add(moduleBase, 0x2809B78) && U32(tableHeader, 8) == 24,
                "Owner/table layout mismatch.");
            var nodes = new List<Node>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<ulong>();
            var current = U64(tableHeader, 24);
            var previous = Add(table, 16);
            var sentinel = Add(table, 17);
            ulong qr = 0;
            string ReadName(ulong pointer)
            {
                var text = new List<byte>(256);
                for (var offset = 0; offset < 256;)
                {
                    var address = Add(pointer, (ulong)offset);
                    // A short terminated name at a page end must not require the next page.
                    var length = Math.Min(Math.Min(32, 256 - offset), (int)(0x1000UL - (address & 0xFFFUL)));
                    var part = Bytes(address, length);
                    offset += length;
                    foreach (var b in part)
                    {
                        if (b == 0)
                        {
                            var name = Encoding.ASCII.GetString(text.ToArray());
                            Require(Identifier(name), "Invalid variable identifier.");
                            return name;
                        }
                        Require(b < 128, "Non-ASCII variable identifier.");
                        text.Add(b);
                    }
                }
                throw new InvalidDataException("Unterminated variable name.");
            }
            while (current != sentinel)
            {
                token.ThrowIfCancellationRequested();
                Require(nodes.Count < 20000 && (current & 7) == 0 && seen.Add(current), "Invalid/cyclic/overlong globals list.");
                var node = Bytes(current, 64);
                var type = U32(node, 48); var declared = U32(node, 52);
                Require(type <= 13 && declared <= 13 && U64(node, 24) == previous, "Invalid globals type/link.");
                var namePointer = U64(node, 40);
                var name = ReadName(namePointer);
                Require(names.Add(name), "Duplicate globals identifier.");
                if (expected.TryGetValue(name, out var tag))
                    Require(type == tag && declared == tag, "Source globals type mismatch.");
                declaredScope?.Validate(name, type, declared);
                if (name == growthName)
                {
                    Require(type == 12 && declared == 12, "QR must be unit array.");
                    qr = current;
                }
                nodes.Add(new(current, node.AsSpan(24, 32).ToArray(), namePointer, name));
                previous = Add(current, 24);
                current = U64(node, 32);
            }
            Require(previous == U64(tableHeader, 16) && qr != 0 && expected.Keys.All(names.Contains), "Globals tail/source declarations missing.");
            if (declaredScope is not null) Require(nodes.Count == declaredScope.Declarations.Count, "Declared union missing globals.");
            if (cached is not null) Require(cached.Qr == qr, "Cached QR identity changed.");
            var array = Q(Add(qr, 56));
            // Read only meaningful array fields, never ABI padding.
            var arrayVt = Q(array); var count = D(Add(array, 8));
            var data = Q(Add(array, 16)); var capacity = D(Add(array, 24));
            Require(arrayVt == Add(moduleBase, 0x2809EF8) && count >= 1 && count <= 4 &&
                capacity >= count && capacity <= 32 && before.View.Slot < count, "QR array/slot not observed.");
            Range(data, checked(4UL * capacity));
            var slotAddress = Add(data, 4UL * before.View.Slot);
            var handle = D(slotAddress);
            ulong? unit = null; uint? raw = null; Warcraft300HandleStamp? allocation = null;
            ulong entry = 0, jassTable = 0; uint limit = 0; byte[]? entryHeader = null;
            if (handle != 0)
            {
                Require(handle >= 0x100000, "Invalid JASS unit handle.");
                limit = D(Add(before.Manager, 0x290)); jassTable = Q(Add(before.Manager, 0x298));
                var index = handle - 0x100000;
                Require(limit > 0 && limit <= 1048576 && index < limit, "JASS handle outside table bounds.");
                Range(jassTable, 24UL * limit);
                entry = Add(jassTable, 24UL * index); entryHeader = Bytes(entry, 24);
                Require(U32(entryHeader) > 0, "JASS reference count is not positive.");
                unit = U64(entryHeader, 8);
                allocation = Warcraft300HandleValidator.Read(Bytes, moduleBase, unit.Value, token);
                Require(allocation.Value.Registry == before.Registry, "Native registry changed.");
                Require(D(Add(unit.Value, 0x1C0)) == 27, "QR unit is not neutral passive.");
                raw = D(Add(unit.Value, 0x178)); Require(raw != 0, "QR unit has zero rawcode.");
            }
            // Recheck structural inputs, not unrelated globals' scalar payload values.
            foreach (var node in nodes)
            {
                Same(Add(node.Address, 24), node.Inputs);
                Require(ReadName(node.NamePointer) == node.Name, "Variable name changed.");
            }
            if (unit.HasValue)
            {
                var second = Warcraft300HandleValidator.Read(Bytes, moduleBase, unit.Value, token);
                Require(allocation == second && D(Add(unit.Value, 0x1C0)) == 27 && D(Add(unit.Value, 0x178)) == raw,
                    "QR native allocation/owner/rawcode changed.");
                Same(entry, entryHeader!);
                Require(D(Add(before.Manager, 0x290)) == limit && Q(Add(before.Manager, 0x298)) == jassTable, "JASS table changed.");
            }
            Require(Q(Add(qr, 56)) == array && Q(array) == arrayVt && D(Add(array, 8)) == count &&
                Q(Add(array, 16)) == data && D(Add(array, 24)) == capacity && D(slotAddress) == handle, "QR array/slot changed.");
            Same(aggregate, ownerHeader); Same(table, tableHeader);
            Require(before == ContextNow(), "Diagnostic roots/headers/session view changed.");
            token.ThrowIfCancellationRequested();
            Warcraft300SourceScopeAudit? sourceScopeAudit = null;
            if (sourceScopeObserver is not null)
            {
                // Everything below uses only already-validated local names/metadata. No native reads.
                var entries = new Warcraft300SourceScopeEntry[nodes.Count];
                for (var index = 0; index < nodes.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var node = nodes[index];
                    entries[index] = new(Warcraft300SourceScopeAudit.HashIdentifier(node.Name), node.Name.Length,
                        U32(node.Inputs, 24), U32(node.Inputs, 28), expected.ContainsKey(node.Name));
                }
                sourceScopeAudit = new(entries, expected.Count);
                token.ThrowIfCancellationRequested();
            }
            var snapshot = declaredScope is null ? null : new Warcraft300GlobalsSnapshot(sampleStarted, sessionKey, moduleBase, world, before.View,
                declaredScope, nodes.Select(n => new Warcraft300SnapshotNode(n.Name, n.Address, n.Inputs)), ownerHeader, tableHeader, before, aggregate, table, qr);
            // Optional selected metadata only. No new target I/O, payload, union policy, or unit-list gate.
            var roundInputs = Warcraft300RoundInputs.TryCreate(sampleStarted, sessionKey, moduleBase, before.View, world, before.Ui,
                before.Instance, before.Script, before.Manager, before.Registry, aggregate, table, ownerHeader, tableHeader,
                Convert.FromHexString(before.InstanceHeader), Convert.FromHexString(before.ScriptHeader),
                nodes.Where(n => n.Name == (source?.PreviousName ?? "pb") || n.Name == (source?.NextName ?? "Eb") || n.Name == (source?.TimerName ?? "qg")).Select(n => (n.Name, n.Address, n.Inputs)), source);
            token.ThrowIfCancellationRequested();
            cached = new(before, aggregate, table, qr, ownerHeader, tableHeader);
            CompletedSnapshot = snapshot;
            if (sourceScopeAudit is not null) sourceScopeObserver!(sourceScopeAudit);
            var ownerStatus = reusedContext
                ? " Known owner context revalidated; current owner-set uniqueness not rechecked."
                : " Owner-set uniqueness checked by full discovery for this observation.";
            return new(unit, raw, allocation, before.View,
                (handle == 0 ? "Absent: explicitly observed stable QR slot DWORD zero (diagnostic only)." :
                    "Observed neutral-passive QR association (diagnostic only; world membership and recognized growth require caller verification).") + ownerStatus,
                world, before.Instance, before.Script, before.Manager, before.Registry, sessionKey, aggregate, table, qr, handle)
            { GlobalsSnapshot = snapshot, RoundInputs = roundInputs, SampleStartedTimestamp = sampleStarted,
                ActivityInputs = activityArrayNames is not null && roundInputs is not null
                    ? new(roundInputs, Array.AsReadOnly(nodes.Where(node => activityArrayNames.Contains(node.Name))
                        .Select(node => new Warcraft300SnapshotNode(node.Name, node.Address, node.Inputs)).ToArray()))
                    : null,
                OwnerDiscoveryMode = actualDiscoveryMode };
        }
        catch (OverflowException error) { Reset(); throw new InvalidDataException("Diagnostic address arithmetic overflow.", error); }
        catch { Reset(); throw; }
    }

    // Windows target pointers are little-endian QWORDs. Align against the remote
    // address, not the managed array index; callers pass only the valid read prefix.
    internal static ReadOnlySpan<ulong> AlignedDiscoveryLanes(ReadOnlySpan<byte> block, ulong address, out int firstOffset)
    {
        firstOffset = (int)((8 - (address & 7)) & 7);
        if (block.Length - firstOffset < 8) return ReadOnlySpan<ulong>.Empty;
        var completeBytes = ((block.Length - firstOffset) / 8) * 8;
        return MemoryMarshal.Cast<byte, ulong>(block.Slice(firstOffset, completeBytes));
    }

    private sealed record Context(string Session, ulong Module, Warcraft300Diagnostic.View View, ulong World, ulong Ui,
        ulong Instance, ulong Script, ulong Manager, ulong Registry, string InstanceHeader, string ScriptHeader,
        string ExpectedGlobalsFingerprint, string SourceFingerprint);
    private sealed record Cache(Context Context, ulong Aggregate, ulong Table, ulong Qr, byte[] OwnerHeader, byte[] TableHeader);
    private sealed record Node(ulong Address, byte[] Inputs, ulong NamePointer, string Name);
    private static uint U32(byte[] bytes, int offset = 0) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static ulong U64(byte[] bytes, int offset = 0) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8));
    private static ulong Add(ulong a, ulong b) { var result = checked(a + b); Range(result, 1); return result; }
    private static void Range(ulong address, ulong length)
    {
        Require(length > 0 && address >= 0x10000 && address <= 0x7FFFFFFFFFFF &&
            checked(address + length - 1) <= 0x7FFFFFFFFFFF, "Invalid diagnostic address/span.");
    }
    private static bool Identifier(string text) => text.Length >= 1 && text.Length <= 255 &&
        (Letter(text[0]) || text[0] == '_') && text.All(c => Letter(c) || c == '_' || c >= '0' && c <= '9');
    private static bool Letter(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z';
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}

internal sealed record Warcraft300GrowthObservation(ulong? UnitPointer, uint? Rawcode,
    Warcraft300HandleStamp? Allocation, Warcraft300Diagnostic.View CurrentView, string StatusDescription,
    ulong World, ulong Instance, ulong Script, ulong MapManager, ulong NativeRegistry, string SessionKey,
    ulong OwnerAggregate, ulong DataTable, ulong QrNode, uint JassHandle)
{
    internal ulong Root => CurrentView.Root;
    internal Warcraft300GlobalsSnapshot? GlobalsSnapshot { get; init; }
    internal Warcraft300RoundInputs? RoundInputs { get; init; }
    internal Warcraft300ActivityInputs? ActivityInputs { get; init; }
    internal long SampleStartedTimestamp { get; init; }
    internal Warcraft300OwnerDiscoveryMode OwnerDiscoveryMode { get; init; } = Warcraft300OwnerDiscoveryMode.FullDiscovery;
    // An explicit diagnostic distinction, never production/local-player authority.
    internal bool OwnerSetUniquenessChecked => OwnerDiscoveryMode == Warcraft300OwnerDiscoveryMode.FullDiscovery;
}


// Metadata-only export. Never contains a raw identifier, native pointer, payload or semantic approval.
internal sealed record Warcraft300SourceScopeEntry(string NormalizedNameSha256, int IdentifierLength,
    uint RuntimeTypeTag, uint DeclaredTypeTag, bool IsExpectedMapDeclaration);

internal sealed class Warcraft300SourceScopeAudit
{
    public IReadOnlyList<Warcraft300SourceScopeEntry> Entries { get; }
    public int TotalCount => Entries.Count;
    public int ExpectedSourceDeclarationCount { get; }
    public int MatchedExpectedMapDeclarationCount { get; }
    public int OtherIdentifierCount => TotalCount - MatchedExpectedMapDeclarationCount;
    public string FreshnessAuthority => "Original outer ScanStartedAt/ScanAgeMilliseconds; this audit does not renew freshness";
    public string Scope => "Validated declaration metadata only; not script/map identity, semantics, scalar layout or current-value approval";

    internal Warcraft300SourceScopeAudit(IReadOnlyList<Warcraft300SourceScopeEntry> entries, int expectedSourceDeclarationCount)
    {
        var count = entries?.Count ?? 0;
        if (entries is null || count is < 1 or > 20000 || expectedSourceDeclarationCount is < 1 or > 20000)
            throw new InvalidDataException("Source-scope audit bounds rejected.");
        var copy = new Warcraft300SourceScopeEntry[count];
        int matched = 0;
        for (int i = 0; i < copy.Length; i++)
        {
            var entry = entries[i];
            if (entry is null || entry.NormalizedNameSha256 is not { Length: 64 } ||
                !entry.NormalizedNameSha256.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F') ||
                entry.IdentifierLength is < 1 or > 255 || entry.RuntimeTypeTag > 13 || entry.DeclaredTypeTag > 13)
                throw new InvalidDataException("Source-scope audit metadata rejected.");
            copy[i] = entry with { }; // Clone entry and collection so no caller-owned array can alter the export.
            if (entry.IsExpectedMapDeclaration) matched++;
        }
        if (matched != expectedSourceDeclarationCount)
            throw new InvalidDataException("Source-scope expected declarations incomplete.");
        Entries = Array.AsReadOnly(copy);
        ExpectedSourceDeclarationCount = expectedSourceDeclarationCount;
        MatchedExpectedMapDeclarationCount = matched;
    }
    internal static string HashIdentifier(string name) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.Normalize(NormalizationForm.FormC))));
}
