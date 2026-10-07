namespace OrandOverlay;

public enum BulletUpgradeObservationSource { Unknown, VerifiedCompleteAbilityList, OfflineFixture, Simulation }
public enum BulletTraitState { Unknown, Baseline, ConfirmedSelected }
public enum BulletUpgradeAbility { Attack, Speed, Armor }

/// <summary>The consumer's current identity and time, not values copied blindly from an observation.</summary>
public sealed record BulletUpgradeObservationContext(BulletMapVersion MapVersion, string MapScriptSha256,
    byte Owner, ulong EngineHandle, string SessionId, DateTimeOffset NowUtc, TimeSpan MaximumAge);

/// <summary>Native h081 (app 180h). No producer is enabled by this contract.
/// Null levels remain unknown; tier3/charges30 never imply trait selection.</summary>
public sealed record BulletUpgradeAbilityObservation(BulletMapVersion MapVersion, string MapScriptSha256,
    BulletUpgradeObservationSource Source, byte Owner, ulong EngineHandle, string Rawcode,
    string SessionId, DateTimeOffset ObservedAtUtc, bool Complete, int? AttackLevel, int? SpeedLevel, int? ArmorLevel)
{
    public bool IsValid(BulletUpgradeObservationContext context) =>
        MapVersion == context.MapVersion && BulletMechanics.ScriptHash(MapVersion) is { } hash &&
        string.Equals(hash, MapScriptSha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(hash, context.MapScriptSha256, StringComparison.OrdinalIgnoreCase) &&
        Source == BulletUpgradeObservationSource.VerifiedCompleteAbilityList && Complete &&
        Owner <= 3 && Owner == context.Owner && EngineHandle != 0 && EngineHandle == context.EngineHandle &&
        Rawcode == "h081" && !string.IsNullOrWhiteSpace(SessionId) && SessionId == context.SessionId &&
        context.MaximumAge > TimeSpan.Zero && context.NowUtc >= ObservedAtUtc &&
        context.NowUtc - ObservedAtUtc <= context.MaximumAge &&
        ValidLevel(AttackLevel) && ValidLevel(SpeedLevel) && ValidLevel(ArmorLevel) &&
        (MapVersion is not (BulletMapVersion.V2320 or BulletMapVersion.V2322) ||
            new[] { AttackLevel, SpeedLevel, ArmorLevel }.Count(level => level == 4) <= 1);

    private bool ValidLevel(int? level) => level is null || level >= 0 && level <= (MapVersion is BulletMapVersion.V2320 or BulletMapVersion.V2322 ? 4 : 3);
    public int? Level(BulletUpgradeAbility ability) => ability switch
    {
        BulletUpgradeAbility.Attack => AttackLevel,
        BulletUpgradeAbility.Speed => SpeedLevel,
        BulletUpgradeAbility.Armor => ArmorLevel,
        _ => null
    };
    public BulletTraitState Trait(BulletUpgradeAbility ability, BulletUpgradeObservationContext context) =>
        !IsValid(context) || MapVersion is not (BulletMapVersion.V2320 or BulletMapVersion.V2322) || Level(ability) is null or 0
            ? BulletTraitState.Unknown : Level(ability) == 4 ? BulletTraitState.ConfirmedSelected : BulletTraitState.Baseline;

    /// <summary>Executable object field, not measured target application. Tooltip48 is separate.</summary>
    public double? ObjectValue(BulletUpgradeAbility ability, BulletUpgradeObservationContext context) =>
        Trait(ability, context) == BulletTraitState.Unknown ? null : (ability, Level(ability)) switch
        {
            (BulletUpgradeAbility.Attack or BulletUpgradeAbility.Speed, 3) => .4,
            (BulletUpgradeAbility.Attack or BulletUpgradeAbility.Speed, 4) => .5,
            (BulletUpgradeAbility.Armor, 1) => -5,
            (BulletUpgradeAbility.Armor, 2) => -20,
            (BulletUpgradeAbility.Armor, 3) => -40,
            (BulletUpgradeAbility.Armor, 4) => -50,
            _ => null
        };
    public const int SelectedTraitTooltipClaim = 48;
}
