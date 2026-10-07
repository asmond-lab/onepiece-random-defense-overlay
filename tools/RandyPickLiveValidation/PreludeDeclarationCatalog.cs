using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using OrandOverlay;

internal static class PreludeDeclarationCatalog
{
    internal const string ResourceHash = "47875266B671E2A53BD0D03409E9E3576F04E82AC2F1A98AD0AA9CECC5DE7697";
    internal const string UnionFingerprint = "CD4D98813C8C60BCF5CD351A32B8BDF7498E7510CF7233F12380BAD0FBDBF44F";
    internal const string CommonHash = "7561E8BE53A01C7105AEC9A09B707AC168495A412D059E68E39E24DCF92F8FBB";
    internal const string BlizzardHash = "B6DB172525D7B106781337252EFCBA4F56BCEB16BB4832680AD07DEA052E6E26";
    internal const string Policy = "JASS-declarations-v1/ordinal/inheritance-no-fallback/code-array-rejected";
    internal static int ResolveTag(string type, bool array, IReadOnlyDictionary<string, string> inheritance)
    {
        var primitives = new Dictionary<string, int>(StringComparer.Ordinal) { ["code"]=3, ["integer"]=4, ["real"]=5, ["string"]=6, ["handle"]=7, ["boolean"]=8 };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!primitives.ContainsKey(type))
        { if (!seen.Add(type) || !inheritance.TryGetValue(type, out var parent)) throw new InvalidDataException("Unresolved/cyclic declaration inheritance"); type = parent; }
        if (array && type == "code") throw new InvalidDataException("Code array rejected");
        return primitives[type] + (array ? 5 : 0);
    }
    internal static Warcraft300DeclaredScope Load(IReadOnlyDictionary<string, int> map)
    {
        using var stream = typeof(PreludeDeclarationCatalog).Assembly.GetManifestResourceStream("RandyPick.PreludeDeclarations") ?? throw new InvalidDataException("Missing pinned prelude");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return Parse(bytes.ToArray(), map);
    }
    internal static Warcraft300DeclaredScope Parse(byte[] bytes, IReadOnlyDictionary<string, int> map)
    {
        if (bytes.Length > 1024*1024 || Convert.ToHexString(SHA256.HashData(bytes)) != ResourceHash) throw new InvalidDataException("Prelude resource hash mismatch");
        using var doc = JsonDocument.Parse(bytes); var root = doc.RootElement;
        if (root.GetProperty("schema").GetInt32()!=1 || root.GetProperty("version").GetString()!=Warcraft300Diagnostic.Version ||
            root.GetProperty("parserPolicy").GetString()!=Policy || root.GetProperty("sources")[0].GetProperty("sha256").GetString()!=CommonHash ||
            root.GetProperty("sources")[1].GetProperty("sha256").GetString()!=BlizzardHash || root.GetProperty("sourceUnionFingerprint").GetString()!=UnionFingerprint)
            throw new InvalidDataException("Prelude provenance mismatch");
        var inheritance = root.GetProperty("inheritance").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!,StringComparer.Ordinal);
        var prelude = new Dictionary<string, Warcraft300Declaration>(StringComparer.Ordinal);
        foreach (var d in root.GetProperty("declarations").EnumerateArray())
        {
            var tag=ResolveTag(d.GetProperty("type").GetString()!,d.GetProperty("array").GetBoolean(),inheritance);
            if(tag!=d.GetProperty("tag").GetInt32() || !prelude.TryAdd(d.GetProperty("name").GetString()!,new(tag,false,d.GetProperty("constant").GetBoolean())))
                throw new InvalidDataException("Prelude declaration duplicate/type mismatch");
        }
        if(map.Count!=3936 || prelude.Count!=2260) throw new InvalidDataException("Source partition counts");
        var scope = new Warcraft300DeclaredScope(map,prelude,Policy+"|"+CommonHash+"|"+BlizzardHash+"|"+Map2320GrowthSource.DataSha256);
        if(scope.Fingerprint!=UnionFingerprint || scope.Declarations.Count!=6196) throw new InvalidDataException("Exact declared union fingerprint mismatch");
        return scope;
    }
}
