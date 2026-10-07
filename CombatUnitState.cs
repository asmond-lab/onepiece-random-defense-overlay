namespace OrandOverlay;

public enum CombatUnitKind
{
    LocalUnit, Bullet, LaneMonster, LaneBoss, Story, SharedBoss, RecipeExemplar, WildcardExemplar
}

public sealed record CombatUnitState(int SampleId, string Rawcode, byte Owner, byte? LaneSlot,
    CombatUnitKind Kind, UnitPosition? Position, float? Life, float? MaximumLife,
    int? ArmorBreakStacks, bool Berserk, bool RokushikiActive)
{
    /// <summary>Stable selected ability only; never the currently executing effect.</summary>
    public BlackMariaMode? BlackMariaSelectedMode { get; init; }
    public BulletAbilityObservation? BulletAbilities { get; init; }
    public bool LegendMarked { get; init; }
    public bool MirrorCopy { get; init; }
    public HelperAbilityState? MirrorAbility { get; init; }
    public HelperAbilityState? UncommonSaleAbility { get; init; }
    public HelperAbilityState? AncientShipAbility { get; init; }
    public float? NativeArmor { get; init; }
    public ulong? EngineHandle { get; init; }
    public uint? CurrentOrderId { get; init; }
    public float? BaseAttackCooldown { get; init; }
}
