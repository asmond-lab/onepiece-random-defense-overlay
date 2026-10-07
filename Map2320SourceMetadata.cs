using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

/// <summary>Exact, bounded 2.320 source evidence. This is not archive approval or runtime compatibility.</summary>
public static class Map2320SourceMetadata
{
    public const string DataFileName = "map-source-metadata-2320.json";
    public const int MaxMetadataBytes = 262144;
    public const string DataSha256 = "e6f53445f8bc0dd0f174bd5fab0eb095295c19c8f40b32e657990a3c0a8311e6";
    public const string SemanticSha256 = "74219edfdc91b6816f9d09696ab40077a210b7d97cd85ecf6cc34e1d982c63bc";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 24,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static MapSourceMetadata LoadBundled()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Data", DataFileName);
        using var stream = File.OpenRead(file);
        if (stream.Length <= 0 || stream.Length > MaxMetadataBytes) throw new InvalidDataException("Metadata size exceeds bound.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (Hash(bytes) != DataSha256) throw new InvalidDataException("Bundled metadata byte hash mismatch.");
        return Load(bytes);
    }

    /// <summary>Allows formatting/property-order changes, never semantic changes. Returns immutable legacy DTOs without modifying their schema.</summary>
    public static MapSourceMetadata Load(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxMetadataBytes) throw new InvalidDataException("Metadata size exceeds bound.");
        try
        {
            using var doc = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
            var canonical = Canonical(doc.RootElement);
            var metadata = JsonSerializer.Deserialize<MapSourceMetadata>(bytes, Options)
                ?? throw new InvalidDataException("Null metadata.");
            // Fixed semantic digest is the exact schema, required-field, range, ordering and source-pin contract.
            // Canonical rejects duplicates and non-integral JSON numbers before hashing. Unknown fields are also rejected by the serializer.
            if (Hash(Encoding.UTF8.GetBytes(canonical)) != SemanticSha256)
                throw new InvalidDataException("2.320 metadata semantic contract mismatch.");
            return metadata;
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid metadata schema or type.", ex); }
    }

    /// <summary>Optional offline verification of all eight extracted source byte arrays. Does not open or approve an external archive.</summary>
    public static void ValidateSourceMembers(MapSourceMetadata metadata, IReadOnlyDictionary<string, byte[]> members)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(members);
        _ = Load(JsonSerializer.SerializeToUtf8Bytes(metadata, Options));
        if (members.Count != metadata.Members.Length) throw new InvalidDataException("Exactly eight extracted members required.");
        foreach (var pin in metadata.Members)
            if (!members.TryGetValue(pin.Name, out var bytes) || bytes is null || bytes.LongLength != pin.LengthBytes || Hash(bytes) != pin.Sha256)
                throw new InvalidDataException("Source byte hash/length mismatch: " + pin.Name);
    }

    public static string ComputeSemanticHash(MapSourceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        using var doc = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(metadata, Options));
        return Hash(Encoding.UTF8.GetBytes(Canonical(doc.RootElement)));
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Canonical(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = element.EnumerateObject().ToArray();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in properties)
                    if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate metadata property: " + property.Name);
                return "{" + string.Join(",", properties.OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => JsonSerializer.Serialize(p.Name, Options) + ":" + Canonical(p.Value))) + "}";
            case JsonValueKind.Array:
                return "[" + string.Join(",", element.EnumerateArray().Select(Canonical)) + "]";
            case JsonValueKind.String:
                return JsonSerializer.Serialize(element.GetString(), Options);
            case JsonValueKind.Number:
                if (!element.TryGetInt64(out var number)) throw new InvalidDataException("Metadata numbers must be bounded integers.");
                return number.ToString(CultureInfo.InvariantCulture);
            default:
                throw new InvalidDataException("Null, boolean, and undefined metadata values are forbidden.");
        }
    }
}
