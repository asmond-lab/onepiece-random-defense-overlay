using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrandOverlay;

public sealed record NormalGuideUnit(string UnitId, bool StoryFast, string DamageType,
    IReadOnlyList<string> MovementRoles)
{
    public IReadOnlyList<string> RecommendedPartners { get; init; } = [];
}

public static class NormalGuideProfile
{
    public const string FileName = "randypick-normal-guide-48129.json";
    public const string SourceUrl = "https://tmo.gg/g/ord/build-helper/48129";
    private const int MaxBytes = 512 * 1024;

    public static IReadOnlyList<NormalGuideUnit> LoadBundled(IEnumerable<string>? knownUnitIds = null)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", FileName));
            if (stream.Length is <= 0 or > MaxBytes) return [];
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return Parse(bytes, knownUnitIds);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<NormalGuideUnit> Parse(ReadOnlyMemory<byte> bytes,
        IEnumerable<string>? knownUnitIds = null)
    {
        if (bytes.Length is <= 0 or > MaxBytes)
            throw new InvalidDataException("Normal guide profile size is invalid.");
        try
        {
            var knownIds = knownUnitIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("guideId").GetString() != "48129" ||
                root.GetProperty("sourceUrl").GetString() != SourceUrl ||
                root.GetProperty("status").GetString() != "reference" ||
                root.GetProperty("catalogMapVersion").GetString() != "2.320")
                throw new InvalidDataException("Normal guide profile source does not match.");

            var units = ImmutableArray.CreateBuilder<NormalGuideUnit>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in root.GetProperty("units").EnumerateArray())
            {
                var id = item.GetProperty("unitId").GetString();
                var damage = item.GetProperty("damageType").GetString();
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) ||
                    damage is not ("physical" or "magical" or "both" or "unknown"))
                    throw new InvalidDataException("Normal guide profile unit is invalid or duplicated.");

                var roles = ImmutableArray.CreateBuilder<string>();
                foreach (var roleElement in item.GetProperty("movementRoles").EnumerateArray())
                {
                    var role = roleElement.GetString();
                    if (role is not ("공중이동" or "지형무시이동" or "순간이동") || roles.Contains(role))
                        throw new InvalidDataException("Normal guide profile movement role is invalid or duplicated.");
                    roles.Add(role);
                }
                var partners = ImmutableArray.CreateBuilder<string>();
                if (item.TryGetProperty("recommendedPartners", out var partnerElements))
                {
                    foreach (var partnerElement in partnerElements.EnumerateArray())
                    {
                        var partner = partnerElement.GetString();
                        if (partner is null || !Regex.IsMatch(partner,
                            "^(?:rawcode:[A-Za-z0-9_]{4,5}|[a-z][a-z0-9_]*)$"))
                            throw new InvalidDataException("Normal guide profile partner ID is invalid.");
                        if ((knownIds is null || knownIds.Contains(partner)) && !partners.Contains(partner))
                            partners.Add(partner);
                    }
                }
                units.Add(new NormalGuideUnit(id, item.GetProperty("storyFast").GetBoolean(),
                    damage, roles.ToImmutable()) { RecommendedPartners = partners.ToImmutable() });
            }
            return units.ToImmutable();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidDataException("Normal guide profile JSON is invalid.", exception);
        }
    }
}
