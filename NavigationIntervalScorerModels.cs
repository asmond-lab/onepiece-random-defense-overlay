using System.Collections.Immutable;
using System.Numerics;

namespace OrandOverlay;

public enum NavigationRiskPosture
{
    Balanced,
    HighCeiling,
    FloorDefense
}

public enum NavigationScoringRegime
{
    None,
    SecureCore,
    GuaranteedRecovery,
    DesperationRecovery
}

public enum NavigationRecommendationState
{
    Waiting,
    Provisional,
    Actionable,
    Locked,
    ManualOverride,
    SourceExpectedForced,
    NoSafeRecommendation
}

public enum NavigationScoringInputState
{
    Ready,
    Transient,
    Unknown
}

public enum NavigationScoreBlocker
{
    BeforeRound20,
    InputNotReady,
    ManualOverride,
    ArithmeticLimitExceeded,
    NoEligibleOptions,
    InsufficientConfidence,
    NonDominantIntervals
}

public readonly record struct NavigationSignedRational
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    public NavigationSignedRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominator));
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public static NavigationSignedRational Zero => new(0, 1);
    public override string ToString() => $"{Numerator}/{Denominator}";
}

public readonly record struct NavigationBasisPointInterval
{
    public int Lower { get; }
    public int Upper { get; }

    public NavigationBasisPointInterval(int lower, int upper)
    {
        if (lower is < 0 or > 10_000 || upper is < 0 or > 10_000 || lower > upper)
            throw new ArgumentOutOfRangeException(nameof(lower));
        Lower = lower;
        Upper = upper;
    }
}

public sealed record NavigationScoringOutcome(
    Rational Probability,
    int AfterBuildBp,
    int AfterCoreBp,
    int AfterCombatBp);

public sealed record NavigationCoupledScenario(
    string Id,
    ImmutableArray<NavigationScoringOutcome> Outcomes);

public sealed record NavigationIntervalOptionInput(
    string OptionId,
    int OrdinalId,
    bool TopCompatible,
    ImmutableArray<int> ConfidenceFactorsBp,
    ImmutableArray<string> MissingSignalIds,
    NavigationRiskPosture Posture,
    ImmutableArray<NavigationCoupledScenario> CoupledScenarios,
    ImmutableArray<string> SourceReasonIds = default);

public sealed record NavigationScenarioScore(
    string ScenarioId,
    int FloorBp,
    int ValueBp,
    int UpperBp,
    int TopBp,
    int CaptureConflictBp,
    int NavigationScoreBp,
    NavigationSignedRational ExpectedUplift,
    Rational Variance,
    int StandardDeviation,
    int Quantile95,
    Rational RecoveryProbability);

public sealed record NavigationIntervalOptionScore(
    string OptionId,
    int OrdinalId,
    int ConfidenceBp,
    NavigationRiskPosture Posture,
    int UncertaintyBp,
    NavigationBasisPointInterval Floor,
    NavigationBasisPointInterval Value,
    NavigationBasisPointInterval Upper,
    NavigationBasisPointInterval CaptureConflict,
    NavigationBasisPointInterval Score,
    ImmutableArray<NavigationScenarioScore> Scenarios,
    ImmutableArray<string> MissingSignalIds,
    ImmutableArray<string> SourceReasonIds = default);

public sealed record NavigationIntervalScoringRequest
{
    public bool EvaluateContinuously { get; init; }
    public int Round { get; init; }
    public int BeforeBuildBp { get; init; }
    public int BeforeCoreBp { get; init; }
    public int BeforeCombatBp { get; init; }
    public int RouteConfidenceBp { get; init; }
    public int MechanicsConfidenceBp { get; init; }
    public NavigationScoringInputState InputState { get; init; } =
        NavigationScoringInputState.Ready;
    public bool ManualNavigationOverride { get; init; }
    public string? ManualOverlayOptionId { get; init; }
    public string? CurrentOverlayRecommendationId { get; init; }
    public string? LockedOverlayRecommendationId { get; init; }
    public string ForcedRound24OptionId { get; init; } =
        "AlliedForces.DoubleBenefit";
    public int MaxCoupledScenarios { get; init; } = 4_096;
    public int MaxOutcomeStates { get; init; } = 65_536;
    public int MaxRationalBits { get; init; } = 262_144;
    public ImmutableArray<NavigationIntervalOptionInput> Options { get; init; } = [];
    public NavigationIntervalScoringResult? PreviousResult { get; init; }
}

public sealed record NavigationIntervalScoringResult(
    NavigationRecommendationState State,
    NavigationScoringRegime Regime,
    string? RecommendedOptionId,
    bool ShouldLockOverlayRecommendation,
    bool IsFrozen,
    ImmutableArray<NavigationIntervalOptionScore> Options,
    ImmutableArray<NavigationScoreBlocker> Blockers,
    ImmutableArray<string> MissingSignalIds)
{
    public bool IsRecommendationOnly => true;
    public bool ClaimsRuntimeSelection => false;
}
