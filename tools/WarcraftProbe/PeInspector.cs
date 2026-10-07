using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace WarcraftProbe;

/// <summary>Bounded, file-layout-only PE32+ inspection. RVAs are not runtime addresses or semantic claims.
/// "resolved" means structural validation only, never native binding, field semantics, or gameplay activation.
/// Section hashes cover exact raw section bytes; the image hash covers all supplied bytes, including overlays.
/// Malformed PE/resource bounds throw BadImageFormatException; missing or conflicting versions are null.</summary>
public static class PeInspector
{
    private const int MaxBytes = 256 * 1024 * 1024;
    private const int MaxMatches = 256;
    private static readonly string[] NativeNames =
    [
        "GetWidgetLife", "GetUnitState", "IsUnitType", "IsUnitHidden", "GetOwningPlayer",
        "GetLocalPlayer", "GetPlayerId", "BlzFrameGetText", "BlzFrameSetText", "BlzCreateFrameByType",
        "BlzGetOriginFrame", "BlzFrameGetParent", "BlzGetFrameByName"
    ];
    private static readonly string[] TypeNames =
    [
        "CUnit", "CGameWar3", "CPlayerWar3", "Jass2ScriptInstance_t", "Jass2Script_t", "CGameState",
        "CGameIdMaps", "CMapSetupWar3", "CGameUI", "CTextFrame", "CBackdropFrame", "CTimerDialogWar3", "CTimerWar3"
    ];

    public static ImageInfo Analyze(byte[] image, string fileName)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(fileName);
        if (image.Length > MaxBytes) throw Invalid("Image exceeds the 256 MiB bound.");
        if (fileName.Length > 32768 || fileName.Any(char.IsControl)) throw Invalid("Invalid file name bounds.");
        string leaf = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or "..") throw Invalid("Invalid file name.");
        var r = new Reader(image);
        r.ValidateEnvelope();
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var headers = pe.PEHeaders;
        var h = headers.PEHeader ?? throw Invalid("Missing PE optional header.");
        if (h.Magic != PEMagic.PE32Plus || headers.CoffHeader.Machine != Machine.Amd64)
            throw Invalid("Only AMD64 PE32+ images are supported.");
        r.LoadSections(headers);
        var anchors = r.FindAnchors();
        return new ImageInfo(leaf, image.LongLength, Hash(image), r.ReadVersion(),
            (ushort)headers.CoffHeader.Machine, h.ImageBase, checked((uint)h.SizeOfImage),
            checked((uint)h.SizeOfHeaders), checked((uint)h.AddressOfEntryPoint),
            unchecked((uint)headers.CoffHeader.TimeDateStamp), r.Sections.ToArray(), anchors);
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static BadImageFormatException Invalid(string message) => new("PE bounds/format failure: " + message);
    private static void AddBounded(List<uint> values, uint rva)
    {
        if (values.Count >= MaxMatches) throw Invalid("Anchor exceeds the 256-match bound.");
        values.Add(rva);
    }
    private static AnchorInfo Anchor(string kind, string name, List<uint> values, bool unresolved = false)
    {
        uint[] all = values.Distinct().Order().ToArray();
        return new(kind, name, all, all.Length > 1 ? "ambiguous" : all.Length == 1 ? "resolved" : unresolved ? "unresolved" : "not-found");
    }

    private sealed class Reader(byte[] bytes)
    {
        private readonly byte[] b = bytes;
        internal readonly List<SectionInfo> Sections = [];
        private int optional;
        private int directoryCount;
        private uint imageSize, headerSize;
        private ulong preferredBase;
        private int hierarchyByteBudget = 1_000_000;
        private readonly Dictionary<(uint, uint), bool> hierarchyCache = [];
        private static bool Pow2(uint n) => n != 0 && (n & (n - 1)) == 0;
        private static ulong End(uint start, uint length) => (ulong)start + length;
        private void FileRange(long offset, long length)
        {
            if (offset < 0 || length < 0 || offset > b.LongLength || length > b.LongLength - offset)
                throw Invalid("Truncated or overflowing file range.");
        }
        private ushort U16(int at) { FileRange(at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2)); }
        private uint U32(int at) { FileRange(at, 4); return BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4)); }
        private ulong U64(int at) { FileRange(at, 8); return BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(at, 8)); }
        private static bool Readable(SectionInfo s) => (s.Characteristics & 0x40000000) != 0;
        private static bool Executable(SectionInfo s) => (s.Characteristics & 0x20000000) != 0;
        private static bool ReadOnly(SectionInfo s) => Readable(s) && !Executable(s) && (s.Characteristics & 0x80000000) == 0;
        private static uint Extent(SectionInfo s) => Math.Max(s.VirtualSize, s.RawSize);

        internal void ValidateEnvelope()
        {
            FileRange(0, 64);
            if (U16(0) != 0x5a4d) throw Invalid("Missing MZ signature.");
            uint pe = U32(0x3c);
            if (pe < 64 || pe > int.MaxValue) throw Invalid("Invalid PE header offset.");
            FileRange(pe, 24);
            int p = (int)pe;
            if (U32(p) != 0x4550 || U16(p + 4) != 0x8664) throw Invalid("Not an AMD64 PE image.");
            int count = U16(p + 6), size = U16(p + 20);
            if (count is < 1 or > 96) throw Invalid("Section count must be 1 through 96.");
            optional = p + 24;
            FileRange(optional, size);
            if (size < 112 || U16(optional) != 0x20b) throw Invalid("Not a complete PE32+ optional header.");
            uint directories = U32(optional + 108);
            if (directories > 16 || 112UL + 8UL * directories > (uint)size) throw Invalid("Invalid data directory bounds.");
            directoryCount = (int)directories;
            imageSize = U32(optional + 56); headerSize = U32(optional + 60); preferredBase = U64(optional + 24);
            uint sa = U32(optional + 32), fa = U32(optional + 36);
            if (!Pow2(sa) || !Pow2(fa) || sa < fa || fa > 65536 || (sa < 4096 ? sa != fa : fa < 512))
                throw Invalid("Invalid section/file alignment.");
            if (imageSize == 0 || imageSize > MaxBytes || imageSize % sa != 0 || headerSize == 0 || headerSize % fa != 0 || headerSize > imageSize)
                throw Invalid("Invalid image/header size bounds.");
            if (preferredBase > ulong.MaxValue - imageSize) throw Invalid("Preferred VA range overflows.");
            FileRange(0, headerSize);
            long sectionEnd = (long)optional + size + count * 40L;
            if (sectionEnd > headerSize) throw Invalid("Section table extends beyond headers.");
            FileRange(optional + size, count * 40);
        }

        internal void LoadSections(PEHeaders headers)
        {
            uint sa = U32(optional + 32), fa = U32(optional + 36);
            foreach (var s in headers.SectionHeaders)
            {
                if (s.VirtualAddress < 0 || s.VirtualSize < 0 || s.PointerToRawData < 0 || s.SizeOfRawData < 0)
                    throw Invalid("Negative/overflowing section bounds.");
                uint va = (uint)s.VirtualAddress, vs = (uint)s.VirtualSize, raw = (uint)s.PointerToRawData, rs = (uint)s.SizeOfRawData;
                uint extent = Math.Max(vs, rs);
                if (extent == 0 || va < headerSize || va % sa != 0 || End(va, extent) > imageSize)
                    throw Invalid("Invalid section virtual bounds.");
                if (rs != 0 && (raw < headerSize || raw % fa != 0 || rs % fa != 0)) throw Invalid("Invalid section raw alignment/bounds.");
                FileRange(raw, rs);
                foreach (var old in Sections)
                {
                    if ((ulong)va < End(old.Rva, Extent(old)) && (ulong)old.Rva < End(va, extent)) throw Invalid("Overlapping virtual sections.");
                    if (rs != 0 && old.RawSize != 0 && (ulong)raw < End(old.RawOffset, old.RawSize) && (ulong)old.RawOffset < End(raw, rs))
                        throw Invalid("Overlapping raw sections.");
                }
                Sections.Add(new(s.Name, va, vs, raw, rs, (uint)s.SectionCharacteristics, Hash(b.AsSpan((int)raw, (int)rs))));
            }
            if (Sections.Count != headers.CoffHeader.NumberOfSections) throw Invalid("Inconsistent section count.");
            ulong expected = Sections.Max(s => End(s.Rva, Extent(s)));
            expected = (expected + sa - 1) / sa * sa;
            if (expected != imageSize) throw Invalid("SizeOfImage does not cover exactly the aligned section extent.");
            uint entry = U32(optional + 16);
            if (entry != 0 && !IsCode(entry)) throw Invalid("Entry point is not file-backed executable code.");
            for (int i = 0; i < directoryCount; i++)
            {
                uint address = U32(optional + 112 + i * 8), size = U32(optional + 116 + i * 8);
                if (address == 0 && size == 0) continue;
                if (address == 0 || size == 0) throw Invalid("Incomplete data directory.");
                if (i == 4)
                {
                    FileRange(address, size);
                    if (address < headerSize || address % 8 != 0 || Sections.Any(s => s.RawSize != 0 && (ulong)address < End(s.RawOffset, s.RawSize) && (ulong)s.RawOffset < End(address, size)))
                        throw Invalid("Invalid certificate file range.");
                }
                else Map(address, size);
            }
        }

        private bool TryMap(uint rva, uint size, out int offset, out SectionInfo? section)
        {
            offset = 0; section = null;
            if (size == 0 || End(rva, size) > imageSize) return false;
            if (rva < headerSize && End(rva, size) <= headerSize) { offset = (int)rva; return true; }
            foreach (var s in Sections)
                if (rva >= s.Rva && End(rva - s.Rva, size) <= s.RawSize)
                { offset = checked((int)(s.RawOffset + (rva - s.Rva))); section = s; return true; }
            return false;
        }
        private int Map(uint rva, uint size) => TryMap(rva, size, out int at, out _) ? at : throw Invalid("Unmapped/truncated RVA range.");
        private bool Data(uint rva, uint size, bool readOnly, out int at)
            => TryMap(rva, size, out at, out var s) && s != null && (readOnly ? ReadOnly(s) : Readable(s) && !Executable(s));
        private bool IsCode(uint rva) => TryMap(rva, 1, out _, out var s) && s != null && Executable(s);
        private bool Normalize(ulong va, out uint rva)
        {
            rva = 0;
            if (va < preferredBase || va - preferredBase >= imageSize) return false;
            rva = (uint)(va - preferredBase); return true;
        }
        private List<uint> FindName(string name, bool readOnly)
        {
            byte[] needle = Encoding.ASCII.GetBytes(name + "\0");
            var found = new List<uint>();
            foreach (var s in Sections.Where(s => readOnly ? ReadOnly(s) : Readable(s) && !Executable(s)))
            {
                int cursor = 0;
                var data = b.AsSpan((int)s.RawOffset, (int)s.RawSize);
                while (cursor <= data.Length - needle.Length)
                {
                    int relative = data[cursor..].IndexOf(needle);
                    if (relative < 0) break;
                    int start = cursor + relative;
                    // Exact C strings, not suffixes of unrelated identifiers.
                    if (start == 0 || data[start - 1] == 0) AddBounded(found, s.Rva + (uint)start);
                    cursor = start + needle.Length;
                }
            }
            return found;
        }

        internal AnchorInfo[] FindAnchors()
        {
            var output = new List<AnchorInfo>();
            foreach (string name in NativeNames) output.Add(Anchor("native-name-string", name, FindName(name, true)));
            var types = TypeNames.ToDictionary(n => n, _ => new List<uint>());
            var nameSeen = TypeNames.ToDictionary(n => n, _ => false);
            var typeOwners = new Dictionary<uint, string>();
            foreach (string name in TypeNames)
            {
                var strings = FindName(".?AV" + name + "@@", false);
                output.Add(Anchor("msvc-rtti-name-string", name, strings));
                nameSeen[name] = strings.Count != 0;
                foreach (uint at in strings)
                {
                    if (at < 16 || (at - 16) % 8 != 0 || !Data(at - 16, (uint)(16 + name.Length + 7), false, out int td)) continue;
                    if (U64(td + 8) != 0 || !Normalize(U64(td), out uint vf) || !Data(vf, 8, true, out _)) continue;
                    uint typeRva = at - 16;
                    AddBounded(types[name], typeRva); typeOwners.Add(typeRva, name);
                }
            }
            var cols = new Dictionary<uint, string>();
            var colCandidates = TypeNames.ToDictionary(n => n, _ => new List<uint>());
            foreach (var s in Sections.Where(ReadOnly))
                for (uint off = (4 - s.Rva % 4) % 4; (ulong)off + 24 <= s.RawSize; off += 4)
                {
                    int at = (int)(s.RawOffset + off);
                    if (U32(at) != 1 || !typeOwners.TryGetValue(U32(at + 12), out string? name)) continue;
                    uint rva = s.Rva + off;
                    AddBounded(colCandidates[name], rva);
                    if (U32(at + 20) != rva || !Hierarchy(U32(at + 16), U32(at + 12))) continue;
                    cols.Add(rva, name);
                }
            var tables = TypeNames.ToDictionary(n => n, _ => new List<uint>());
            var candidates = TypeNames.ToDictionary(n => n, _ => new List<uint>());
            foreach (var s in Sections.Where(ReadOnly))
                for (uint off = (8 - s.Rva % 8) % 8; (ulong)off + 8 <= s.RawSize; off += 8)
                {
                    int at = (int)(s.RawOffset + off);
                    if (!Normalize(U64(at), out uint col) || !cols.TryGetValue(col, out string? name)) continue;
                    uint vt = s.Rva + off + 8;
                    AddBounded(candidates[name], vt);
                    if (!Data(vt, 24, true, out int slots)) continue;
                    bool valid = true;
                    for (int i = 0; i < 3; i++)
                        if (!Normalize(U64(slots + i * 8), out uint code) || !IsCode(code)) { valid = false; break; }
                    if (valid) AddBounded(tables[name], vt);
                }
            foreach (string name in TypeNames)
            {
                output.Add(Anchor("msvc-type-descriptor", name, types[name], nameSeen[name]));
                output.Add(Anchor("msvc-complete-object-locator", name, cols.Where(c => c.Value == name).Select(c => c.Key).ToList(), nameSeen[name]));
                output.Add(new AnchorInfo("msvc-complete-object-locator-candidate", name, colCandidates[name].Order().ToArray(),
                    colCandidates[name].Count > 1 ? "ambiguous-candidates" : colCandidates[name].Count == 1 ? "candidate" : nameSeen[name] ? "unresolved" : "not-found"));
                var verified = Anchor("msvc-vtable", name, tables[name], nameSeen[name]);
                if (candidates[name].Count > 1 || colCandidates[name].Count > 1) verified = verified with { Status = "ambiguous-candidates" };
                output.Add(verified);
                output.Add(new AnchorInfo("msvc-vtable-candidate", name, candidates[name].Order().ToArray(),
                    candidates[name].Count > 1 ? "ambiguous-candidates" : candidates[name].Count == 1 ? "candidate" : nameSeen[name] ? "unresolved" : "not-found"));
            }
            return output.ToArray();
        }

        private bool Hierarchy(uint rva, uint expectedType)
        {
            var key = (rva, expectedType);
            if (hierarchyCache.TryGetValue(key, out bool cached)) return cached;
            bool result = CheckHierarchy(rva, expectedType);
            hierarchyCache.Add(key, result);
            return result;
        }

        private bool CheckHierarchy(uint rva, uint expectedType)
        {
            if (rva % 4 != 0 || !Data(rva, 16, true, out int h) || U32(h) != 0 || (U32(h + 4) & ~7u) != 0) return false;
            uint count = U32(h + 8), array = U32(h + 12);
            if (count == 0 || count > 256 || array % 4 != 0 || !Data(array, count * 4, true, out int a)) return false;
            bool includesType = false;
            for (int i = 0; i < count; i++)
            {
                uint descriptor = U32(a + i * 4);
                if (descriptor % 4 != 0 || !Data(descriptor, 28, true, out int d)) return false;
                uint td = U32(d);
                if (td % 8 != 0 || !Data(td, 18, false, out int t) || U32(d + 4) >= count) return false;
                // Bound every referenced type name to its file-backed data section.
                bool terminated = false;
                for (uint j = 16; j < 528; j++)
                {
                    if (--hierarchyByteBudget < 0) throw Invalid("RTTI hierarchy byte-work bound exceeded.");
                    if (!Data(td + j, 1, false, out int c)) return false;
                    if (b[c] == 0) { terminated = j > 16; break; }
                    if (b[c] < 0x20 || b[c] > 0x7e) return false;
                }
                if (!terminated) return false;
                uint nested = U32(d + 24);
                if (nested != 0 && !Data(nested, 16, true, out _)) return false;
                includesType |= td == expectedType;
            }
            return includesType;
        }

        internal string? ReadVersion()
        {
            if (directoryCount <= 2) return null;
            uint root = U32(optional + 128), size = U32(optional + 132);
            if (root == 0 && size == 0) return null;
            int start = Map(root, size), entriesSeen = 0, versionsSeen = 0;
            var visited = new HashSet<uint>();
            var versions = new HashSet<string>(StringComparer.Ordinal);
            int Resource(uint offset, uint length)
            {
                if (End(offset, length) > size) throw Invalid("Resource-relative path escapes directory bounds.");
                return checked(start + (int)offset);
            }
            void Walk(uint offset, int depth, bool version)
            {
                if (depth > 2 || !visited.Add(offset)) throw Invalid("Cyclic/aliased or too-deep resource path.");
                int directory = Resource(offset, 16);
                int named = U16(directory + 12), count = named + U16(directory + 14);
                entriesSeen += count;
                if (entriesSeen > 4096) throw Invalid("Resource entry bound exceeded.");
                int table = Resource(checked(offset + 16), (uint)count * 8);
                for (int i = 0; i < count; i++)
                {
                    uint id = U32(table + i * 8), target = U32(table + i * 8 + 4);
                    bool stringId = (id & 0x80000000) != 0;
                    if (stringId != (i < named)) throw Invalid("Malformed resource identifier table.");
                    if (stringId)
                    {
                        uint text = id & 0x7fffffff;
                        int key = Resource(text, 2);
                        Resource(checked(text + 2), (uint)U16(key) * 2);
                    }
                    else if (id > ushort.MaxValue) throw Invalid("Invalid numeric resource identifier.");
                    bool isVersion = depth == 0 ? !stringId && id == 16 : version;
                    bool child = (target & 0x80000000) != 0;
                    uint relative = target & 0x7fffffff;
                    if (depth < 2)
                    {
                        if (!child) throw Invalid("Resource leaf before language level.");
                        Walk(relative, depth + 1, isVersion);
                    }
                    else
                    {
                        if (child) throw Invalid("Resource directory below language level.");
                        int leaf = Resource(relative, 16);
                        uint dataRva = U32(leaf), length = U32(leaf + 4);
                        if (U32(leaf + 12) != 0 || dataRva < root || End(dataRva - root, length) > size)
                            throw Invalid("Resource payload escapes directory bounds.");
                        int data = Resource(dataRva - root, length);
                        if (isVersion)
                        {
                            if (++versionsSeen > MaxMatches) throw Invalid("Version resource match bound exceeded.");
                            string? v = VersionBlock(data, length);
                            if (v != null) versions.Add(v);
                        }
                    }
                }
            }
            Walk(0, 0, false);
            // Conflicting language/name variants are unknown, never a guessed version.
            return versions.Count == 1 ? versions.Single() : null;
        }

        private string? VersionBlock(int data, uint available)
        {
            if (available < 6) throw Invalid("Truncated VS_VERSION_INFO.");
            uint length = U16(data), valueLength = U16(data + 2);
            if (length < 6 || length > available || U16(data + 4) != 0) throw Invalid("Invalid VS_VERSION_INFO header.");
            byte[] key = Encoding.Unicode.GetBytes("VS_VERSION_INFO\0");
            if (6 + key.Length > length || !b.AsSpan(data + 6, key.Length).SequenceEqual(key)) throw Invalid("Invalid version resource key.");
            int value = (6 + key.Length + 3) & ~3;
            if ((ulong)value + valueLength > length) throw Invalid("Version fixed value exceeds block.");
            if (valueLength == 0) return null;
            if (valueLength != 52 || U32(data + value) != 0xfeef04bd || U32(data + value + 4) != 0x10000)
                throw Invalid("Invalid VS_FIXEDFILEINFO.");
            uint ms = U32(data + value + 8), ls = U32(data + value + 12);
            return $"{ms >> 16}.{ms & 65535}.{ls >> 16}.{ls & 65535}";
        }
    }
}
