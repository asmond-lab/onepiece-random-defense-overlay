namespace OrandOverlay;

public enum BulletMapVersion { Unknown, V2314, V2320, V2322 }
public enum BulletFormula
{
    SpeedBaseUniqueAoe, SpeedLegendaryAoe, SpeedExtraTarget,
    AttackOrdinary, AttackSpecial, StrikeInitial, StrikeRepeatEach
}

/// <summary>Offline source expressions only. Not damage, DPS, activation or target eligibility.</summary>
public static class BulletMechanics
{
    public const int StrikeRepeatCount = 12;
    public const string Script2314 = "0BCCC47907A9505F38EFAF6BBF20228A728EABDFAEC3209CCA7DF2269BFC2028";
    public const string Script2320 = "6FDFC64BF8AD9463F5B5C8A351FFA7E1875D6CF9B51210129539375FA77A6C7C";
    public const string Script2322 = Map2322SourceContract.JassSha256;
    public static string? ScriptHash(BulletMapVersion version) => version switch
    {
        BulletMapVersion.V2314 => Script2314,
        BulletMapVersion.V2320 => Script2320,
        BulletMapVersion.V2322 => Script2322,
        _ => null
    };

    /// <summary>Null means unknown/invalid. Levels are ability levels, never charges.
    /// Old expressions preserve raw levels; new expressions use min(level,3).
    /// Current life is required only by life-dependent expressions. No lower clamp or rounding.</summary>
    public static double? Evaluate(BulletMapVersion version, BulletFormula formula,
        int? attackLevel, int? speedLevel, double? currentLife = null)
    {
        // 2.322 trait control flow is sourced; its damage expressions are not.
        if (version == BulletMapVersion.V2322 || ScriptHash(version) is null || !Enum.IsDefined(formula) ||
            attackLevel is < 0 || speedLevel is < 0 ||
            currentLife is { } life && (!double.IsFinite(life) || life < 0)) return null;
        var raw = formula is BulletFormula.AttackOrdinary or BulletFormula.AttackSpecial or BulletFormula.StrikeInitial
            ? attackLevel : speedLevel;
        if (raw is null) return null;
        if (formula is BulletFormula.AttackOrdinary or BulletFormula.AttackSpecial && currentLife is null) return null;
        var modern = version == BulletMapVersion.V2320;
        double level = modern ? Math.Min(raw.Value, 3) : raw.Value;
        var h = currentLife.GetValueOrDefault();
        var value = formula switch
        {
            BulletFormula.SpeedBaseUniqueAoe => 30000 + (modern ? 25000 : 2500) * level,
            BulletFormula.SpeedLegendaryAoe => 50000 + (modern ? 45000 : 4500) * level,
            BulletFormula.SpeedExtraTarget => (modern ? 100000 : 10000) * level,
            BulletFormula.AttackOrdinary => modern ? (250000 + .02 * h) * (1 + .1 * level) : (250000 + .02 * h) * level * .01,
            BulletFormula.AttackSpecial => modern ? 250000 * (1 + .2 * level) + .01 * h : 250000 * level * .02 + .01 * h,
            BulletFormula.StrikeInitial => 1500000 * (1 + (modern ? .5 : .05) * level),
            BulletFormula.StrikeRepeatEach => 100000 * (1 + (modern ? .1 : .01) * level),
            _ => double.NaN
        };
        return double.IsFinite(value) ? value : null;
    }

    public static double? Evaluate(BulletFormula formula, BulletUpgradeAbilityObservation? observation,
        BulletUpgradeObservationContext context, double? currentLife = null) =>
        observation?.IsValid(context) == true
            ? Evaluate(context.MapVersion, formula, observation.AttackLevel, observation.SpeedLevel, currentLife)
            : null;
}
