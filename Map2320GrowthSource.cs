using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace OrandOverlay;

// Auxiliary diagnostic schema, not an eighth member of the closed offline map bundle.
// Declaration agreement does not verify the running script's bytes or approve a native reader.
internal sealed class Map2320GrowthSource
{
    internal const string FileName = "map-growth-globals-2320.json";
    internal const string DataSha256 = "08D09CA1386CFD57E5935BBFA9C2E595644B410AE61F7054089357FAA0904909";
    internal const string JassSha256 = "6fdfc64bf8ad9463f5b5c8a351ffa7e1875d6cf9b51210129539375fa77a6c7c";
    internal const int NormalizedLength = 52536;
    internal string MapVersion { get; }
    internal string GrowthName => MapVersion switch { "2.323" => "kU", "2.322" => "yl", "2.321" => "Vu", _ => "QR" };
    internal string PreviousName => MapVersion switch { "2.323" => "Av", "2.322" => "GR", "2.321" => "Ag", _ => "pb" };
    internal string NextName => MapVersion switch { "2.323" => "Ov", "2.322" => "hR", "2.321" => "lg", _ => "Eb" };
    internal string TimerName => MapVersion switch { "2.323" => "hp", "2.322" => "Vs", "2.321" => "yp", _ => "qg" };
    internal IReadOnlyDictionary<string, int> Globals { get; }

    private Map2320GrowthSource(ImmutableDictionary<string, int> globals, string version) { Globals = globals; MapVersion = version; }

    internal static Map2320GrowthSource LoadBundled() => LoadBundled("2.320");

    internal static Map2320GrowthSource LoadBundled(string mapVersion)
    {
        if (mapVersion is not ("2.320" or "2.321" or "2.322" or "2.323")) throw new InvalidDataException("Unsupported growth source.");
        var path = Path.Combine(AppContext.BaseDirectory, "Data", mapVersion switch { "2.323" => "map-growth-globals-2323.json", "2.322" => "map-growth-globals-2322.json", "2.321" => "map-growth-globals-2321.json", _ => FileName });
        if (new FileInfo(path).LinkTarget is not null) throw new InvalidDataException("Growth schema links rejected.");
        using var stream = File.OpenRead(path);
        if (stream.Length is <= 0 or > 128 * 1024) throw new InvalidDataException("Growth schema size rejected.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Growth schema changed during read.");
        return Load(bytes, mapVersion);
    }

    internal static Map2320GrowthSource Load(byte[] bytes, string mapVersion = "2.320")
    {
        if (bytes is null || bytes.Length is <= 0 or > 128 * 1024)
            throw new InvalidDataException("Growth schema size rejected.");
        byte[] normalized;
        try { normalized = Map2320DataBundle.Normalize(bytes); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("Growth schema UTF-8 rejected.", error); }
        if (mapVersion is not ("2.320" or "2.321" or "2.322" or "2.323")) throw new InvalidDataException("Unsupported growth source.");
        var modern = mapVersion == "2.321";
        var next = mapVersion == "2.322";
        var latest = mapVersion == "2.323";
        if (normalized.Length != (latest ? 52283 : next ? 52297 : modern ? 52397 : NormalizedLength) ||
            Convert.ToHexString(SHA256.HashData(normalized)) != (latest ? Map2323SourceContract.GrowthDataSha256.ToUpperInvariant() : next ? "CB15BA1E524C9C055384284F0715C11F16FDE31FA908AD9D5779F8B5D90F7E6E" : modern ? "8B151B4FA743E2944C805BF1ADE7F127FBB867731A98DC02E04AEB961E1E196C" : DataSha256))
            throw new InvalidDataException("Growth schema integrity mismatch.");
        using var document = JsonDocument.Parse(normalized);
        var root = document.RootElement;
        if (root.EnumerateObject().Count() != 4 || root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("mapVersion").GetString() != mapVersion ||
            root.GetProperty("jassSha256").GetString() != (latest ? Map2323SourceContract.JassSha256 : next ? Map2322SourceContract.JassSha256 : modern ? "55d0ffb9921433f45a9244cb946bdd27dcd2552a3550d30c4617c2eaccb94e97" : JassSha256))
            throw new InvalidDataException("Growth schema source mismatch.");
        var result = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var property in root.GetProperty("globals").EnumerateObject())
        {
            var name = property.Name;
            var type = property.Value.GetInt32();
            if (name.Length is < 1 or > 255 || !(char.IsAsciiLetter(name[0]) || name[0] == '_') ||
                name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') || type is < 3 or > 13 ||
                !result.TryAdd(name, type))
                throw new InvalidDataException("Growth declaration rejected.");
        }
        if (result.Count != (latest ? 3925 : next ? 3926 : modern ? 3926 : 3936) || !result.TryGetValue(latest ? "kU" : next ? "yl" : modern ? "Vu" : "QR", out var qrType) || qrType != 12)
            throw new InvalidDataException("Growth declarations incomplete.");
        return new(result.ToImmutable(), mapVersion);
    }
}
