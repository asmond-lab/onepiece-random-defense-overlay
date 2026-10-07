namespace OrandOverlay;

/// <summary>2.322 Kaido movement evidence. Catalog aliases do not identify a current form.</summary>
public enum Map2322KaidoMovement { NotKaido, Conditional, EnhancedFormUnknown, FlyingDragon, GroundHybrid }

public static class Map2322KaidoAirRole
{
    public const string ConditionalNote = "카이도 공중이동은 용 형태에서만 확인됨 · 강화/현재 형태 미확인";

    // Object IDs: h07M base, h0AD dragon (fly/105), h0BW enhanced hybrid (no fly override).
    // App IDs reverse the four bytes. A0OG grants the hybrid transformation after the trait;
    // an enhancement flag alone does not prove which form is currently active.
    public static Map2322KaidoMovement Decide(string mapVersion, string unitId,
        IEnumerable<string> rawcodes, bool? traitEnhanced = null)
    {
        if (mapVersion is not ("2.322" or "2.323")) return Map2322KaidoMovement.NotKaido;
        var code = unitId.StartsWith("rawcode:", StringComparison.OrdinalIgnoreCase)
            ? unitId["rawcode:".Length..] : null;
        if (code == "DA0h") return Map2322KaidoMovement.FlyingDragon;
        if (code == "WB0h") return Map2322KaidoMovement.GroundHybrid;
        if (code == "M70h" || rawcodes.Contains("M70h", StringComparer.Ordinal))
            return traitEnhanced == true ? Map2322KaidoMovement.EnhancedFormUnknown
                : Map2322KaidoMovement.Conditional;
        return Map2322KaidoMovement.NotKaido;
    }

    public static bool CountsAsAir(string mapVersion, UnitDefinition unit, bool catalogAir,
        bool? traitEnhanced = null) => Decide(mapVersion, unit.Id, unit.Rawcodes, traitEnhanced) switch
    {
        Map2322KaidoMovement.FlyingDragon => true,
        Map2322KaidoMovement.GroundHybrid or Map2322KaidoMovement.Conditional or
            Map2322KaidoMovement.EnhancedFormUnknown => false,
        _ => catalogAir
    };
}
