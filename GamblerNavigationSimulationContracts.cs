using System.Collections.Immutable;

namespace OrandOverlay;

public enum GamblerActionTier
{
    Low,
    Middle,
    High,
    World,
    RareReroll
}

public readonly record struct GamblerObservedActionBudget
{
    public int Low { get; }
    public int Middle { get; }
    public int High { get; }
    public int World { get; }

    public GamblerObservedActionBudget(int low, int middle, int high, int world)
    {
        if (low < 0 || middle < 0 || high < 0 || world < 0)
            throw new ArgumentOutOfRangeException(nameof(low));
        (Low, Middle, High, World) = (low, middle, high, world);
    }
}

public sealed record GamblerRewardQuantity
{
    public string Id { get; }
    public int Count { get; }

    public GamblerRewardQuantity(string id, int count)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A reward id is required.", nameof(id));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        (Id, Count) = (id, count);
    }
}

public sealed class GamblerOutcomeDelta
{
    public ImmutableArray<GamblerRewardQuantity> Rewards { get; }
    public static GamblerOutcomeDelta Empty { get; } = new([]);

    public GamblerOutcomeDelta(ImmutableArray<GamblerRewardQuantity> rewards)
    {
        Rewards = Canonicalize(rewards);
    }

    internal static ImmutableArray<GamblerRewardQuantity> Canonicalize(
        IEnumerable<GamblerRewardQuantity> rewards) => rewards
        .GroupBy(reward => reward.Id, StringComparer.Ordinal)
        .Select(group => new GamblerRewardQuantity(group.Key,
            checked(group.Sum(reward => reward.Count))))
        .OrderBy(reward => reward.Id, StringComparer.Ordinal)
        .ToImmutableArray();
}

public sealed class GamblerPlannerOutcomeState : IEquatable<GamblerPlannerOutcomeState>
{
    public ImmutableArray<GamblerRewardQuantity> Rewards { get; }
    public int GambleActions { get; }
    public int WorldFailureStreak { get; }
    public int RerollsAvailable { get; }
    public static GamblerPlannerOutcomeState Empty { get; } = new([], 0, 0, 0);

    public GamblerPlannerOutcomeState(ImmutableArray<GamblerRewardQuantity> rewards,
        int gambleActions, int worldFailureStreak, int rerollsAvailable)
    {
        if (gambleActions < 0 || worldFailureStreak < 0 || rerollsAvailable < 0)
            throw new ArgumentOutOfRangeException(nameof(gambleActions));
        Rewards = GamblerOutcomeDelta.Canonicalize(rewards);
        (GambleActions, WorldFailureStreak, RerollsAvailable) =
            (gambleActions, worldFailureStreak, rerollsAvailable);
    }

    public int Reward(string id) => Rewards.FirstOrDefault(reward =>
        reward.Id.Equals(id, StringComparison.Ordinal))?.Count ?? 0;

    internal GamblerPlannerOutcomeState Apply(GamblerOutcomeDelta delta,
        int actionIncrement = 0, int? worldFailureStreak = null,
        int? rerollsAvailable = null) => new(Rewards.Concat(delta.Rewards).ToImmutableArray(),
            checked(GambleActions + actionIncrement),
            worldFailureStreak ?? WorldFailureStreak,
            rerollsAvailable ?? RerollsAvailable);

    public string CanonicalKey => string.Join('|', Rewards.Select(reward =>
        $"{reward.Id}:{reward.Count}")) +
        $"#{GambleActions}:{WorldFailureStreak}:{RerollsAvailable}";

    public bool Equals(GamblerPlannerOutcomeState? other) => other is not null &&
        GambleActions == other.GambleActions &&
        WorldFailureStreak == other.WorldFailureStreak &&
        RerollsAvailable == other.RerollsAvailable &&
        Rewards.SequenceEqual(other.Rewards);

    public override bool Equals(object? obj) => Equals(obj as GamblerPlannerOutcomeState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GambleActions); hash.Add(WorldFailureStreak); hash.Add(RerollsAvailable);
        foreach (var reward in Rewards) hash.Add(reward);
        return hash.ToHashCode();
    }
}

public sealed record GamblerWeightedRouteOutcome(
    string Id, Rational Weight, GamblerOutcomeDelta Delta);

public sealed record GamblerRoutePool
{
    public ImmutableArray<GamblerWeightedRouteOutcome> Outcomes { get; }

    public GamblerRoutePool(ImmutableArray<GamblerWeightedRouteOutcome> outcomes)
    {
        if (outcomes.IsDefaultOrEmpty || outcomes.Any(outcome => outcome is null ||
                string.IsNullOrWhiteSpace(outcome.Id) || outcome.Delta is null))
            throw new ArgumentException("A route pool requires typed outcomes.", nameof(outcomes));
        Outcomes = outcomes;
    }
}

public sealed record GamblerSimulationRequest
{
    public string OptionId { get; }
    public GamblerObservedActionBudget Budget { get; }
    public bool IsekaiMode { get; }
    public GamblerPlannerOutcomeState InitialState { get; }
    public ImmutableDictionary<string, GamblerRoutePool> RoutePools { get; }

    public GamblerSimulationRequest(string optionId, GamblerObservedActionBudget budget,
        bool isekaiMode, GamblerPlannerOutcomeState initialState,
        ImmutableDictionary<string, GamblerRoutePool> routePools)
    {
        if (string.IsNullOrWhiteSpace(optionId))
            throw new ArgumentException("An option id is required.", nameof(optionId));
        OptionId = optionId; Budget = budget; IsekaiMode = isekaiMode;
        InitialState = initialState ?? throw new ArgumentNullException(nameof(initialState));
        RoutePools = routePools ?? throw new ArgumentNullException(nameof(routePools));
    }
}

public sealed record GamblerWeightedPlannerState(
    GamblerPlannerOutcomeState State, Rational Mass);

public sealed record GamblerSimulationResult(
    ArithmeticDisposition ArithmeticDisposition,
    WaveDisposition WaveDisposition,
    NavigationLimitKind? ExceededLimit,
    int Requested,
    int Limit,
    int ExecutedActions,
    int MaxObservedRationalBits,
    ImmutableArray<GamblerActionTier> DisabledTiers,
    ImmutableArray<GamblerWeightedPlannerState> Outcomes,
    Rational TotalMass,
    ImmutableArray<ReasonCode> Reasons);
