using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OrandOverlay;

public enum GoalCarryMode
{
    Unknown,
    SoloPreferred,
    MultiAllowed,
    MultiRequired
}

public sealed record GoalCarryPolicyEntry(
    string GoalUnitId,
    GoalCarryMode Mode,
    string Source,
    string Reason);

public sealed class GoalCarryPolicy
{
    public const int SchemaVersion = 1;
    public const int GeneratorVersion = 1;

    private static readonly Regex SoloPattern = new(
        @"(?:^|[\s·,/()\[\]])(?:솔딜|1상위)(?=$|[\s·,/()\[\]])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MultiPattern = new(
        @"(?:^|[\s·,/()\[\]])다상위(?=$|[\s·,/()\[\]])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IReadOnlyDictionary<string, GoalCarryPolicyEntry> _entries;

    private GoalCarryPolicy(IEnumerable<GoalCarryPolicyEntry> entries) =>
        _entries = entries.ToDictionary(entry => entry.GoalUnitId,
            StringComparer.OrdinalIgnoreCase);

    public static GoalCarryPolicy Empty { get; } = new([]);

    public GoalCarryPolicyEntry ForGoal(string goalUnitId) =>
        _entries.GetValueOrDefault(goalUnitId,
            new GoalCarryPolicyEntry(goalUnitId, GoalCarryMode.Unknown,
                "fallback", "정책 항목 없음"));

    public static GoalCarryPolicy Load(string path, DataCatalog catalog)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("목표 carry 정책 데이터가 없습니다.");
        var document = JsonSerializer.Deserialize<GoalCarryPolicyDocument>(
                           File.ReadAllText(path), JsonOptions)
                       ?? throw new InvalidDataException(
                           "목표 carry 정책 데이터를 읽을 수 없습니다.");
        if (document.SchemaVersion != SchemaVersion ||
            document.GeneratorVersion != GeneratorVersion)
            throw new InvalidDataException("지원하지 않는 목표 carry 정책 버전입니다.");

        var expectedSources = SourceIdentities(AppContext.BaseDirectory);
        if (!document.SourceFiles.SequenceEqual(expectedSources))
            throw new InvalidDataException("목표 carry 정책의 원본 SHA256이 현재 데이터와 다릅니다.");

        var topIds = catalog.AllUnits
            .Where(unit => TopGradePolicy.IsTopGrade(unit.Tier))
            .Select(unit => unit.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entryIds = document.Entries.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!entryIds.SetEquals(topIds))
            throw new InvalidDataException(
                "carry 정책 목표 집합이 현재 상위 목표 집합과 다릅니다.");
        if (!document.Source.Equals("tmo-description", StringComparison.Ordinal))
            throw new InvalidDataException("carry 정책 권위 출처가 잘못되었습니다.");
        return new GoalCarryPolicy(document.Entries.Select(pair =>
            ToEntry(pair.Key, pair.Value)));
    }

    public static string GenerateCanonical(DataCatalog catalog,
        string baseDirectory)
    {
        var entries = catalog.AllUnits
            .Where(unit => TopGradePolicy.IsTopGrade(unit.Tier))
            .DistinctBy(unit => unit.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(unit => unit.Id, StringComparer.Ordinal)
            .ToDictionary(unit => unit.Id,
                unit => ClassifyDescription(unit.Description),
                StringComparer.Ordinal);
        var document = new GoalCarryPolicyDocument
        {
            SchemaVersion = SchemaVersion,
            GeneratorVersion = GeneratorVersion,
            SourceFiles = SourceIdentities(baseDirectory).ToList(),
            Source = "tmo-description",
            Entries = entries
        };
        return JsonSerializer.Serialize(document, JsonOptions)
                   .ReplaceLineEndings("\n") + "\n";
    }

    public static GoalCarryMode ClassifyDescription(string? description)
    {
        var text = description ?? "";
        var solo = SoloPattern.IsMatch(text);
        var multi = MultiPattern.IsMatch(text);
        if (solo == multi) return GoalCarryMode.Unknown;
        return solo ? GoalCarryMode.SoloPreferred : GoalCarryMode.MultiAllowed;
    }

    private static GoalCarryPolicyEntry ToEntry(string goalUnitId,
        GoalCarryMode mode) =>
        new(goalUnitId, mode, "tmo-description", mode switch
        {
            GoalCarryMode.SoloPreferred => "TMO 설명의 솔딜/1상위 명시",
            GoalCarryMode.MultiAllowed => "TMO 설명의 다상위 명시",
            _ => "권위 문구 없음"
        });

    private static IReadOnlyList<GoalCarrySourceFile> SourceIdentities(
        string baseDirectory)
    {
        var data = Path.Combine(baseDirectory, "Data");
        return new[]
            {
                "game-data.demo.json",
                "tmo-unit-additions-42479.json",
                "tmo-unit-catalog.json"
            }
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                var canonicalText = File.ReadAllText(Path.Combine(data, name))
                    .ReplaceLineEndings("\n");
                var bytes = Encoding.UTF8.GetBytes(canonicalText);
                return new GoalCarrySourceFile(name,
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            })
            .ToList();
    }

    private sealed class GoalCarryPolicyDocument
    {
        [JsonPropertyOrder(0)] public int SchemaVersion { get; init; }
        [JsonPropertyOrder(1)] public int GeneratorVersion { get; init; }
        [JsonPropertyOrder(2)] public List<GoalCarrySourceFile> SourceFiles { get; init; } = [];
        [JsonPropertyOrder(3)] public string Source { get; init; } = "";
        [JsonPropertyOrder(4)]
        public Dictionary<string, GoalCarryMode> Entries { get; init; } =
            new(StringComparer.Ordinal);
    }
}

public sealed record GoalCarrySourceFile(
    string Path,
    string Sha256);

public static class TopGradePolicy
{
    public static bool IsTopGrade(string tier) =>
        BaseTier(tier) is "신비함" or "초월" or "불멸" or "영원" or "제한됨";

    public static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();
}
