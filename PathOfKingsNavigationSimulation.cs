using System.Collections.Immutable;
using System.Numerics;

namespace OrandOverlay;

public sealed class PathOfKingsNavigationSimulation
{
    private readonly NavigationMechanicsProfile _profile;
    private readonly PathOfKingsCategoryOutcome _category;

    public PathOfKingsNavigationSimulation(NavigationMechanicsProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        var category = profile.CategoryBasics.Single(value => value.Id == "PathOfKings");
        if (category.TopUnitLimit != 1 || !category.Effects.SequenceEqual(
                ["line_movement_reduction:7/100", "boss_lumber:2_each", "registered_bosses:3"]) ||
            profile.PathBosses.Length != 3 ||
            profile.PathBosses.Any(value => value.LumberReward != 2))
            throw new InvalidDataException("Path mechanics are not source-pinned");
        _category = new PathOfKingsCategoryOutcome(new Rational(7, 100), 2, 0, 1);
    }

    public PathOfKingsSimulationResult SimulateMartial(PathOfKingsSimulationInput input)
    {
        ValidateInput(input);
        var category = Category(input, topLimit: 0);
        if (input.PlannedTopCount > 0)
            return Empty(PathOfKingsOption.MartialLaw,
                PathOfKingsDisposition.HardIneligible, category);
        return Result(PathOfKingsOption.MartialLaw, category,
            [FinalInput(PathOfKingsOption.MartialLaw, category)]);
    }

    public PathOfKingsSimulationResult SimulateBounty(PathOfKingsSimulationInput input)
    {
        ValidateInput(input);
        var category = Category(input, _category.TopUnitLimit);
        if (input.PlannedTopCount > category.TopUnitLimit)
            return Empty(PathOfKingsOption.BountyHunter,
                PathOfKingsDisposition.HardIneligible, category);
        if (input.FutureRegisteredKills > _profile.Limits.MaxFutureKills)
            return Empty(PathOfKingsOption.BountyHunter,
                PathOfKingsDisposition.ArithmeticLimitExceeded, category);
        if (input.TreasureMapActive)
            return Empty(PathOfKingsOption.BountyHunter,
                PathOfKingsDisposition.UnknownInput, category);

        var chestOutcomes = ChestOutcomes(input.ChestRouteDeltas);
        var bounty = PathOfKingsBountyDp.Run(_profile, input.FutureRegisteredKills,
            input.InitialDryStreak, chestOutcomes);
        var final = FinalInput(PathOfKingsOption.BountyHunter, category) with
        {
            ExecutedKills = input.FutureRegisteredKills,
            RouteDelta = bounty.ExpectedChestRouteDelta
        };
        return new PathOfKingsSimulationResult(PathOfKingsOption.BountyHunter,
            PathOfKingsDisposition.Eligible, category, bounty, [], [final]);
    }

    public PathOfKingsSimulationResult SimulateRoyal(PathOfKingsSimulationInput input)
    {
        ValidateInput(input);
        var category = Category(input, _category.TopUnitLimit);
        if (input.PlannedTopCount > category.TopUnitLimit)
            return Empty(PathOfKingsOption.RoyalLoader,
                PathOfKingsDisposition.HardIneligible, category);
        var pinned = _profile.Fixture("royal-piecewise-proc-branches")
            .SemanticInput.RoyalAttackScenarios;
        var requested = input.RoyalAttackScenarios.IsDefaultOrEmpty
            ? pinned
            : input.RoyalAttackScenarios;
        if (!requested.SequenceEqual(pinned) || requested.Length > _profile.Limits.MaxScenarios)
            return Empty(PathOfKingsOption.RoyalLoader,
                PathOfKingsDisposition.UnknownInput, category);

        var scenarios = requested.Select(value => Royal(value, category)).ToImmutableArray();
        return new PathOfKingsSimulationResult(PathOfKingsOption.RoyalLoader,
            PathOfKingsDisposition.Eligible, category, null, scenarios,
            scenarios.Select(value => value.FinalScoreInput).ToImmutableArray());
    }

    private PathRoyalScenarioResult Royal(NavigationRoyalAttackInput input,
        PathOfKingsCategoryOutcome category)
    {
        var royal = _profile.Royal;
        var transform = royal.AttackSpeedTransform;
        var baseCooldown = Fraction.From(input.BaseWeaponCooldown);
        var horizon = Fraction.From(input.EngagedHorizon);
        var scale = Fraction.From(transform.PointScale);
        var beforePoints = Math.Clamp(input.ExistingAttackSpeedPoints,
            transform.MinimumTotalAttackSpeedPoints, transform.MaximumTotalAttackSpeedPoints);
        var afterPoints = Math.Clamp(checked(input.ExistingAttackSpeedPoints +
            royal.AttackSpeedPoints), transform.MinimumTotalAttackSpeedPoints,
            transform.MaximumTotalAttackSpeedPoints);
        var cooldownBefore = baseCooldown / (Fraction.One + scale * beforePoints);
        var cooldownAfter = baseCooldown / (Fraction.One + scale * afterPoints);
        var attacksBefore = Fraction.Floor(horizon / cooldownBefore);
        var attacksAfter = Fraction.Floor(horizon / cooldownAfter);
        var threshold = Fraction.From(royal.CooldownThreshold);
        var slowMinimum = new Fraction(royal.BaseDamage, 1);
        if (baseCooldown > threshold)
            slowMinimum += Fraction.From(royal.SlowCooldownCoefficient) *
                (baseCooldown - threshold);
        var cooldownMinimum = baseCooldown * Fraction.From(royal.CooldownCoefficient);
        var productBranch = cooldownMinimum < slowMinimum;
        var procDamage = productBranch ? cooldownMinimum : slowMinimum;
        var procDelta = procDamage * Fraction.From(royal.ProcProbability) *
            attacksAfter * input.TargetsWithin400;
        var mechanics = new NavigationRoyalBranchExpected(
            productBranch ? NavigationRoyalPiecewiseBranch.CooldownProductMinimum :
                NavigationRoyalPiecewiseBranch.SlowCooldownMinimum,
            procDamage.AsProbability(), royal.ProcProbability, royal.ProcCooldownInput,
            baseCooldown.AsProbability(), cooldownBefore.AsProbability(),
            cooldownAfter.AsProbability(), input.EngagedHorizon, attacksBefore,
            attacksAfter, attacksAfter - attacksBefore, input.TargetsWithin400,
            procDelta.AsProbability(), royal.AttackDamage, royal.Radius);
        var final = FinalInput(PathOfKingsOption.RoyalLoader, category) with
        {
            TargetsWithin400 = input.TargetsWithin400,
            BaseWeaponCooldown = baseCooldown.AsRational(),
            AttackCountDelta = attacksAfter - attacksBefore,
            DirectAttackDamageDelta = new Rational(
                (BigInteger)attacksAfter * royal.AttackDamage, 1),
            ProcDamageDelta = procDelta.AsRational()
        };
        return new PathRoyalScenarioResult(mechanics, final);
    }

    private ImmutableArray<PathChestOutcome> ChestOutcomes(
        ImmutableArray<PathChestRouteDelta> deltas)
    {
        var supplied = deltas.IsDefault ? [] : deltas;
        if (supplied.Select(value => value.PoolMemberId).Distinct(StringComparer.Ordinal)
                .Count() != supplied.Length)
            throw new ArgumentException("Chest route delta IDs must be unique", nameof(deltas));
        var pool = _profile.Pool(_profile.Bounty.ChestLootPoolId);
        if (supplied.Any(value => pool.Members.All(member => member.Id != value.PoolMemberId)))
            throw new ArgumentException("Unknown chest route delta ID", nameof(deltas));
        return pool.Members.Select(member => new PathChestOutcome(member.Id,
            member.Probability.Value, supplied.FirstOrDefault(value =>
                value.PoolMemberId == member.Id)?.Delta ?? new SignedRouteDelta(0)))
            .ToImmutableArray();
    }

    private PathOfKingsCategoryOutcome Category(PathOfKingsSimulationInput input, int topLimit) =>
        _category with
        {
            TotalBossLumber = input.RemainingRegisteredBosses * 2,
            TopUnitLimit = topLimit
        };

    private static void ValidateInput(PathOfKingsSimulationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PlannedTopCount < 0 || input.RemainingRegisteredBosses is < 0 or > 3 ||
            input.FutureRegisteredKills < 0 || input.InitialDryStreak < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
    }

    private static PathOfKingsFinalScoreInput FinalInput(PathOfKingsOption option,
        PathOfKingsCategoryOutcome category) => new(option, category.LineMovementReduction,
        category.TotalBossLumber, 0, 0, new Rational(0, 1), 0,
        new Rational(0, 1), new Rational(0, 1), PathSignedRational.Zero);

    private static PathOfKingsSimulationResult Result(PathOfKingsOption option,
        PathOfKingsCategoryOutcome category,
        ImmutableArray<PathOfKingsFinalScoreInput> final) => new(option,
        PathOfKingsDisposition.Eligible, category, null, [], final);

    private static PathOfKingsSimulationResult Empty(PathOfKingsOption option,
        PathOfKingsDisposition disposition, PathOfKingsCategoryOutcome category) =>
        new(option, disposition, category, null, [], []);
}
