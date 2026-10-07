namespace OrandOverlay;

public sealed record BulletGuideSupport(double ArmorPotential, double SlowPotential, int ArmorTarget,
    bool StunPairReady, string? RecommendedUnitId)
{
    public bool IsReady => ArmorPotential >= ArmorTarget && SlowPotential >= 82 && StunPairReady;
}

/// <summary>
/// 2.314 nominal aura capacity, NOT target-applied debuffs. Buff groups deduplicate copies/forms.
/// Source: .omo/evidence/bullet-map-analysis.md, support table; JASS SHA 0bccc479...bfc2028.
/// Conditional procs, unresolved variants and Bullet's own upgrades are never credited here.
/// </summary>
public sealed class BulletGuideSupportPolicy(DataCatalog catalog)
{
    private sealed record Aura(string Code, string Buff, int Armor = 0, int Slow = 0);
    private static readonly Aura[] Auras =
    [
        new("HA0h", "B00C", Armor: 5), new("HA0h", "B00H", Slow: 10),
        new("V20h", "B012", Slow: 50), new("M30h", "B02B", Armor: 20), new("M30h", "B02C", Slow: 25),
        new("H30h", "B017", Armor: 25), new("Z20h", "B01B", Armor: 12), new("N30h", "B02M", Armor: 25),
        new("F30h", "B00K", Armor: 20), new("K30h", "B025", Armor: 18), new("S30h", "B00N", Armor: 27),
        new("830h", "B016", Armor: 30), new("W20h", "B01D", Armor: 10), new("W20h", "B01F", Slow: 10),
        new("U30h", "B01U", Armor: 20), new("MC0h", "B02W", Armor: 22), new("540h", "B02R", Armor: 12),
        new("Q30h", "B01Z", Slow: 40), new("O30h", "B029", Armor: 11), new("Y30h", "B029", Armor: 11),
        new("K50h", "B005", Slow: 20), new("H20h", "B001", Slow: 15), new("A10h", "B001", Slow: 5),
        new("D20h", "B004", Slow: 15), new("Y00h", "B004", Slow: 5), new("F10h", "B012", Slow: 5),
        new("E10h", "B02G", Armor: 3), new("610h", "B011", Armor: 3)
    ];
    internal static readonly string[] CommonCodes = ["300h", "200h", "700h", "100h", "400h", "800h", "500h", "900h", "600h"];

    public BulletGuideSupport Evaluate(IReadOnlyDictionary<string, int> inventory,
        string? confirmedNavigation, GoroseiMode gorosei, Func<string, bool>? candidateAllowed = null,
        bool includeAuxiliaryAuras = true, Func<string, double>? learnedWeight = null)
    {
        var counts = inventory.Where(pair => pair.Value > 0).SelectMany(pair =>
                catalog.Unit(pair.Key).Rawcodes.Take(1).Select(code => (Code: code, pair.Value)))
            .GroupBy(item => item.Code).ToDictionary(group => group.Key, group => group.Sum(item => item.Value));
        // Support must survive the final recipe; Smoker50 and Shiki control cannot be double-spent.
        if (counts.GetValueOrDefault("180h") == 0)
            foreach (var code in BulletGuidePolicy.ComponentCodes)
                if (counts.GetValueOrDefault(code) > 0) counts[code]--;
        bool Has(string code) => counts.GetValueOrDefault(code) > 0;
        bool Auxiliary(string code) => TopGradePolicy.BaseTier(catalog.Unit("rawcode:" + code).Tier)
            is "희귀함" or "특별함";
        // Nominal display still counts owned auxiliary effects. Craft retention does not:
        // those small effects are leftovers optimization, not protected combat roles.
        var groups = Auras.Where(aura => Has(aura.Code) && (includeAuxiliaryAuras || !Auxiliary(aura.Code)))
            .GroupBy(aura => aura.Buff).ToArray();
        var armor = groups.Sum(group => group.Max(aura => aura.Armor));
        var slow = groups.Sum(group => group.Max(aura => aura.Slow)) +
            (confirmedNavigation == BulletGuidePolicy.NavigationId ? 7 : 0);
        var armorTarget = gorosei == GoroseiMode.Warcury ? 120 : 100;
        var stun = Has("Z20h") || Has("930h") ||
            (Has("IC0h") || Has("W20h")) && (Has("O30h") || Has("Y30h")) || Has("O30h") && Has("Y30h");
        string? target = armor < armorTarget
            ? Pick(["M30h", "H30h", "MC0h", "N30h", "K30h", "S30h", "Z20h", "O30h", "Y30h", "W20h", "E10h", "610h"])
            : slow < 82 ? Pick(["Q30h", "M30h", "K50h", "H20h", "D20h", "HA0h", "W20h"])
            : !stun ? Pick(Has("O30h") ? ["IC0h", "Y30h", "Z20h", "W20h"] :
                Has("Y30h") ? ["IC0h", "O30h", "Z20h", "W20h"] : ["O30h", "Y30h", "Z20h", "W20h"])
            : null;
        return new(armor, slow, armorTarget, stun, target);

        string? Pick(string[] codes)
        {
            var calculator = new RecipeCompletionCalculator(catalog.Unit);
            return codes.Where(code => !Has(code))
                .Where(code => !Auxiliary(code) || Has("180h") && stun)
                .Where(code => armor < armorTarget
                    ? Auras.Any(aura => aura.Code == code && aura.Armor >
                        Auras.Where(current => current.Buff == aura.Buff && Has(current.Code))
                            .Select(current => current.Armor).DefaultIfEmpty(0).Max())
                    : slow >= 82 || Auras.Any(aura => aura.Code == code && aura.Slow >
                        Auras.Where(current => current.Buff == aura.Buff && Has(current.Code))
                            .Select(current => current.Slow).DefaultIfEmpty(0).Max()))
                .Select((code, rank) => (Id: "rawcode:" + code, Rank: rank))
                .Where(item => candidateAllowed?.Invoke(item.Id) != false)
                .OrderBy(item => Auxiliary(item.Id[8..]))
                .ThenByDescending(item => calculator.Calculate([item.Id], inventory).CompletionRatio)
                .ThenByDescending(item => learnedWeight?.Invoke(item.Id) ?? 0)
                .ThenBy(item => item.Rank).Select(item => item.Id).FirstOrDefault();
        }
    }
}
