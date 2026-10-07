using System.Globalization;

namespace OrandOverlay;

public sealed record InventoryStatSummary(
    double Stun,
    double Slow,
    double TriggeredSlow,
    double ArmorReduction,
    double TriggeredArmorReduction,
    double StackingArmorReduction,
    double SingleArmorReduction,
    double AttackBoost,
    double TriggeredAttackBoost,
    double AttackSpeed,
    double HealthRegen,
    double ManaRegen,
    int ArmorBreakProviders,
    int BossControlProviders,
    int BerserkControlProviders,
    int AirMovementProviders,
    int TeleportProviders,
    int BurgessProviders,
    int SingleDamageProviders = 0,
    int FinisherDamageProviders = 0,
    double MagicArmorReduction = 0,
    double MagicAmp = 0)
{
    public double TotalSlow => Slow + TriggeredSlow;
    // Source stacking values are disclosed as potential, not counted toward the live threshold total.
    public double TotalArmorReduction => ArmorReduction + TriggeredArmorReduction + StackingArmorReduction - UnobservedStackingArmorReduction;
    public double TotalAttackBoost => AttackBoost + TriggeredAttackBoost;

    // Init-only additions preserve existing positional constructor callers.
    public string SourceLabel { get; init; } = "legacy catalog abilities";
    public int SourceUnitCount { get; init; }
    public int UnlistedUnitCount { get; init; }
    public int UnknownValueUnitCount { get; init; }
    public string SourceNotes { get; init; } = "";
    public string ProfileStatus { get; init; } = "legacy";
    public double ExplosionAmp { get; init; }
    public double AllDamageAmp { get; init; }
    public double SingleMagicAmp { get; init; }
    public double SingleDamageWeight { get; init; }
    public double FinisherDamageWeight { get; init; }
    public double UnobservedStackingArmorReduction { get; init; }
    public bool IsLegacyReferenceForSelectedMap { get; init; }
}

/// <summary>
/// Computes hand display stats. Matched source-profile units use only their explicit
/// TMO helper values; all unlisted units retain the established catalog fallback.
/// </summary>
public sealed class InventoryStatsCalculator
{
    private readonly DataCatalog _catalog;
    private readonly HandStatsProfile? _profile;

    public InventoryStatsCalculator(DataCatalog catalog, HandStatsProfile? profile = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _profile = profile ?? HandStatsProfile.LoadBundled();
    }

    public InventoryStatSummary Calculate(IEnumerable<InventoryEntry> inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var stun = 0d; var slow = 0d; var triggeredSlow = 0d;
        var armor = 0d; var triggeredArmor = 0d; var stackingArmor = 0d; var singleArmor = 0d;
        var attack = 0d; var triggeredAttack = 0d; var attackSpeed = 0d;
        var healthRegen = 0d; var manaRegen = 0d; var magicArmor = 0d; var magicAmp = 0d;
        var explosionAmp = 0d; var allDamageAmp = 0d; var singleMagicAmp = 0d;
        var singleDamageWeight = 0d; var finisherDamageWeight = 0d;
        var armorBreakProviders = 0; var bossProviders = 0; var berserkProviders = 0;
        var airMovementProviders = 0; var teleportProviders = 0; var burgessProviders = 0;
        var singleDamageProviders = 0; var finisherDamageProviders = 0;
        var conditionalJinbeCount = 0; var sourceUnitCount = 0; var unlistedUnitCount = 0;
        var unknownValueUnitCount = 0; var unobservedStackingArmor = 0d;
        var sourceNotes = new List<string>();
        var nonStackingMaximums = new Dictionary<(string AbilityName, string GroupId), double>();

        foreach (var entry in inventory)
        {
            // Corrupt recognition counts fail closed instead of overflowing a display total.
            if (entry.Count <= 0 || entry.Count > 10_000) continue;
            var unit = _catalog.Unit(entry.UnitId);
            var count = entry.Count;
            if (TryGetProfileUnit(entry.UnitId, unit, out var source))
            {
                sourceUnitCount += count;
                unobservedStackingArmor += Accumulate(source, count, nonStackingMaximums,
                    ref stun, ref slow, ref triggeredSlow, ref armor, ref triggeredArmor, ref stackingArmor,
                    ref singleArmor, ref attack, ref triggeredAttack, ref attackSpeed, ref healthRegen,
                    ref manaRegen, ref magicArmor, ref magicAmp, ref explosionAmp, ref allDamageAmp,
                    ref singleMagicAmp, ref singleDamageWeight, ref finisherDamageWeight);
                if (HasProvider(source, "아머브레이크", "단일아머브레이크")) armorBreakProviders += count;
                if (HasProvider(source, "보스 잡기")) bossProviders += count;
                if (HasProvider(source, "광폭화 잡기", "광폭화")) berserkProviders += count;
                if (HasProvider(source, "공중이동")) airMovementProviders += count;
                if (HasProvider(source, "순간이동")) teleportProviders += count;
                if (HasProvider(source, "바제스")) burgessProviders += count;
                if (HasProvider(source, "단일")) singleDamageProviders += count;
                if (HasProvider(source, "끝딜")) finisherDamageProviders += count;
                if (IsUnknownSourceUnit(source)) unknownValueUnitCount += count;
                AddNotes(sourceNotes, source);
                continue;
            }

            unlistedUnitCount += count;
            stun += Value(unit, "스턴") * count;
            slow += Value(unit, "이동속도 감소") * count;
            triggeredSlow += Value(unit, "발동이동속도 감소") * count;
            armor += Value(unit, "방어력 감소") * count;
            triggeredArmor += Value(unit, "발동방어력 감소") * count;
            if (unit.Rawcodes.Contains("W30h", StringComparer.Ordinal)) triggeredArmor += 30 * count;
            stackingArmor += Value(unit, "중첩방어력 감소") * count;
            singleArmor += Value(unit, "단일방어력 감소") * count;
            attack += Value(unit, "공격력 증가") * count;
            triggeredAttack += Value(unit, "발동공격력 증가") * count;
            attackSpeed += Value(unit, "공격속도 증가") * count;
            healthRegen += Value(unit, "체력 재생") * count;
            manaRegen += Value(unit, "마나 재생") * count;
            magicArmor += Value(unit, "마법방어력 감소") * count;
            magicAmp += Value(unit, "마법데미지 증폭") * count;
            if (Has(unit, "아머브레이크", "단일아머브레이크")) armorBreakProviders += count;
            if (Has(unit, "보스 잡기")) bossProviders += count;
            if (Has(unit, "광폭화 잡기", "광폭화")) berserkProviders += count;
            if (Map2322KaidoAirRole.CountsAsAir(_catalog.MapVersion, unit,
                Has(unit, "공중이동"))) airMovementProviders += count;
            if (Has(unit, "순간이동")) teleportProviders += count;
            if (Has(unit, "바제스")) burgessProviders += count;
            if (Has(unit, "단일")) singleDamageProviders += count;
            if (Has(unit, "끝딜")) finisherDamageProviders += count;
            if (unit.Rawcodes.Contains("G30h", StringComparer.Ordinal)) conditionalJinbeCount += count;
        }

        if (conditionalJinbeCount > 0 && armorBreakProviders > 0) triggeredArmor += conditionalJinbeCount * 25;
        var status = _profile is null ? "legacy: source profile unavailable" :
            sourceUnitCount == 0 ? "legacy: inventory units unlisted by source" :
            unlistedUnitCount == 0 ? "source profile" : "mixed: source profile plus legacy fallback";
        var referenceOnly = Map2320DataBundle.IsCompatible(_catalog.MapVersion) ||
            _catalog.MapVersion is "2.322" or "2.323";
        return new InventoryStatSummary(stun, slow, triggeredSlow, armor, triggeredArmor,
            stackingArmor, singleArmor, attack, triggeredAttack, attackSpeed, healthRegen, manaRegen,
            armorBreakProviders, bossProviders, berserkProviders, airMovementProviders, teleportProviders,
            burgessProviders, singleDamageProviders, finisherDamageProviders, magicArmor, magicAmp)
        {
            SourceLabel = (_profile?.SourceLabel ?? "legacy catalog abilities") +
                (referenceOnly ? $" · {_catalog.MapVersion} 검증값이 아닌 이전 출처 참고" : ""),
            SourceUnitCount = sourceUnitCount,
            UnlistedUnitCount = unlistedUnitCount,
            UnknownValueUnitCount = unknownValueUnitCount,
            SourceNotes = string.Join(" | ", sourceNotes.Distinct(StringComparer.Ordinal)) +
                (referenceOnly ? $" | {_catalog.MapVersion} 인게임 보드나 실제 전투 합계가 아닙니다. 강화·모드·대상별 효과는 별도 관측이 필요합니다." : ""),
            ProfileStatus = referenceOnly ? $"reference-only: unverified for {_catalog.MapVersion}; " + status : status,
            IsLegacyReferenceForSelectedMap = referenceOnly,
            ExplosionAmp = explosionAmp,
            AllDamageAmp = allDamageAmp,
            SingleMagicAmp = singleMagicAmp,
            SingleDamageWeight = singleDamageWeight,
            FinisherDamageWeight = finisherDamageWeight,
            UnobservedStackingArmorReduction = unobservedStackingArmor
        };
    }

    private bool TryGetProfileUnit(string unitId, UnitDefinition unit, out HandStatsUnit source)
    {
        if (_profile is not null)
        {
            // Preserve a recognized form's exact code before following catalog aliases.
            const string prefix = "rawcode:";
            if (unitId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                _profile.TryGet(unitId[prefix.Length..], out source!)) return true;
            // Direct source lookup first, then only stats aliases. Never match names.
            foreach (var rawcode in unit.Rawcodes)
                if (_profile.TryGet(rawcode, out source!)) return true;
            foreach (var rawcode in unit.Rawcodes)
                if (_profile.TryGet(RawcodeAliases.CanonicalForStats(rawcode), out source!)) return true;
        }
        source = null!;
        return false;
    }

    private static double Accumulate(HandStatsUnit unit, int count,
        Dictionary<(string AbilityName, string GroupId), double> nonStackingMaximums,
        ref double stun, ref double slow, ref double triggeredSlow, ref double armor,
        ref double triggeredArmor, ref double stackingArmor, ref double singleArmor, ref double attack,
        ref double triggeredAttack, ref double attackSpeed, ref double healthRegen, ref double manaRegen,
        ref double magicArmor, ref double magicAmp, ref double explosionAmp, ref double allDamageAmp,
        ref double singleMagicAmp, ref double singleDamageWeight, ref double finisherDamageWeight)
    {
        stun += Amount(unit, "스턴", count, nonStackingMaximums);
        slow += Amount(unit, "이동속도 감소", count, nonStackingMaximums);
        triggeredSlow += Amount(unit, "발동이동속도 감소", count, nonStackingMaximums);
        armor += Amount(unit, "방어력 감소", count, nonStackingMaximums);
        triggeredArmor += Amount(unit, "발동방어력 감소", count, nonStackingMaximums);
        var stacking = Amount(unit, "중첩방어력 감소", count, nonStackingMaximums);
        stackingArmor += stacking;
        singleArmor += Amount(unit, "단일방어력 감소", count, nonStackingMaximums);
        attack += Amount(unit, "공격력 증가", count, nonStackingMaximums);
        triggeredAttack += Amount(unit, "발동공격력 증가", count, nonStackingMaximums);
        attackSpeed += Amount(unit, "공격속도 증가", count, nonStackingMaximums);
        healthRegen += Amount(unit, "체력 재생", count, nonStackingMaximums);
        manaRegen += Amount(unit, "마나 재생", count, nonStackingMaximums);
        magicArmor += Amount(unit, "마법방어력 감소", count, nonStackingMaximums);
        magicAmp += Amount(unit, "마법데미지 증폭", count, nonStackingMaximums);
        explosionAmp += Amount(unit, "폭발형 데미지 증폭", count, nonStackingMaximums);
        allDamageAmp += Amount(unit, "모든피해증가", count, nonStackingMaximums);
        singleMagicAmp += Amount(unit, "단일마법 데미지 증가", count, nonStackingMaximums);
        singleDamageWeight += Amount(unit, "단일", count, nonStackingMaximums);
        finisherDamageWeight += Amount(unit, "끝딜", count, nonStackingMaximums);
        return stacking;
    }

    private static double Amount(HandStatsUnit unit, string name, int count,
        Dictionary<(string AbilityName, string GroupId), double> nonStackingMaximums)
    {
        if (!unit.TryGetNumber(name, out var value)) return 0;
        if (!unit.NonStackingGroups.TryGetValue(name, out var groupId)) return value * count;
        var key = (name, groupId);
        var prior = nonStackingMaximums.GetValueOrDefault(key);
        if (value <= prior) return 0;
        nonStackingMaximums[key] = value;
        return value - prior;
    }

    private static readonly HashSet<string> SupportedNumericAbilities =
    ["스턴", "이동속도 감소", "발동이동속도 감소", "방어력 감소", "발동방어력 감소",
     "중첩방어력 감소", "단일방어력 감소", "공격력 증가", "발동공격력 증가", "공격속도 증가",
     "체력 재생", "마나 재생", "마법방어력 감소", "마법데미지 증폭", "폭발형 데미지 증폭",
     "모든피해증가", "단일마법 데미지 증가", "단일", "끝딜"];

    private static readonly HashSet<string> ProviderAbilities =
    ["아머브레이크", "단일아머브레이크", "보스 잡기", "광폭화 잡기", "광폭화", "공중이동",
     "순간이동", "바제스", "단일", "끝딜"];

    private static readonly HashSet<string> SupportedSourceAbilities =
    [.. SupportedNumericAbilities, .. ProviderAbilities];

    private static void AddNotes(List<string> output, HandStatsUnit unit)
    {
        foreach (var note in unit.Notes.Concat(unit.Exclusions).Where(note => !string.IsNullOrWhiteSpace(note)))
            output.Add($"{unit.Name}: {note}");
        foreach (var ability in unit.Abilities.Where(IsExcludedFromTotal))
            output.Add($"{unit.Name} {ability.Key}: {AbilityText(ability.Value)} (합계 제외)");
    }

    private static bool IsUnknownSourceUnit(HandStatsUnit unit) => unit.Abilities.Count == 0 ||
        unit.Abilities.Any(IsExcludedFromTotal);

    private static bool IsExcludedFromTotal(KeyValuePair<string, HandStatsAbility> pair) =>
        pair.Value.Text is not null || !SupportedSourceAbilities.Contains(pair.Key) ||
        !ProviderAbilities.Contains(pair.Key) && SupportedNumericAbilities.Contains(pair.Key) && pair.Value.Number is null;

    private static string AbilityText(HandStatsAbility ability) => ability.Number?.ToString(CultureInfo.InvariantCulture) ??
        ability.Boolean?.ToString() ?? ability.Text ?? "미상";

    private static bool HasProvider(HandStatsUnit unit, params string[] names) => names.Any(name =>
        unit.HasTrue(name) || unit.TryGetNumber(name, out var amount) && amount > 0);

    private static double Value(UnitDefinition unit, params string[] names) => unit.OfficialAbilities
        .Where(ability => names.Contains(ability.Name, StringComparer.Ordinal))
        .Sum(ability => double.TryParse(ability.DisplayValue, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : 0);

    private static bool Has(UnitDefinition unit, params string[] names) => unit.OfficialAbilities.Any(ability =>
        names.Contains(ability.Name, StringComparer.Ordinal) &&
        !ability.DisplayValue.Equals("불가", StringComparison.OrdinalIgnoreCase));
}
