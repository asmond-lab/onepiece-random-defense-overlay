using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace OrandOverlay;

/// <summary>
/// Immutable hand-stat transcription from the user-selected TMO helper. It is
/// display evidence, not a replacement for the catalog or recommendation data.
/// </summary>
public sealed class HandStatsProfile
{
    public const int ExpectedSchemaVersion = 1;
    public const int ExpectedSourceGuideId = 48784;
    public const string ExpectedSourceUrl = "https://tmo.gg/g/ord/build-helper/48784";
    public const string BundledFileName = "tmo-hand-stats-48784.json";

    private readonly ImmutableDictionary<string, HandStatsUnit> _unitsByRawcode;
    private readonly ImmutableArray<HandStatsUnit> _units;
    private static readonly Lazy<HandStatsProfile?> BundledProfile = new(LoadBundledUncached, true);

    private HandStatsProfile(string sourceTitle, DateTimeOffset capturedAt,
        ImmutableDictionary<string, HandStatsUnit> unitsByRawcode,
        ImmutableArray<string> unmapped)
    {
        SourceTitle = sourceTitle;
        CapturedAt = capturedAt;
        _unitsByRawcode = unitsByRawcode;
        _units = unitsByRawcode.Values.ToImmutableArray();
        Unmapped = unmapped;
    }

    public int SchemaVersion => ExpectedSchemaVersion;
    public int SourceGuideId => ExpectedSourceGuideId;
    public string SourceUrl => ExpectedSourceUrl;
    public string SourceTitle { get; }
    public DateTimeOffset CapturedAt { get; }
    public IReadOnlyList<HandStatsUnit> Units => _units;
    public IReadOnlyList<string> Unmapped { get; }
    public string SourceLabel => $"TMO helper {SourceGuideId}: {SourceTitle}";

    /// <summary>Returns null only when the optional bundled source has not yet been installed.</summary>
    public static HandStatsProfile? LoadBundled() => BundledProfile.Value;

    private static HandStatsProfile? LoadBundledUncached()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", BundledFileName);
        return File.Exists(path) ? LoadFromFile(path) : null;
    }

    /// <summary>Invalid existing source files throw instead of being partially installed.</summary>
    public static HandStatsProfile LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return Parse(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"손패 스탯 프로필 JSON이 올바르지 않습니다: {path}", exception);
        }
    }

    /// <summary>Convenient immutable construction for focused tests and non-file hosts.</summary>
    public static HandStatsProfile CreateForTests(string sourceTitle,
        IEnumerable<HandStatsUnit> units, DateTimeOffset? capturedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTitle);
        ArgumentNullException.ThrowIfNull(units);
        var map = ImmutableDictionary.CreateBuilder<string, HandStatsUnit>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            var snapshot = unit with
            {
                Abilities = unit.Abilities.ToImmutableDictionary(StringComparer.Ordinal),
                Notes = unit.Notes.ToImmutableArray(),
                Exclusions = unit.Exclusions.ToImmutableArray(),
                NonStackingGroups = unit.NonStackingGroups.ToImmutableDictionary(StringComparer.Ordinal)
            };
            ValidateUnit(snapshot);
            if (!map.TryAdd(snapshot.Rawcode, snapshot))
                throw new InvalidDataException($"손패 스탯 프로필에 중복 rawcode가 있습니다: {snapshot.Rawcode}");
        }
        return new HandStatsProfile(sourceTitle, capturedAt ?? DateTimeOffset.UtcNow,
            map.ToImmutable(), []);
    }

    public bool TryGet(string rawcode, out HandStatsUnit unit) =>
        _unitsByRawcode.TryGetValue(rawcode, out unit!);

    private static HandStatsProfile Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("손패 스탯 프로필 루트는 객체여야 합니다.");
        if (RequiredInt(root, "schemaVersion") != ExpectedSchemaVersion)
            throw new InvalidDataException($"지원하지 않는 손패 스탯 스키마입니다. 예상값: {ExpectedSchemaVersion}");
        if (RequiredInt(root, "sourceGuideId") != ExpectedSourceGuideId)
            throw new InvalidDataException($"손패 스탯 출처 ID가 {ExpectedSourceGuideId}가 아닙니다.");
        if (!string.Equals(RequiredString(root, "sourceUrl"), ExpectedSourceUrl, StringComparison.Ordinal))
            throw new InvalidDataException("손패 스탯 출처 URL이 예상한 TMO helper URL과 다릅니다.");

        var sourceTitle = RequiredString(root, "sourceTitle");
        if (!DateTimeOffset.TryParse(RequiredString(root, "capturedAt"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var capturedAt))
            throw new InvalidDataException("손패 스탯 capturedAt은 ISO-8601 시간이어야 합니다.");

        var unitsElement = Required(root, "units");
        if (unitsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("손패 스탯 units는 배열이어야 합니다.");
        var units = ImmutableDictionary.CreateBuilder<string, HandStatsUnit>(StringComparer.Ordinal);
        foreach (var item in unitsElement.EnumerateArray())
        {
            var unit = ParseUnit(item);
            if (!units.TryAdd(unit.Rawcode, unit))
                throw new InvalidDataException($"손패 스탯 프로필에 중복 rawcode가 있습니다: {unit.Rawcode}");
        }

        var unmapped = ImmutableArray.CreateBuilder<string>();
        if (root.TryGetProperty("unmapped", out var unmappedElement))
        {
            if (unmappedElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("손패 스탯 unmapped는 배열이어야 합니다.");
            foreach (var item in unmappedElement.EnumerateArray())
                unmapped.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText());
        }
        return new HandStatsProfile(sourceTitle, capturedAt, units.ToImmutable(), unmapped.ToImmutable());
    }

    private static HandStatsUnit ParseUnit(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("손패 스탯 unit은 객체여야 합니다.");
        var abilitiesElement = Required(element, "abilities");
        if (abilitiesElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("손패 스탯 abilities는 객체여야 합니다.");
        var abilities = ImmutableDictionary.CreateBuilder<string, HandStatsAbility>(StringComparer.Ordinal);
        foreach (var property in abilitiesElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
                throw new InvalidDataException("손패 스탯 ability 이름이 비어 있습니다.");
            if (!abilities.TryAdd(property.Name, HandStatsAbility.FromJson(property.Value)))
                throw new InvalidDataException($"손패 스탯 ability가 중복되었습니다: {property.Name}");
        }
        var notes = ReadStringArray(element, "notes", required: true);
        var exclusions = ReadStringArray(element, "exclusions", required: false);
        var unit = new HandStatsUnit(RequiredString(element, "rawcode"), RequiredString(element, "name"),
            RequiredString(element, "tier"), RequiredString(element, "sourceName"),
            RequiredString(element, "sourceTier"), RequiredString(element, "sourceText", allowEmpty: true),
            abilities.ToImmutable(), notes, exclusions)
        {
            NonStackingGroups = ReadNonStackingGroups(element)
        };
        ValidateUnit(unit);
        return unit;
    }

    private static void ValidateUnit(HandStatsUnit unit)
    {
        if (!IsValidRawcode(unit.Rawcode))
            throw new InvalidDataException($"손패 스탯 rawcode 형식이 올바르지 않습니다: {unit.Rawcode}");
        if (string.IsNullOrWhiteSpace(unit.Name) || string.IsNullOrWhiteSpace(unit.Tier) ||
            string.IsNullOrWhiteSpace(unit.SourceName) || string.IsNullOrWhiteSpace(unit.SourceTier))
            throw new InvalidDataException($"손패 스탯 unit 메타데이터가 비어 있습니다: {unit.Rawcode}");
        if (unit.Abilities.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || !pair.Value.IsValid))
            throw new InvalidDataException($"손패 스탯 ability 값이 유효하지 않습니다: {unit.Rawcode}");
        foreach (var (abilityName, groupId) in unit.NonStackingGroups)
        {
            var reviewedPair = abilityName == "방어력 감소" &&
                groupId is "hawkins-special-rare-armor" or "bonclay-ivankov-armor" or "drake-king-armor" ||
                abilityName == "단일방어력 감소" && groupId == "gaban-kargara-youmu-single-armor";
            if (!reviewedPair || !unit.TryGetNumber(abilityName, out var value) || value < 0)
                throw new InvalidDataException($"손패 스탯 nonStackingGroups가 유효하지 않습니다: {unit.Rawcode}/{abilityName}");
        }
    }

    private static bool IsValidRawcode(string rawcode) => rawcode.Length == 4 &&
        rawcode.All(char.IsLetterOrDigit) ||
        rawcode.Length == 5 && rawcode[^1] == '_' && rawcode[..4].All(char.IsLetterOrDigit);

    private static ImmutableDictionary<string, string> ReadNonStackingGroups(JsonElement element)
    {
        if (!element.TryGetProperty("nonStackingGroups", out var value)) return ImmutableDictionary<string, string>.Empty;
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("손패 스탯 nonStackingGroups는 객체여야 합니다.");
        var groups = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()) ||
                !groups.TryAdd(property.Name, property.Value.GetString()!))
                throw new InvalidDataException("손패 스탯 nonStackingGroups는 고유한 비어 있지 않은 문자열이어야 합니다.");
        }
        return groups.ToImmutable();
    }

    private static ImmutableArray<string> ReadStringArray(JsonElement element, string property, bool required)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            if (required) throw new InvalidDataException($"손패 스탯 {property}가 없습니다.");
            return [];
        }
        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
            throw new InvalidDataException($"손패 스탯 {property}는 문자열 배열이어야 합니다.");
        return value.EnumerateArray().Select(x => x.GetString()!).ToImmutableArray();
    }

    private static JsonElement Required(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw new InvalidDataException($"손패 스탯 {name}가 없습니다.");

    private static string RequiredString(JsonElement element, string name, bool allowEmpty = false)
    {
        var value = Required(element, name);
        if (value.ValueKind != JsonValueKind.String || (!allowEmpty && string.IsNullOrWhiteSpace(value.GetString())))
            throw new InvalidDataException($"손패 스탯 {name}는 {(allowEmpty ? "문자열" : "비어 있지 않은 문자열")}이어야 합니다.");
        return value.GetString()!;
    }

    private static int RequiredInt(JsonElement element, string name)
    {
        var value = Required(element, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number : throw new InvalidDataException($"손패 스탯 {name}는 정수여야 합니다.");
    }
}

public sealed record HandStatsUnit(
    string Rawcode,
    string Name,
    string Tier,
    string SourceName,
    string SourceTier,
    string SourceText,
    IReadOnlyDictionary<string, HandStatsAbility> Abilities,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Exclusions)
{
    /// <summary>Only reviewed fixed armor groups from the source may opt into this rule.</summary>
    public IReadOnlyDictionary<string, string> NonStackingGroups { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    public bool TryGetNumber(string abilityName, out double value)
    {
        if (Abilities.TryGetValue(abilityName, out var ability) && ability.Number is double number)
        {
            value = number;
            return true;
        }
        value = 0;
        return false;
    }

    public bool HasTrue(string abilityName) => Abilities.TryGetValue(abilityName, out var ability) && ability.Boolean == true;
}

/// <summary>Preserves explicit zero, booleans, and textual unknowns without coercing them.</summary>
public sealed record HandStatsAbility(double? Number, bool? Boolean, string? Text)
{
    internal const double MaximumMagnitude = 1_000_000d;
    internal bool IsValid =>
        (Number.HasValue ? 1 : 0) + (Boolean.HasValue ? 1 : 0) + (Text is null ? 0 : 1) == 1 &&
        (Number is not double number || double.IsFinite(number) && Math.Abs(number) <= MaximumMagnitude);

    internal static HandStatsAbility FromJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDouble(out var number) && double.IsFinite(number) &&
                                  Math.Abs(number) <= MaximumMagnitude => new(number, null, null),
        JsonValueKind.True => new(null, true, null),
        JsonValueKind.False => new(null, false, null),
        JsonValueKind.String => new(null, null, value.GetString()),
        _ => throw new InvalidDataException("손패 스탯 ability 값은 유한 숫자, boolean 또는 문자열이어야 합니다.")
    };
}

