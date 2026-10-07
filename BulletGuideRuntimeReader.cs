using System.ComponentModel;

namespace OrandOverlay;

public sealed record BulletGuideRuntimeState(bool IsCurrent, int? ArmorTier, int? SpeedTier, int? AttackTier, string Detail)
{
    public BulletMapVersion MapVersion { get; init; } = BulletMapVersion.Unknown;
    public BulletUpgradeAbilityObservation? UpgradeAbilities { get; private init; }
    private BulletUpgradeObservationContext? UpgradeContext { get; init; }
    public BulletGuideRuntimeState WithUpgradeAbilities(BulletUpgradeAbilityObservation? observation,
        BulletUpgradeObservationContext context) => this with
    {
        UpgradeAbilities = IsCurrent && MapVersion == context.MapVersion && observation?.IsValid(context) == true &&
            MatchesTier(observation.AttackLevel, AttackTier) && MatchesTier(observation.SpeedLevel, SpeedTier) &&
            MatchesTier(observation.ArmorLevel, ArmorTier) ? observation : null,
        UpgradeContext = context
    };
    private static bool MatchesTier(int? level, int? tier) => level is null || tier is >= 1 and <= 3 && Math.Min(level.Value, 3) == tier;
    public BulletTraitState Trait(BulletUpgradeAbility ability) => IsCurrent && UpgradeContext is { } context &&
        MapVersion == context.MapVersion && UpgradeAbilities is { } observation &&
        MatchesTier(observation.AttackLevel, AttackTier) && MatchesTier(observation.SpeedLevel, SpeedTier) &&
        MatchesTier(observation.ArmorLevel, ArmorTier)
        ? observation.Trait(ability, context with { NowUtc = DateTimeOffset.UtcNow }) : BulletTraitState.Unknown;
    public double? ObservedObjectValue(BulletUpgradeAbility ability) => IsCurrent && UpgradeContext is { } context &&
        MapVersion == context.MapVersion && UpgradeAbilities is { } observation &&
        MatchesTier(observation.AttackLevel, AttackTier) && MatchesTier(observation.SpeedLevel, SpeedTier) &&
        MatchesTier(observation.ArmorLevel, ArmorTier)
        ? observation.ObjectValue(ability, context with { NowUtc = DateTimeOffset.UtcNow }) : null;
    public int? BaselineArmorReduction => IsCurrent ? ArmorTier switch { 1 => 5, 2 => 20, 3 => 40, _ => null } : null;
    public BulletUpgradeCounts? ExactCounts { get; init; }
    public BulletGuideRuntimeState WithExactCounts(BulletUpgradeCounts? counts)
    {
        if (counts is null) return this with { ExactCounts = null };
        static int Tier(int count) => count is >= 1 and < 15 ? 1 : count is >= 15 and < 30 ? 2 : count == 30 ? 3 : 0;
        return IsCurrent && Tier(counts.Armor) == ArmorTier && Tier(counts.Speed) == SpeedTier &&
            Tier(counts.Attack) == AttackTier
            ? this with { ExactCounts = counts }
            : Unknown with { Detail = "불릿 강화 정보가 바뀌고 있습니다. 다시 확인하겠습니다." };
    }
    public static BulletGuideRuntimeState Unknown { get; } = new(false, null, null, null,
        "내 불릿의 강화 상태를 아직 읽지 못했습니다.");
    public int? BulletArmorReduction => MapVersion is BulletMapVersion.V2320 or BulletMapVersion.V2322
        ? ObservedObjectValue(BulletUpgradeAbility.Armor) is { } value ? (int)-value : ArmorTier == 3 ? null : BaselineArmorReduction
        : BaselineArmorReduction;
    public int? BulletSlow => IsCurrent && ArmorTier is { } tier ? tier >= 2 ? 20 : 0 : null;
}

/// <summary>2.314 JASS ag/Bg/Rg tiers. Exact item charges are deliberately not inferred.</summary>
internal sealed class BulletGuideRuntimeReader
{
    private static readonly string[] Names = ["ag", "Bg", "Rg"];
    private readonly Func<ulong, int, byte[]>? _testRead;
    private readonly Func<IEnumerable<(ulong Base, byte[] Buffer)>>? _testChunks;
    private Dictionary<string, ulong>? _nodes;
    private DateTime _retryAfterUtc;
    private string _failure = BulletGuideRuntimeState.Unknown.Detail;

    internal BulletGuideRuntimeReader() { }
    internal BulletGuideRuntimeReader(Func<ulong, int, byte[]> read,
        Func<IEnumerable<(ulong Base, byte[] Buffer)>> chunks) => (_testRead, _testChunks) = (read, chunks);
    internal void Reset() { _nodes = null; _retryAfterUtc = DateTime.MinValue; }

    internal BulletGuideRuntimeState Read(ReadOnlyProcessMemory memory, string version, string mapHash,
        byte? owner, bool ownsBullet, ulong? anchor, CancellationToken token) => ReadCore(memory.ReadAvailable,
            () => memory.ReadChunks(memory.ReadablePrivateRegions(), 4 * 1024 * 1024, 64),
            version, mapHash, owner, ownsBullet, anchor, token);

    internal BulletGuideRuntimeState Read(string version, string mapHash, byte? owner, bool ownsBullet) =>
        ReadCore(_testRead ?? throw new InvalidOperationException("메모리 연결 없음"),
            _testChunks ?? throw new InvalidOperationException("메모리 영역 연결 없음"),
            version, mapHash, owner, ownsBullet, null, default);

    private BulletGuideRuntimeState ReadCore(Func<ulong, int, byte[]> read,
        Func<IEnumerable<(ulong Base, byte[] Buffer)>> chunks, string version, string mapHash,
        byte? owner, bool ownsBullet, ulong? anchor, CancellationToken token)
    {
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 || owner is null or > 3 || !ownsBullet)
            return BulletGuideRuntimeState.Unknown;
        if (_nodes is null && DateTime.UtcNow < _retryAfterUtc)
            return BulletGuideRuntimeState.Unknown with { Detail = _failure };
        try
        {
            var memory = new RouteQuestMemory(read);
            _nodes ??= anchor is { } start
                ? QuestGlobalDiscovery.FindRelated(memory, start, Names, token)
                : QuestGlobalDiscovery.Find(memory, chunks, token, Names);
            var first = Snapshot(memory, owner.Value);
            var second = Snapshot(memory, owner.Value);
            if (first != second) throw new InvalidDataException("불릿 강화 등급 변경 중");
            return second;
        }
        catch (Exception error) when (error is InvalidDataException or Win32Exception or OverflowException)
        {
            _nodes = null; _retryAfterUtc = DateTime.UtcNow.AddSeconds(10);
            _failure = "내 불릿의 강화 정보를 읽지 못했습니다. 잠시 후 다시 확인하겠습니다.";
            return BulletGuideRuntimeState.Unknown with { Detail = _failure };
        }
    }

    private BulletGuideRuntimeState Snapshot(RouteQuestMemory memory, byte owner)
    {
        int Tier(string name)
        {
            var values = memory.Array(_nodes![name], name, 9, 4);
            if (values is null || values.Length <= owner || values[owner] is < 1 or > 3)
                throw new InvalidDataException("로컬 강화 등급 배열 확인 실패: " + name);
            return values[owner];
        }
        return new(true, Tier("ag"), Tier("Bg"), Tier("Rg"),
            "불릿 강화 등급을 확인했습니다. 정확한 강화 횟수는 강화 아이템에서 따로 확인합니다.") { MapVersion = BulletMapVersion.V2314 };
    }
}
