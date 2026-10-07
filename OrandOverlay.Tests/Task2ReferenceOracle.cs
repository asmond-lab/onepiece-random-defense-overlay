using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using OrandOverlay;

namespace OrandOverlay.Tests;

internal readonly record struct OracleRational : IComparable<OracleRational>
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    public OracleRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominator));
        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public static OracleRational Parse(NavigationProbability value) =>
        new(BigInteger.Parse(value.Numerator, CultureInfo.InvariantCulture),
            BigInteger.Parse(value.Denominator, CultureInfo.InvariantCulture));

    public static OracleRational operator +(OracleRational left, OracleRational right) =>
        new(left.Numerator * right.Denominator + right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);

    public static OracleRational operator -(OracleRational left, OracleRational right) =>
        new(left.Numerator * right.Denominator - right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);

    public static OracleRational operator *(OracleRational left, OracleRational right) =>
        new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);

    public static OracleRational operator /(OracleRational left, OracleRational right) =>
        new(left.Numerator * right.Denominator, left.Denominator * right.Numerator);

    public int CompareTo(OracleRational other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public override string ToString() => $"{Numerator}/{Denominator}";
}

internal sealed record OracleExecutionResult(
    NavigationVerificationInput RequestedInput,
    ArithmeticDisposition ArithmeticDisposition,
    WaveDisposition WaveDisposition,
    OracleRational Mass,
    OracleRational ExpectedValue,
    OracleRational Variance,
    int VarianceDenominatorBeforeReductionBits,
    int StandardDeviation,
    int Quantile95,
    int FloorBp,
    int ValueBp,
    int UpperBp,
    int TopBp,
    int CaptureConflictBp,
    int NavigationScore,
    int ExecutedActions,
    int ExecutedKills,
    int TerminalStateCount,
    int ObservedScenarioCount,
    long TransitionCount,
    int MaxObservedRationalBits,
    NavigationSemanticExpected SemanticExpected);

internal static class Task2ReferenceOracle
{
    private static readonly OracleRational Zero = new(0, 1);
    private static readonly OracleRational One = new(1, 1);
    private static readonly NavigationSemanticExpected EmptySemantics =
        new([], [], [], []);

    public static OracleExecutionResult Execute(
        NavigationMechanicsProfile profile,
        NavigationVerificationFixture fixture)
    {
        var input = fixture.Input;
        if (Exceeds(input.GambleActions, profile.Limits.MaxGambleActions) ||
            Exceeds(input.FutureKills, profile.Limits.MaxFutureKills) ||
            Exceeds(input.AggregatedStates, profile.Limits.MaxStates) ||
            Exceeds(input.CoupledScenarios, profile.Limits.MaxScenarios) ||
            Exceeds(input.RationalBits, profile.Limits.MaxRationalBits))
            return Failure(input);

        return fixture.Kind switch
        {
            NavigationFixtureKind.PoolMass => PoolMass(profile, input),
            NavigationFixtureKind.ArithmeticBoundary => Boundary(input, fixture.LimitKind),
            NavigationFixtureKind.BountyScore => Bounty(profile, input),
            NavigationFixtureKind.ContinuousSemantic => Continuous(profile, fixture),
            NavigationFixtureKind.RiskProgression => Risk(profile, fixture),
            NavigationFixtureKind.RoyalPiecewise => Royal(profile, fixture),
            _ => throw new InvalidDataException("unsupported verification fixture")
        };
    }

    private static OracleExecutionResult PoolMass(
        NavigationMechanicsProfile profile,
        NavigationVerificationInput input)
    {
        var mass = profile.Pool(input.PoolId).Members
            .Select(member => OracleRational.Parse(member.Probability))
            .Aggregate(Zero, (sum, value) => sum + value);
        return Success(input, mass);
    }

    private static OracleExecutionResult Boundary(
        NavigationVerificationInput input,
        NavigationLimitKind? limitKind)
    {
        if (limitKind is null)
            throw new InvalidDataException("boundary fixture has no limit kind");

        var executedActions = 0;
        var executedKills = 0;
        var terminalStates = 0;
        var observedScenarios = 0;
        var rationalBits = 0;
        switch (limitKind.Value)
        {
            case NavigationLimitKind.GambleActions:
                for (var action = 0; action < input.GambleActions; action++)
                    executedActions = checked(executedActions + 1);
                break;
            case NavigationLimitKind.FutureKills:
                for (var kill = 0; kill < input.FutureKills; kill++)
                    executedKills = checked(executedKills + 1);
                break;
            case NavigationLimitKind.AggregatedStates:
            {
                var states = new int[input.AggregatedStates];
                for (var state = 0; state < states.Length; state++)
                    states[state] = state;
                terminalStates = states.Distinct().Count();
                break;
            }
            case NavigationLimitKind.CoupledScenarios:
            {
                var scenarios = new int[input.CoupledScenarios];
                for (var scenario = 0; scenario < scenarios.Length; scenario++)
                    scenarios[scenario] = scenario;
                observedScenarios = scenarios.Distinct().Count();
                break;
            }
            case NavigationLimitKind.RationalBits:
                if (input.RationalBits > 0)
                {
                    var rational = new OracleRational(
                        BigInteger.One << (input.RationalBits - 1), BigInteger.One);
                    rationalBits = checked((int)rational.Numerator.GetBitLength());
                }
                break;
            default:
                throw new InvalidDataException("unsupported arithmetic limit kind");
        }

        return new(input, ArithmeticDisposition.Allowed,
            WaveDisposition.SafeRecommendation, One, Zero, Zero,
            0, 0, 0, 0, 0, 0, 0, 0, 0,
            executedActions, executedKills, terminalStates, observedScenarios,
            0, rationalBits, EmptySemantics);
    }

    private static OracleExecutionResult Continuous(
        NavigationMechanicsProfile profile,
        NavigationVerificationFixture fixture)
    {
        var formula = profile.Formulas.Single(value => value.Id == fixture.FormulaId);
        if (formula.Kind != NavigationFormulaKind.MilestoneModulo)
            throw new InvalidDataException("Continuous fixture formula mismatch");
        var vectors = fixture.SemanticInput.ContinuousAttempts.Select(attempt =>
        {
            var milestoneAttempt = profile.ContinuousBetting.MilestonePrecedence
                .First(value => attempt % value == 0);
            var milestone = profile.ContinuousBetting.Milestones.Single(value =>
                value.Attempts == milestoneAttempt);
            var rewards = milestone.Rewards.Select(ParseReward).ToImmutableArray();
            return new NavigationRewardVector(attempt, rewards);
        }).ToImmutableArray();
        return SemanticSuccess(profile, fixture,
            new NavigationSemanticExpected(vectors, [], [], []));
    }

    private static OracleExecutionResult Risk(
        NavigationMechanicsProfile profile,
        NavigationVerificationFixture fixture)
    {
        var transition = profile.Transitions.Single(value =>
            value.Id == fixture.TransitionId);
        var initial = transition.Integers.Single(value =>
            value.Name == "worldInitialPercent").Value;
        var step = transition.Integers.Single(value =>
            value.Name == "worldFailureStepPercent").Value;
        var input = fixture.SemanticInput;
        if (input.RiskInitialSuccessPercent != initial ||
            input.RiskFailureStepPercent != step)
            throw new InvalidDataException("Risk fixture transition mismatch");
        var raw = Enumerable.Range(0, input.RiskProgressionLength)
            .Select(index => checked(initial + step * index)).ToImmutableArray();
        var effective = raw.Select(value => value >= input.RiskRollMaximum
                ? input.RiskRollMaximum
                : value)
            .ToImmutableArray();
        return SemanticSuccess(profile, fixture,
            new NavigationSemanticExpected([], raw, effective, []));
    }

    private static OracleExecutionResult Royal(
        NavigationMechanicsProfile profile,
        NavigationVerificationFixture fixture)
    {
        var formula = profile.Formulas.Single(value => value.Id == fixture.FormulaId);
        if (formula.Kind != NavigationFormulaKind.RoyalPiecewiseProc)
            throw new InvalidDataException("Royal fixture formula mismatch");
        var royal = profile.Royal;
        var transform = royal.AttackSpeedTransform;
        var threshold = OracleRational.Parse(royal.CooldownThreshold);
        var slowCoefficient = OracleRational.Parse(royal.SlowCooldownCoefficient);
        var cooldownCoefficient = OracleRational.Parse(royal.CooldownCoefficient);
        var baseDamage = new OracleRational(royal.BaseDamage, 1);
        var procProbability = OracleRational.Parse(royal.ProcProbability);
        var pointScale = OracleRational.Parse(transform.PointScale);
        var objectBonus = OracleRational.Parse(transform.BonusObject.Value);
        if (pointScale * new OracleRational(royal.AttackSpeedPoints, 1) != objectBonus)
            throw new InvalidDataException("Royal object bonus and point scale disagree");

        var branches = fixture.SemanticInput.RoyalAttackScenarios.Select(input =>
        {
            if (input.MinimumAttackSpeedPoints !=
                    transform.MinimumTotalAttackSpeedPoints ||
                input.MaximumAttackSpeedPoints !=
                    transform.MaximumTotalAttackSpeedPoints ||
                input.AddedAttackSpeedPoints != royal.AttackSpeedPoints ||
                input.TargetsWithin400 < 0)
                throw new InvalidDataException("Royal attack-speed operands disagree");
            var baseWeaponCooldown = OracleRational.Parse(input.BaseWeaponCooldown);
            var horizon = OracleRational.Parse(input.EngagedHorizon);
            var beforePoints = ApplyAttackSpeedCap(input.ExistingAttackSpeedPoints,
                input.MinimumAttackSpeedPoints, input.MaximumAttackSpeedPoints);
            var afterPoints = ApplyAttackSpeedCap(checked(
                    input.ExistingAttackSpeedPoints + input.AddedAttackSpeedPoints),
                input.MinimumAttackSpeedPoints, input.MaximumAttackSpeedPoints);
            var beforeMultiplier = One + pointScale *
                new OracleRational(beforePoints, 1);
            var afterMultiplier = One + pointScale *
                new OracleRational(afterPoints, 1);
            if (beforeMultiplier.CompareTo(Zero) <= 0 ||
                afterMultiplier.CompareTo(Zero) <= 0)
                throw new InvalidDataException("Royal attack-speed multiplier is not positive");
            var cooldownBefore = baseWeaponCooldown / beforeMultiplier;
            var cooldownAfter = baseWeaponCooldown / afterMultiplier;
            var attacksBefore = Floor(horizon / cooldownBefore);
            var attacksAfter = Floor(horizon / cooldownAfter);

            var candidate = baseDamage;
            if (baseWeaponCooldown.CompareTo(threshold) > 0)
                candidate += slowCoefficient * (baseWeaponCooldown - threshold);
            var cooldownProduct = baseWeaponCooldown * cooldownCoefficient;
            var productMinimum = candidate.CompareTo(cooldownProduct) > 0;
            var damage = productMinimum ? cooldownProduct : candidate;
            var procDelta = new OracleRational(attacksAfter, 1) * procProbability *
                damage * new OracleRational(input.TargetsWithin400, 1);
            return new NavigationRoyalBranchExpected(
                productMinimum
                    ? NavigationRoyalPiecewiseBranch.CooldownProductMinimum
                    : NavigationRoyalPiecewiseBranch.SlowCooldownMinimum,
                AsProbability(damage), AsProbability(procProbability),
                royal.ProcCooldownInput, AsProbability(baseWeaponCooldown),
                AsProbability(cooldownBefore), AsProbability(cooldownAfter),
                AsProbability(horizon), attacksBefore, attacksAfter,
                checked(attacksAfter - attacksBefore), input.TargetsWithin400,
                AsProbability(procDelta), royal.AttackDamage, royal.Radius);
        }).ToImmutableArray();
        return SemanticSuccess(profile, fixture,
            new NavigationSemanticExpected([], [], [], branches));
    }

    private static int ApplyAttackSpeedCap(int value, int minimum, int maximum)
    {
        if (minimum >= maximum)
            throw new InvalidDataException("Royal attack-speed cap is invalid");
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }

    private static int Floor(OracleRational value)
    {
        if (value.CompareTo(Zero) < 0)
            throw new InvalidDataException("Royal attack count is negative");
        return checked((int)(value.Numerator / value.Denominator));
    }

    private static OracleExecutionResult SemanticSuccess(
        NavigationMechanicsProfile profile,
        NavigationVerificationFixture fixture,
        NavigationSemanticExpected semantics)
    {
        var mass = profile.Pool(fixture.Input.PoolId).Members
            .Select(member => OracleRational.Parse(member.Probability))
            .Aggregate(Zero, (sum, value) => sum + value);
        return new(fixture.Input, ArithmeticDisposition.Allowed,
            WaveDisposition.SafeRecommendation, mass, Zero, Zero,
            0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, fixture.Input.CoupledScenarios, 0, 0, semantics);
    }

    private static NavigationRewardQuantity ParseReward(string value)
    {
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(value[(separator + 1)..],
                NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            throw new InvalidDataException("invalid Continuous reward token");
        return new(value[..separator], count);
    }

    private static NavigationProbability AsProbability(OracleRational value) =>
        new(value.Numerator.ToString(CultureInfo.InvariantCulture),
            value.Denominator.ToString(CultureInfo.InvariantCulture));

    private static OracleExecutionResult Bounty(
        NavigationMechanicsProfile profile,
        NavigationVerificationInput input)
    {
        var bounty = profile.Bounty;
        var formula = profile.Formulas.Single(value => value.Id == "bounty-pity-formula");
        RequireParameter(formula, "initialChanceNumerator", bounty.InitialChanceNumerator);
        RequireParameter(formula, "dryStreakIncrement", bounty.DryStreakIncrement);
        RequireParameter(formula, "rollDenominator", bounty.RollDenominator);
        RequireParameter(formula, "resetDryStreak", bounty.ResetDryStreak);
        RequireParameter(formula, "futureKillHorizon", bounty.FutureKillHorizon);

        var chestMass = profile.Pool(bounty.ChestLootPoolId).Members
            .Select(member => OracleRational.Parse(member.Probability))
            .Aggregate(Zero, (sum, value) => sum + value);
        if (chestMass != One || input.TreasureMapActive ||
            !bounty.BaselineChestPoolRequiresNoTreasureMap ||
            input.ValueKind != NavigationFixtureValueKind.NextChestChanceNumerator)
            throw new InvalidDataException("Bounty fixture is not source-derived");

        var firstCertainDryStreak = DivideCeiling(
            bounty.RollDenominator - bounty.InitialChanceNumerator,
            bounty.DryStreakIncrement);
        var weights = new BigInteger[input.InitialDryStreak + 1];
        weights[input.InitialDryStreak] = BigInteger.One;
        var commonDenominator = BigInteger.One;
        long transitionCount = 0;
        var maxObservedBits = 1;

        for (var kill = 0; kill < input.FutureKills; kill++)
        {
            var nextLength = Math.Min(weights.Length + 1, firstCertainDryStreak + 1);
            if (nextLength > profile.Limits.MaxStates)
                return Failure(input);
            var next = new BigInteger[nextLength];
            var successWeight = BigInteger.Zero;
            for (var dryStreak = 0; dryStreak < weights.Length; dryStreak++)
            {
                var weight = weights[dryStreak];
                if (weight.IsZero)
                    continue;
                var chanceNumerator = Math.Min(bounty.RollDenominator,
                    checked(bounty.InitialChanceNumerator +
                        bounty.DryStreakIncrement * dryStreak));
                successWeight += weight * chanceNumerator;
                if (chanceNumerator < bounty.RollDenominator)
                    next[dryStreak + 1] = weight *
                        (bounty.RollDenominator - chanceNumerator);
                transitionCount = checked(transitionCount + 1);
            }
            next[bounty.ResetDryStreak] = successWeight;
            weights = next;
            commonDenominator *= bounty.RollDenominator;
            maxObservedBits = Math.Max(maxObservedBits,
                checked((int)commonDenominator.GetBitLength()));
            if (maxObservedBits > profile.Limits.MaxRationalBits)
                return Failure(input);
        }

        var massNumerator = weights.Aggregate(BigInteger.Zero, (sum, weight) => sum + weight);
        var mass = new OracleRational(massNumerator, commonDenominator);
        if (mass != One)
            throw new InvalidDataException("Bounty Markov mass was not preserved");

        var firstMomentNumerator = BigInteger.Zero;
        var secondMomentNumerator = BigInteger.Zero;
        for (var dryStreak = 0; dryStreak < weights.Length; dryStreak++)
        {
            var value = Math.Min(bounty.RollDenominator,
                checked(bounty.InitialChanceNumerator +
                    bounty.DryStreakIncrement * dryStreak));
            firstMomentNumerator += weights[dryStreak] * value;
            secondMomentNumerator += weights[dryStreak] * value * value;
        }
        var expectedValue = new OracleRational(firstMomentNumerator, commonDenominator);
        maxObservedBits = MaximumBitLength(maxObservedBits,
            expectedValue.Numerator, expectedValue.Denominator);
        if (maxObservedBits > profile.Limits.MaxRationalBits)
            return Failure(input);

        // E[X^2] - E[X]^2 is deliberately formed over D^2 before reduction.
        var varianceNumerator =
            secondMomentNumerator * commonDenominator -
            firstMomentNumerator * firstMomentNumerator;
        var varianceDenominator = commonDenominator * commonDenominator;
        var varianceDenominatorBits = checked((int)varianceDenominator.GetBitLength());
        var variance = new OracleRational(varianceNumerator, varianceDenominator);
        maxObservedBits = MaximumBitLength(maxObservedBits,
            variance.Numerator, variance.Denominator);
        if (maxObservedBits > profile.Limits.MaxRationalBits)
            return Failure(input);
        var standardDeviation = IntegerSquareRootFloor(variance);
        var quantile95 = Quantile95(weights, commonDenominator, bounty);

        var floor = input.CoreFloorBp;
        var valueComponent = ClampBasisPoints(5000 + RoundAwayFromZero(
            expectedValue / new OracleRational(2, 1)));
        var upperComponent = ClampBasisPoints(standardDeviation + RoundAwayFromZero(
            new OracleRational(quantile95, 1) - expectedValue));
        var topComponent = input.TopCompatible ? 10_000 : 0;
        var captureDenominator = Math.Max(1, 10_000 - input.StateUtilityBeforeBp);
        var captureConflict = ClampBasisPoints(5000 + RoundAwayFromZero(
            new OracleRational(5000, 1) * expectedValue /
            new OracleRational(captureDenominator, 1)));
        var navigationScore = RoundAwayFromZero(new OracleRational(
            40 * floor + 25 * valueComponent + 15 * upperComponent +
            10 * topComponent + 10 * captureConflict, 100));

        return new(input, ArithmeticDisposition.Allowed,
            WaveDisposition.SafeRecommendation, mass, expectedValue, variance,
            varianceDenominatorBits, standardDeviation, quantile95, floor,
            valueComponent, upperComponent, topComponent, captureConflict,
            navigationScore, 0, input.FutureKills, weights.Length, 0,
            transitionCount, maxObservedBits, EmptySemantics);
    }

    private static void RequireParameter(
        NavigationFormula formula,
        string name,
        int expected)
    {
        if (formula.Integers.Single(parameter => parameter.Name == name).Value != expected)
            throw new InvalidDataException("Bounty formula and recurrence disagree");
    }

    private static int Quantile95(
        IReadOnlyList<BigInteger> weights,
        BigInteger denominator,
        NavigationBounty bounty)
    {
        var cumulative = BigInteger.Zero;
        for (var dryStreak = 0; dryStreak < weights.Count; dryStreak++)
        {
            cumulative += weights[dryStreak];
            if (cumulative * 20 >= denominator * 19)
                return Math.Min(bounty.RollDenominator,
                    checked(bounty.InitialChanceNumerator +
                        bounty.DryStreakIncrement * dryStreak));
        }
        throw new InvalidDataException("Bounty quantile mass is incomplete");
    }

    private static int IntegerSquareRootFloor(OracleRational value)
    {
        var low = 0;
        var high = 10_000;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var square = new BigInteger(middle) * middle;
            if (square * value.Denominator <= value.Numerator)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return high;
    }

    private static int RoundAwayFromZero(OracleRational value)
    {
        var absolute = BigInteger.Abs(value.Numerator);
        var quotient = BigInteger.DivRem(absolute, value.Denominator, out var remainder);
        if (remainder * 2 >= value.Denominator)
            quotient += BigInteger.One;
        if (value.Numerator.Sign < 0)
            quotient = -quotient;
        return checked((int)quotient);
    }

    private static int ClampBasisPoints(int value) =>
        value < 0 ? 0 : value > 10_000 ? 10_000 : value;

    private static int DivideCeiling(int numerator, int denominator) =>
        checked((numerator + denominator - 1) / denominator);

    private static int MaximumBitLength(int current, params BigInteger[] values) =>
        values.Aggregate(current, (maximum, value) => Math.Max(maximum,
            checked((int)BigInteger.Abs(value).GetBitLength())));

    private static bool Exceeds(int value, int limit) => value < 0 || value > limit;

    private static OracleExecutionResult Success(
        NavigationVerificationInput input,
        OracleRational mass) =>
        new(input, ArithmeticDisposition.Allowed, WaveDisposition.SafeRecommendation,
            mass, Zero, Zero, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, EmptySemantics);

    private static OracleExecutionResult Failure(NavigationVerificationInput input) =>
        new(input, ArithmeticDisposition.ArithmeticLimitExceeded,
            WaveDisposition.NoSafeRecommendation, Zero, Zero, Zero,
            0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, EmptySemantics);
}
