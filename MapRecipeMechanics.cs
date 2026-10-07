using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
namespace OrandOverlay;

/// <summary>Bounded ET02 migration, not a full 2.320 recipe catalog.</summary>
public sealed class MapRecipeMechanics
{
    public const string ScriptSha256 = "6FDFC64BF8AD9463F5B5C8A351FFA7E1875D6CF9B51210129539375FA77A6C7C";
    public string MapVersion { get; init; } = "";
    public string MapScriptSha256 { get; init; } = "";
    public string RecipeId { get; init; } = "";
    public string Result { get; init; } = "";
    public string[] Alternatives { get; init; } = [];
    public string Token { get; init; } = "";
    public int TokenCount { get; init; }
    public int Lumber { get; init; }
    public string Source { get; init; } = "";
    public static MapRecipeMechanics Load(string path) => Load(Map2320DataBundle.Normalize(File.ReadAllBytes(path)));
    public static MapRecipeMechanics Load(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length <= 0 || bytes.Length > 8192 ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(Map2320DataBundle.NikaSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unapproved Nika condition data.");
        var value = JsonSerializer.Deserialize<MapRecipeMechanics>(bytes,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
        if (value is null || value.MapVersion != "2.320" || value.MapScriptSha256 != ScriptSha256 ||
            value.RecipeId != "ET02" || value.Result != "H0BK" ||
            !value.Alternatives.SequenceEqual(new[] { "H099", "H0B2" }) ||
            value.Token != "AI01" || value.TokenCount != 1 || value.Lumber != 5)
            throw new InvalidDataException("Unverified 2.320 Nika mechanics.");
        return value;
    }
    public RecipeConditionRequirements Conditions => new(MapVersion, true, Token, TokenCount, Lumber);
    public static bool IsNikaCode(string rawcode) => rawcode == "KB0H" || rawcode.StartsWith("KB0H_", StringComparison.Ordinal);
}
