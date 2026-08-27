using System.Collections.Immutable;
using System.Numerics;

namespace OrandOverlay;

public static class GamblerNavigationSimulation
{
    private const string Casino = "Gambler.Casino";
    private const string Risk = "Gambler.RiskHedge";
    private const string Continuous = "Gambler.ContinuousBetting";

    public static GamblerSimulationResult Run(NavigationMechanicsProfile profile,
        GamblerSimulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        var option = profile.Options.SingleOrDefault(value => value.Id == request.OptionId);
        var binding = profile.Bindings.SingleOrDefault(value => value.OptionId == request.OptionId);
        if (option is null || binding is null || request.OptionId is not
                (Casino or Risk or Continuous))
            return Invalid([]);

        var context = new SimulationContext(profile.Limits);
        var actions = RequestedActions(request);
        if (!context.Check(NavigationLimitKind.GambleActions, actions))
            return context.Failure(option);
        if (!ValidateRoutePools(request, context))
            return context.ExceededLimit is null
                ? Invalid(Disabled(option))
                : context.Failure(option);

        var rerolls = request.OptionId == Continuous ? 0 :
            Integer(profile, request.OptionId == Casino
                ? "casino-sequential" : "risk-rising-hazard", "rareRerolls");
        var initial = request.InitialState.Apply(GamblerOutcomeDelta.Empty,
            rerollsAvailable: rerolls);
        var distribution = new Dictionary<GamblerPlannerOutcomeState, Rational>
        {
            [initial] = new Rational(1, 1)
        };

        if (request.OptionId == Casino)
        {
            var pool = profile.Pool(request.IsekaiMode ? "casino-isekai" : "casino-normal");
            if (!RunPinnedPool(profile, request, context, pool, request.Budget.High,
                    distribution, out distribution))
                return Failed(context, option);
        }
        else if (request.OptionId == Risk)
        {
            if (!RunPinnedPool(profile, request, context,
                    profile.Pool("risk-high-gamble"), request.Budget.High,
                    distribution, out distribution) ||
                !RunWorld(profile, request, context, request.Budget.World,
                    distribution, out distribution))
                return Failed(context, option);
        }
        else if (!RunPinnedPool(profile, request, context,
                     profile.Pool("continuous-middle-gamble"), request.Budget.Middle,
                     distribution, out distribution))
            return Failed(context, option);

        if (!context.TrySum(distribution.Values, out var mass))
            return context.Failure(option);
        if (mass != new Rational(1, 1)) return Invalid(Disabled(option));
        var outcomes = distribution.OrderBy(pair => pair.Key.CanonicalKey,
                StringComparer.Ordinal)
            .Select(pair => new GamblerWeightedPlannerState(pair.Key, pair.Value))
            .ToImmutableArray();
        return new(ArithmeticDisposition.Allowed, WaveDisposition.SafeRecommendation,
            null, 0, 0, actions, context.MaxBits, Disabled(option), outcomes, mass,
            [ReasonCode.Ready]);
    }

    public static Rational WorldSuccessProbability(NavigationMechanicsProfile profile,
        int worldFailureStreak)
    {
        if (worldFailureStreak < 0)
            throw new ArgumentOutOfRangeException(nameof(worldFailureStreak));
        var initial = Integer(profile, "risk-rising-hazard", "worldInitialPercent");
        var step = Integer(profile, "risk-rising-hazard", "worldFailureStepPercent");
        return new Rational(Math.Min(100, checked(initial + step * worldFailureStreak)), 100);
    }

    public static GamblerOutcomeDelta ContinuousMilestone(
        NavigationMechanicsProfile profile, int attempt)
    {
        if (attempt < 0) throw new ArgumentOutOfRangeException(nameof(attempt));
        var milestone = profile.ContinuousBetting.MilestonePrecedence
            .Where(value => attempt > 0 && attempt % value == 0)
            .Select(value => profile.ContinuousBetting.Milestones.Single(item =>
                item.Attempts == value))
            .FirstOrDefault();
        if (milestone is null) return GamblerOutcomeDelta.Empty;
        return new GamblerOutcomeDelta(milestone.Rewards.Select(ParseReward)
            .ToImmutableArray());
    }

    private static bool RunPinnedPool(NavigationMechanicsProfile profile,
        GamblerSimulationRequest request, SimulationContext context,
        NavigationPool pool, int count,
        Dictionary<GamblerPlannerOutcomeState, Rational> current,
        out Dictionary<GamblerPlannerOutcomeState, Rational> result)
    {
        result = current;
        if (!ValidatePool(pool.Members.Select(member => member.Probability.Value), context))
            return false;
        for (var action = 1; action <= count; action++)
        {
            var next = new Dictionary<GamblerPlannerOutcomeState, Rational>();
            foreach (var source in result.OrderBy(pair => pair.Key.CanonicalKey,
                         StringComparer.Ordinal))
            foreach (var member in pool.Members)
            {
                var routes = Routes(request, member.Id);
                foreach (var route in routes)
                {
                    if (!context.TryMultiply(source.Value, member.Probability.Value,
                            out var branch) ||
                        !context.TryMultiply(branch, route.Weight, out branch)) return false;
                    var state = ApplyBranch(profile, request.OptionId, source.Key,
                        member.Id, route);
                    if (!Merge(next, state, branch, context)) return false;
                }
            }
            result = next;
        }
        return true;
    }

    private static bool RunWorld(NavigationMechanicsProfile profile,
        GamblerSimulationRequest request, SimulationContext context, int count,
        Dictionary<GamblerPlannerOutcomeState, Rational> current,
        out Dictionary<GamblerPlannerOutcomeState, Rational> result)
    {
        result = current;
        for (var action = 0; action < count; action++)
        {
            var next = new Dictionary<GamblerPlannerOutcomeState, Rational>();
            foreach (var source in result.OrderBy(pair => pair.Key.CanonicalKey,
                         StringComparer.Ordinal))
            {
                var success = WorldSuccessProbability(profile, source.Key.WorldFailureStreak);
                var failure = new Rational(success.Denominator - success.Numerator,
                    success.Denominator);
                if (!RunWorldBranch(request, context, next, source, "world-success",
                        success, 0) ||
                    !RunWorldBranch(request, context, next, source, "world-failure",
                        failure, source.Key.WorldFailureStreak + 1)) return false;
            }
            result = next;
        }
        return true;
    }

    private static bool RunWorldBranch(GamblerSimulationRequest request,
        SimulationContext context,
        Dictionary<GamblerPlannerOutcomeState, Rational> next,
        KeyValuePair<GamblerPlannerOutcomeState, Rational> source,
        string id, Rational probability, int failureStreak)
    {
        if (probability.Numerator == 0) return true;
        var routes = Routes(request, id);
        foreach (var route in routes)
        {
            if (!context.TryMultiply(source.Value, probability, out var branch) ||
                !context.TryMultiply(branch, route.Weight, out branch)) return false;
            var delta = request.RoutePools.ContainsKey(id) ? route.Delta :
                new GamblerOutcomeDelta([new GamblerRewardQuantity(id, 1)]);
            var state = source.Key.Apply(delta, 1, failureStreak);
            if (!Merge(next, state, branch, context)) return false;
        }
        return true;
    }

    private static GamblerPlannerOutcomeState ApplyBranch(
        NavigationMechanicsProfile profile, string optionId,
        GamblerPlannerOutcomeState source, string memberId,
        GamblerWeightedRouteOutcome route)
    {
        var state = optionId == Continuous
            ? source.Apply(ContinuousMilestone(profile, source.GambleActions + 1))
            : source;
        var delta = route.Delta;
        if (optionId == Risk && memberId == "failure")
            delta = Combine(delta, new GamblerOutcomeDelta([
                new GamblerRewardQuantity("consolation_token",
                    Integer(profile, "risk-rising-hazard", "failureTokens")),
                new GamblerRewardQuantity("lumber",
                    Integer(profile, "risk-rising-hazard", "failureLumber"))]));
        return state.Apply(delta, 1);
    }

    private static ImmutableArray<GamblerWeightedRouteOutcome> Routes(
        GamblerSimulationRequest request, string memberId) =>
        request.RoutePools.TryGetValue(memberId, out var pool) ? pool.Outcomes :
        [new GamblerWeightedRouteOutcome(memberId, new Rational(1, 1),
            new GamblerOutcomeDelta([new GamblerRewardQuantity(memberId, 1)]))];

    private static bool ValidatePool(IEnumerable<Rational> weights,
        SimulationContext context)
    {
        var materialized = weights.ToArray();
        return materialized.All(weight => weight.Numerator > 0) &&
            context.TrySum(materialized, out var mass) && mass == new Rational(1, 1);
    }

    private static bool ValidateRoutePools(GamblerSimulationRequest request,
        SimulationContext context)
    {
        foreach (var pool in request.RoutePools.Values)
            if (!ValidatePool(pool.Outcomes.Select(outcome => outcome.Weight), context))
                return false;
        return true;
    }

    private static bool Merge(Dictionary<GamblerPlannerOutcomeState, Rational> target,
        GamblerPlannerOutcomeState state, Rational mass, SimulationContext context)
    {
        if (target.TryGetValue(state, out var existing))
        {
            if (!context.TryAdd(existing, mass, out var merged)) return false;
            target[state] = merged;
            return true;
        }
        if (!context.Check(NavigationLimitKind.AggregatedStates, target.Count + 1))
            return false;
        target.Add(state, mass);
        return context.Observe(mass);
    }

    private static int RequestedActions(GamblerSimulationRequest request) =>
        request.OptionId switch
        {
            Casino => request.Budget.High,
            Risk => checked(request.Budget.High + request.Budget.World),
            Continuous => request.Budget.Middle,
            _ => 0
        };

    private static int Integer(NavigationMechanicsProfile profile,
        string transitionId, string name) => profile.Transitions
        .Single(value => value.Id == transitionId).Integers
        .Single(value => value.Name == name).Value;

    private static GamblerRewardQuantity ParseReward(string text)
    {
        var parts = text.Split(':');
        return new GamblerRewardQuantity(parts[0], int.Parse(parts[1],
            System.Globalization.CultureInfo.InvariantCulture));
    }

    private static GamblerOutcomeDelta Combine(params GamblerOutcomeDelta[] deltas) =>
        new(deltas.SelectMany(delta => delta.Rewards).ToImmutableArray());

    private static ImmutableArray<GamblerActionTier> Disabled(
        NavigationMechanicsOption option) => option.DisabledActions.Select(value => value switch
        {
            "low_gamble" => GamblerActionTier.Low,
            "middle_gamble" => GamblerActionTier.Middle,
            "high_gamble" => GamblerActionTier.High,
            "rare_reroll" => GamblerActionTier.RareReroll,
            _ => throw new InvalidDataException("Unknown disabled Gambler action.")
        }).ToImmutableArray();

    private static GamblerSimulationResult Invalid(
        ImmutableArray<GamblerActionTier> disabled) =>
        new(ArithmeticDisposition.Allowed, WaveDisposition.NoSafeRecommendation,
            null, 0, 0, 0, 0, disabled, [], new Rational(0, 1),
            [ReasonCode.UnknownInput, ReasonCode.NoSafeRecommendation]);

    private static GamblerSimulationResult Failed(SimulationContext context,
        NavigationMechanicsOption option) => context.ExceededLimit is null
        ? Invalid(Disabled(option))
        : context.Failure(option);
}
