using System.Collections.Immutable;
using System.Numerics;

namespace OrandOverlay;

public enum PathOfKingsOption { MartialLaw, BountyHunter, RoyalLoader }

public enum PathOfKingsDisposition
{
    Eligible,
    HardIneligible,
    UnknownInput,
    ArithmeticLimitExceeded
}

public readonly record struct PathSignedRational
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    public PathSignedRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public static PathSignedRational Zero => new(0, 1);
    public static PathSignedRational operator +(PathSignedRational left,
        PathSignedRational right) => new(
        left.Numerator * right.Denominator + right.Numerator * left.Denominator,
        left.Denominator * right.Denominator);
    public static PathSignedRational operator *(PathSignedRational left, Rational right) =>
        new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);
}

public sealed record PathChestRouteDelta(string PoolMemberId, SignedRouteDelta Delta);

public sealed record PathOfKingsSimulationInput(
    int PlannedTopCount,
    int RemainingRegisteredBosses,
    int FutureRegisteredKills,
    int InitialDryStreak,
    bool TreasureMapActive,
    ImmutableArray<PathChestRouteDelta> ChestRouteDeltas,
    ImmutableArray<NavigationRoyalAttackInput> RoyalAttackScenarios);

public sealed record PathOfKingsCategoryOutcome(
    Rational LineMovementReduction,
    int LumberPerBoss,
    int TotalBossLumber,
    int TopUnitLimit);

public sealed record PathChestOutcome(
    string PoolMemberId,
    Rational Probability,
    SignedRouteDelta RouteDelta);

public sealed record PathBountyTerminalState(
    int DryStreak,
    Rational Probability);

public sealed record PathBountyResult(
    int ExecutedKills,
    Rational ProbabilityMass,
    Rational ExpectedChestCount,
    ImmutableArray<PathBountyTerminalState> TerminalStates,
    ImmutableArray<PathChestOutcome> ChestOutcomes,
    PathSignedRational ExpectedChestRouteDelta);

public sealed record PathOfKingsFinalScoreInput(
    PathOfKingsOption Option,
    Rational LineMovementReduction,
    int BossLumber,
    int ExecutedKills,
    int TargetsWithin400,
    Rational BaseWeaponCooldown,
    int AttackCountDelta,
    Rational DirectAttackDamageDelta,
    Rational ProcDamageDelta,
    PathSignedRational RouteDelta);

public sealed record PathRoyalScenarioResult(
    NavigationRoyalBranchExpected Mechanics,
    PathOfKingsFinalScoreInput FinalScoreInput);

public sealed record PathOfKingsSimulationResult(
    PathOfKingsOption Option,
    PathOfKingsDisposition Disposition,
    PathOfKingsCategoryOutcome Category,
    PathBountyResult? Bounty,
    ImmutableArray<PathRoyalScenarioResult> RoyalScenarios,
    ImmutableArray<PathOfKingsFinalScoreInput> FinalScoreInputs);
