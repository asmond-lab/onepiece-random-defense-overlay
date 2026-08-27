using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

public readonly record struct Rational : IEquatable<Rational>
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0 || numerator < 0)
            throw new ArgumentOutOfRangeException();
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public static Rational Parse(string numerator, string denominator) =>
        new(BigInteger.Parse(numerator, CultureInfo.InvariantCulture),
            BigInteger.Parse(denominator, CultureInfo.InvariantCulture));

    public static Rational operator +(Rational left, Rational right) =>
        new(left.Numerator * right.Denominator + right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);

    public static Rational operator *(Rational left, Rational right) =>
        new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);

    public static Rational operator /(Rational left, Rational right) =>
        new(left.Numerator * right.Denominator, left.Denominator * right.Numerator);

    public override string ToString() => $"{Numerator}/{Denominator}";
}

public enum NavigationSourceRole
{
    AbilityObjectData,
    UnitObjectData,
    TriggerStrings,
    JassMechanics,
    GameplayConstants
}

public enum NavigationExtractionMode
{
    InMemoryReadOnlyMpqMember
}

public enum NavigationSourcePurpose
{
    IntegerRandomRange,
    ContinuousBetting,
    RandomFlavorDispatch,
    GamblerSelection,
    BestHelpSelection,
    PathSelection,
    AlliedSelection,
    NavigationActionWindow,
    BountyPityRecurrence,
    HighGamble,
    TraitEngineering,
    EarthquakeEffect,
    SeastoneEffect,
    BusterCallEffect,
    ManaPotionEffect,
    PoisonEffect,
    AlliedCraftReward,
    NavigationDefinitions,
    BountyChestLoot,
    AlchemyWrapper,
    RareReroll,
    WorldGamble,
    RoyalProc,
    AlchemyDirectChildren,
    PathTopRestriction,
    PathBossReward,
    RoyalAttackSpeedApplication,
    WarcraftAttackSpeedConstants
}

public sealed record NavigationSourceMember(
    string Name,
    NavigationSourceRole Role,
    long Length,
    string Sha256,
    NavigationExtractionMode ExtractionMode);

public sealed record NavigationSourceRange(
    string Id,
    string Artifact,
    string Function,
    int StartLine,
    int EndLine,
    int EvidenceLine,
    NavigationSourcePurpose Purpose);

public enum NavigationPathTopBranchKind
{
    MartialZeroTop,
    NonMartialOneTop
}

public sealed record NavigationSourceBranch(
    string Id,
    string SourceRangeId,
    NavigationPathTopBranchKind Kind,
    int StartLine,
    int EndLine,
    int EvidenceLine,
    ImmutableArray<string> OwnerOptionIds);

public sealed record NavigationSource(
    string MapVersion,
    string JassSha256,
    long JassLengthBytes,
    int JassLineCount,
    string SourceArtifact,
    ImmutableArray<NavigationSourceMember> Members,
    ImmutableArray<NavigationSourceRange> Ranges,
    ImmutableArray<NavigationSourceBranch> Branches);

public enum WaveDisposition
{
    SafeRecommendation,
    NoSafeRecommendation
}

public sealed record WaveRecommendationResult(
    WaveDisposition Disposition,
    ImmutableArray<NavigationUnknownReason> Reasons);

public sealed record NavigationCategoryMechanics(
    string Id,
    int TopUnitLimit,
    ImmutableArray<string> Effects);

public sealed record NavigationProbability(string Numerator, string Denominator)
{
    public Rational Value => Rational.Parse(Numerator, Denominator);
}

public sealed record NavigationMechanicsOption(
    string Id,
    string CategoryId,
    string Name,
    int TopUnitLimit,
    ImmutableArray<string> Effects,
    ImmutableArray<string> Actions,
    ImmutableArray<string> DisabledActions,
    ImmutableArray<string> SourceRangeIds);

public sealed record NavigationActionWindow(
    int PreviewRound,
    ImmutableArray<int> ActionableRounds,
    int ForcedRound);

public sealed record NavigationBounty(
    int InitialChanceNumerator,
    int DryStreakIncrement,
    int RollDenominator,
    int ResetDryStreak,
    int FutureKillHorizon,
    string ChestLootPoolId,
    bool BaselineChestPoolRequiresNoTreasureMap,
    ImmutableArray<int> PityNumeratorCheckpoints,
    ImmutableArray<string> SourceRangeIds);

public enum NavigationAttackSpeedFormulaKind
{
    WarcraftCappedAdditive
}

public enum NavigationAttackSpeedCapAuthority
{
    WarcraftDefaultNoMapOverride
}

public sealed record NavigationObjectDataFieldPin(
    string MemberName,
    string BaseRawcode,
    string ObjectRawcode,
    string FieldId,
    int Level,
    int DataPointer,
    int RecordStartOffset,
    int RecordEndOffset,
    int ValueOffset,
    NavigationProbability Value,
    string RecordSha256);

public sealed record NavigationAttackSpeedTransform(
    NavigationAttackSpeedFormulaKind Kind,
    NavigationAttackSpeedCapAuthority CapAuthority,
    int MinimumTotalAttackSpeedPoints,
    int MaximumTotalAttackSpeedPoints,
    NavigationProbability PointScale,
    NavigationObjectDataFieldPin BonusObject,
    ImmutableArray<string> SourceRangeIds);

public sealed record NavigationRoyal(
    int AttackSpeedPoints,
    int AttackDamage,
    NavigationProbability ProcProbability,
    int Radius,
    int BaseDamage,
    NavigationProbability CooldownThreshold,
    NavigationProbability SlowCooldownCoefficient,
    NavigationProbability CooldownCoefficient,
    string ProcCooldownInput,
    NavigationAttackSpeedTransform AttackSpeedTransform,
    NavigationUnknownField TargetCount,
    ImmutableArray<string> SourceRangeIds);

public sealed record NavigationAlchemy(
    int ManaCost,
    string DecompositionKind,
    ImmutableArray<string> AllowedTiers,
    bool RequiredRootProtection,
    ImmutableArray<string> SourceRangeIds);

public sealed record NavigationMilestone(
    int Attempts,
    ImmutableArray<string> Rewards,
    ImmutableArray<string> SourceRangeIds);

public sealed record NavigationContinuous(
    ImmutableArray<string> OutcomeOrder,
    ImmutableArray<int> MilestonePrecedence,
    ImmutableArray<NavigationMilestone> Milestones,
    ImmutableArray<string> SourceRangeIds);

public sealed record NavigationPathBoss(
    int Ordinal,
    int LumberReward,
    string RuntimeSourceRangeId,
    ImmutableArray<string> PresentationSourceRangeIds);

public sealed record NavigationLevelValue(int Level, string Value);

public sealed record NavigationHelperSpell(
    string Id,
    NavigationKnowledgeState State,
    int? ManaCost,
    ImmutableArray<NavigationLevelValue> ManaCosts,
    ImmutableArray<NavigationLevelValue> Cooldowns,
    ImmutableArray<NavigationLevelValue> Levels,
    ImmutableArray<string> SourceRangeIds);

public enum NavigationKnowledgeState
{
    Known,
    Unknown
}

public enum NavigationUnknownReason
{
    NoStaticWaveRoster,
    DynamicGroupEnumeration,
    NoStaticControlDeficit,
    NoStaticWaveDuration
}

public sealed record NavigationUnknownField(
    NavigationKnowledgeState State,
    NavigationUnknownReason Reason,
    ImmutableArray<string> SourceRangeIds);

public sealed record NavigationWaveCombat(
    NavigationUnknownField RemainingEffectiveHp,
    NavigationUnknownField TargetCounts,
    NavigationUnknownField ControlDeficits,
    NavigationUnknownField ActiveDuration);

public enum NavigationPoolKind
{
    OutcomeDistribution,
    DeterministicBundle,
    UniformDispatch
}

public sealed record NavigationPoolMember(
    string Id,
    NavigationProbability Probability,
    ImmutableArray<string> SourceRangeIds);

public sealed record NavigationPool(
    string Id,
    string OptionId,
    NavigationPoolKind Kind,
    ImmutableArray<NavigationPoolMember> Members,
    ImmutableArray<string> SourceRangeIds);

public enum NavigationTransitionKind
{
    QualifyingCraftReward,
    WildcardGrant,
    ResourceConversion,
    TopRestriction,
    DryStreakMarkov,
    RoyalEnhancement,
    SequentialGamble,
    RisingHazardGamble,
    ExclusiveMilestoneLadder,
    HelperLevelChange,
    DirectUnitDecomposition,
    ReverseGrantAndRestriction,
    UniformRandomDispatch,
    ForcedRoundSelection
}

public sealed record NavigationIntegerParameter(string Name, int Value);
public sealed record NavigationProbabilityParameter(string Name, NavigationProbability Value);

public sealed record NavigationTransition(
    string Id,
    string OptionId,
    NavigationTransitionKind Kind,
    ImmutableArray<NavigationIntegerParameter> Integers,
    ImmutableArray<NavigationProbabilityParameter> Probabilities,
    ImmutableArray<string> SourceRangeIds);

public enum NavigationFormulaKind
{
    DeterministicGrant,
    ResourceExchange,
    RestrictedMultiplier,
    BountyPity,
    RoyalPiecewiseProc,
    SequentialOutcome,
    MilestoneModulo,
    HelperObjectEffects,
    DirectRecipeChildren,
    RestrictedImmediateGrant,
    UniformTwelveWayDispatch
}

public sealed record NavigationFormula(
    string Id,
    string OptionId,
    NavigationFormulaKind Kind,
    ImmutableArray<NavigationIntegerParameter> Integers,
    ImmutableArray<NavigationProbabilityParameter> Probabilities,
    ImmutableArray<string> SourceRangeIds);

public enum ArithmeticDisposition
{
    Allowed,
    ArithmeticLimitExceeded
}

public enum NavigationLimitKind
{
    GambleActions,
    FutureKills,
    AggregatedStates,
    CoupledScenarios,
    RationalBits
}

public sealed record ArithmeticCheck(
    ArithmeticDisposition ArithmeticDisposition,
    WaveDisposition WaveDisposition,
    NavigationLimitKind Kind,
    int Requested,
    int Limit);

public sealed record NavigationLimits(
    int MaxGambleActions,
    int MaxFutureKills,
    int MaxStates,
    int MaxScenarios,
    int MaxRationalBits)
{
    public ArithmeticCheck CheckActions(int value) =>
        Check(NavigationLimitKind.GambleActions, value, MaxGambleActions);

    public ArithmeticCheck CheckKills(int value) =>
        Check(NavigationLimitKind.FutureKills, value, MaxFutureKills);

    public ArithmeticCheck CheckStates(int value) =>
        Check(NavigationLimitKind.AggregatedStates, value, MaxStates);

    public ArithmeticCheck CheckScenarios(int value) =>
        Check(NavigationLimitKind.CoupledScenarios, value, MaxScenarios);

    public ArithmeticCheck CheckRationalBits(int value) =>
        Check(NavigationLimitKind.RationalBits, value, MaxRationalBits);

    private static ArithmeticCheck Check(
        NavigationLimitKind kind,
        int value,
        int limit) =>
        value >= 0 && value <= limit
            ? new(ArithmeticDisposition.Allowed, WaveDisposition.SafeRecommendation,
                kind, value, limit)
            : new(ArithmeticDisposition.ArithmeticLimitExceeded,
                WaveDisposition.NoSafeRecommendation, kind, value, limit);
}

public enum NavigationFixtureKind
{
    PoolMass,
    BountyScore,
    ArithmeticBoundary,
    ContinuousSemantic,
    RiskProgression,
    RoyalPiecewise
}

public enum NavigationFixtureValueKind
{
    None,
    NextChestChanceNumerator
}

public sealed record NavigationVerificationInput(
    int GambleActions,
    int FutureKills,
    int AggregatedStates,
    int CoupledScenarios,
    int RationalBits,
    int InitialDryStreak,
    int CoreFloorBp,
    int StateUtilityBeforeBp,
    bool TopCompatible,
    bool TreasureMapActive,
    string PoolId,
    NavigationFixtureValueKind ValueKind);

public sealed record NavigationVerificationExpected(
    ArithmeticDisposition ArithmeticDisposition,
    WaveDisposition WaveDisposition,
    NavigationProbability Mass,
    NavigationProbability ExpectedValue,
    NavigationProbability Variance,
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
    int MaxObservedRationalBits);

public sealed record NavigationRewardQuantity(string Id, int Count);

public sealed record NavigationRewardVector(
    int Attempt,
    ImmutableArray<NavigationRewardQuantity> Rewards);

public enum NavigationRoyalPiecewiseBranch
{
    CooldownProductMinimum,
    SlowCooldownMinimum
}

public sealed record NavigationRoyalAttackInput(
    NavigationProbability BaseWeaponCooldown,
    int ExistingAttackSpeedPoints,
    int MinimumAttackSpeedPoints,
    int MaximumAttackSpeedPoints,
    int AddedAttackSpeedPoints,
    NavigationProbability EngagedHorizon,
    int TargetsWithin400);

public sealed record NavigationRoyalBranchExpected(
    NavigationRoyalPiecewiseBranch Branch,
    NavigationProbability ProcDamage,
    NavigationProbability ProcProbability,
    string ProcCooldownInput,
    NavigationProbability ProcBaseWeaponCooldown,
    NavigationProbability CooldownBefore,
    NavigationProbability CooldownAfter,
    NavigationProbability EngagedHorizon,
    int AttacksBefore,
    int AttacksAfter,
    int AttackCountDelta,
    int TargetsWithin400,
    NavigationProbability ProcDelta,
    int AttackDamage,
    int Radius);

public sealed record NavigationSemanticInput(
    ImmutableArray<int> ContinuousAttempts,
    int RiskInitialSuccessPercent,
    int RiskFailureStepPercent,
    int RiskRollMaximum,
    int RiskProgressionLength,
    ImmutableArray<NavigationRoyalAttackInput> RoyalAttackScenarios);

public sealed record NavigationSemanticExpected(
    ImmutableArray<NavigationRewardVector> ContinuousRewards,
    ImmutableArray<int> RiskRawThresholds,
    ImmutableArray<int> RiskEffectiveSuccessPercents,
    ImmutableArray<NavigationRoyalBranchExpected> RoyalBranches);

public sealed record NavigationVerificationFixture(
    string Id,
    string OptionId,
    NavigationFixtureKind Kind,
    NavigationLimitKind? LimitKind,
    string TransitionId,
    string FormulaId,
    NavigationVerificationInput Input,
    NavigationVerificationExpected Expected,
    NavigationSemanticInput SemanticInput,
    NavigationSemanticExpected SemanticExpected,
    ImmutableArray<string> SourceRangeIds);

public sealed record NavigationMechanicsBinding(
    string OptionId,
    ImmutableArray<string> PoolIds,
    ImmutableArray<string> TransitionIds,
    ImmutableArray<string> FormulaIds,
    ImmutableArray<string> FixtureIds,
    ImmutableArray<string> SourceRangeIds);

public sealed record NavigationMechanicsProfile(
    NavigationSource Source,
    ImmutableArray<NavigationCategoryMechanics> CategoryBasics,
    NavigationActionWindow ActionWindow,
    string ForcedRound24OptionId,
    NavigationProbability RandomSelectionProbability,
    NavigationLimits Limits,
    NavigationBounty Bounty,
    NavigationRoyal Royal,
    NavigationAlchemy Alchemy,
    NavigationContinuous ContinuousBetting,
    ImmutableArray<NavigationPathBoss> PathBosses,
    ImmutableArray<NavigationHelperSpell> HelperSpells,
    NavigationWaveCombat WaveCombat,
    ImmutableArray<NavigationMechanicsOption> Options,
    ImmutableArray<NavigationPool> Pools,
    ImmutableArray<NavigationTransition> Transitions,
    ImmutableArray<NavigationFormula> Formulas,
    ImmutableArray<NavigationVerificationFixture> VerificationFixtures,
    ImmutableArray<NavigationMechanicsBinding> Bindings)
{
    public ImmutableArray<NavigationCategoryMechanics> Categories => CategoryBasics;

    public Rational RandomSelectionMass =>
        Enumerable.Repeat(RandomSelectionProbability.Value, 12)
            .Aggregate(new Rational(0, 1), (sum, value) => sum + value);

    public NavigationMechanicsOption Option(string id) =>
        Options.Single(option => option.Id == id);

    public NavigationPool Pool(string id) =>
        Pools.Single(pool => pool.Id == id);

    public NavigationVerificationFixture Fixture(string id) =>
        VerificationFixtures.Single(fixture => fixture.Id == id);

    public bool CanSafelyRecommendWave =>
        WaveCombat.RemainingEffectiveHp.State == NavigationKnowledgeState.Known &&
        WaveCombat.TargetCounts.State == NavigationKnowledgeState.Known &&
        WaveCombat.ControlDeficits.State == NavigationKnowledgeState.Known &&
        WaveCombat.ActiveDuration.State == NavigationKnowledgeState.Known;

    public WaveRecommendationResult GetWaveDisposition()
    {
        if (CanSafelyRecommendWave)
            return new(WaveDisposition.SafeRecommendation, []);
        var reasons = new[]
            {
                WaveCombat.RemainingEffectiveHp,
                WaveCombat.TargetCounts,
                WaveCombat.ControlDeficits,
                WaveCombat.ActiveDuration
            }
            .Where(field => field.State == NavigationKnowledgeState.Unknown)
            .Select(field => field.Reason)
            .Distinct()
            .ToImmutableArray();
        return new(WaveDisposition.NoSafeRecommendation, reasons);
    }
}

public static class NavigationMechanicsProfileLoader
{
    private const string FileName = "navigation-mechanics-2314.json";
    private const string ExpectedJassHash =
        "0bccc47907a9505f38efaf6bbf20228a728eabdfaec3209cca7df2269bfc2028";
    private const string ExpectedProfileHash =
        "b0b0825700c5c4d39aed9a91a42c2b2782490eca88b470a9c1aaf3340833a7f3";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static readonly (string Id, string Function, int Start, int End,
        int Evidence, NavigationSourcePurpose Purpose)[] ExpectedRanges =
        [
            ("rng-integer", "rcw", 4779, 4783, 4780, NavigationSourcePurpose.IntegerRandomRange),
            ("continuous-betting", "Ciw", 10022, 10076, 10035, NavigationSourcePurpose.ContinuousBetting),
            ("random-flavor-dispatch", "CjN", 11025, 11039, 11026, NavigationSourcePurpose.RandomFlavorDispatch),
            ("gambler-selection", "PnN", 11063, 11122, 11069, NavigationSourcePurpose.GamblerSelection),
            ("best-help-selection", "WqE", 11164, 11225, 11169, NavigationSourcePurpose.BestHelpSelection),
            ("path-selection", "kRN", 11226, 11295, 11230, NavigationSourcePurpose.PathSelection),
            ("allied-selection", "paE", 11296, 11354, 11300, NavigationSourcePurpose.AlliedSelection),
            ("navigation-action-window", "DBE", 11397, 11424, 11403, NavigationSourcePurpose.NavigationActionWindow),
            ("bounty-pity", "Euw", 13791, 13842, 13813, NavigationSourcePurpose.BountyPityRecurrence),
            ("high-gamble", "IoN", 20093, 20180, 20110, NavigationSourcePurpose.HighGamble),
            ("trait-engineering", "JwN", 21380, 21422, 21387, NavigationSourcePurpose.TraitEngineering),
            ("earthquake-effect", "CbN", 9847, 9859, 9852, NavigationSourcePurpose.EarthquakeEffect),
            ("seastone-effect", "O1w", 26076, 26084, 26078, NavigationSourcePurpose.SeastoneEffect),
            ("buster-call-effect", "O7E", 26223, 26236, 26227, NavigationSourcePurpose.BusterCallEffect),
            ("mana-potion-effect", "cDw", 52323, 52332, 52330, NavigationSourcePurpose.ManaPotionEffect),
            ("poison-effect", "cVG", 52709, 52722, 52712, NavigationSourcePurpose.PoisonEffect),
            ("allied-craft-reward", "h9E", 57573, 57587, 57578, NavigationSourcePurpose.AlliedCraftReward),
            ("navigation-definitions", "hWG", 58727, 59477, 58727, NavigationSourcePurpose.NavigationDefinitions),
            ("bounty-chest-loot", "ilw", 60837, 60876, 60843, NavigationSourcePurpose.BountyChestLoot),
            ("alchemy-decomposition", "qIw", 69632, 69662, 69658, NavigationSourcePurpose.AlchemyWrapper),
            ("rare-reroll", "uEE", 75294, 75340, 75310, NavigationSourcePurpose.RareReroll),
            ("world-gamble", "vAG", 76528, 76559, 76539, NavigationSourcePurpose.WorldGamble),
            ("royal-proc", "yxG", 82033, 82061, 82038, NavigationSourcePurpose.RoyalProc),
            ("alchemy-direct-child-runtime", "aKG", 69602, 69631, 69615, NavigationSourcePurpose.AlchemyDirectChildren),
            ("path-top-restriction-runtime", "Elw", 13612, 13625, 13615, NavigationSourcePurpose.PathTopRestriction),
            ("path-boss-1-reward-runtime", "LRE", 23056, 23088, 23077, NavigationSourcePurpose.PathBossReward),
            ("path-boss-2-reward-runtime", "ScE", 42049, 42081, 42070, NavigationSourcePurpose.PathBossReward),
            ("path-boss-3-reward-runtime", "pyN", 69217, 69249, 69238, NavigationSourcePurpose.PathBossReward),
            ("royal-attack-speed-application", "juG", 9158, 9186, 9166, NavigationSourcePurpose.RoyalAttackSpeedApplication),
            ("warcraft-attack-speed-constants", "Misc", 1, 45, 2, NavigationSourcePurpose.WarcraftAttackSpeedConstants)
        ];

    private static readonly string[] ExpectedOptionIds =
    [
        "AlliedForces.DoubleBenefit", "AlliedForces.EmergencyCall",
        "AlliedForces.TraitEngineering", "PathOfKings.MartialLaw",
        "PathOfKings.BountyHunter", "PathOfKings.RoyalLoader",
        "Gambler.Casino", "Gambler.RiskHedge", "Gambler.ContinuousBetting",
        "BestHelp.MaximumOutput", "BestHelp.Alchemy", "BestHelp.ReverseThinking",
        "Random.BlueFlavor", "Random.GreenFlavor", "Random.YellowFlavor"
    ];

    private static readonly Dictionary<string, string> ExpectedPoolOptions = new(StringComparer.Ordinal)
    {
        ["allied-double"] = "AlliedForces.DoubleBenefit",
        ["emergency-wildcards"] = "AlliedForces.EmergencyCall",
        ["trait-engineering"] = "AlliedForces.TraitEngineering",
        ["martial-law"] = "PathOfKings.MartialLaw",
        ["bounty-chest-loot"] = "PathOfKings.BountyHunter",
        ["royal-proc"] = "PathOfKings.RoyalLoader",
        ["casino-normal"] = "Gambler.Casino",
        ["casino-isekai"] = "Gambler.Casino",
        ["risk-high-gamble"] = "Gambler.RiskHedge",
        ["continuous-middle-gamble"] = "Gambler.ContinuousBetting",
        ["maximum-output"] = "BestHelp.MaximumOutput",
        ["alchemy-decomposition"] = "BestHelp.Alchemy",
        ["reverse-thinking"] = "BestHelp.ReverseThinking",
        ["random-blue"] = "Random.BlueFlavor",
        ["random-green"] = "Random.GreenFlavor",
        ["random-yellow"] = "Random.YellowFlavor"
    };

    private static readonly Dictionary<string, (string Option, NavigationTransitionKind Kind)>
        ExpectedTransitions = new(StringComparer.Ordinal)
        {
            ["allied-double-craft"] = ("AlliedForces.DoubleBenefit", NavigationTransitionKind.QualifyingCraftReward),
            ["emergency-wildcard-grant"] = ("AlliedForces.EmergencyCall", NavigationTransitionKind.WildcardGrant),
            ["trait-resource-conversion"] = ("AlliedForces.TraitEngineering", NavigationTransitionKind.ResourceConversion),
            ["martial-top-restriction"] = ("PathOfKings.MartialLaw", NavigationTransitionKind.TopRestriction),
            ["bounty-dry-streak"] = ("PathOfKings.BountyHunter", NavigationTransitionKind.DryStreakMarkov),
            ["royal-enhancement"] = ("PathOfKings.RoyalLoader", NavigationTransitionKind.RoyalEnhancement),
            ["casino-sequential"] = ("Gambler.Casino", NavigationTransitionKind.SequentialGamble),
            ["risk-rising-hazard"] = ("Gambler.RiskHedge", NavigationTransitionKind.RisingHazardGamble),
            ["continuous-exclusive-milestones"] = ("Gambler.ContinuousBetting", NavigationTransitionKind.ExclusiveMilestoneLadder),
            ["maximum-output-level-change"] = ("BestHelp.MaximumOutput", NavigationTransitionKind.HelperLevelChange),
            ["alchemy-direct-unit"] = ("BestHelp.Alchemy", NavigationTransitionKind.DirectUnitDecomposition),
            ["reverse-grant-restriction"] = ("BestHelp.ReverseThinking", NavigationTransitionKind.ReverseGrantAndRestriction),
            ["random-blue-dispatch"] = ("Random.BlueFlavor", NavigationTransitionKind.UniformRandomDispatch),
            ["random-green-dispatch"] = ("Random.GreenFlavor", NavigationTransitionKind.UniformRandomDispatch),
            ["random-yellow-dispatch"] = ("Random.YellowFlavor", NavigationTransitionKind.UniformRandomDispatch),
            ["forced-round-24"] = ("AlliedForces.DoubleBenefit", NavigationTransitionKind.ForcedRoundSelection)
        };

    private static readonly Dictionary<string, (string Option, NavigationFormulaKind Kind)>
        ExpectedFormulas = new(StringComparer.Ordinal)
        {
            ["allied-double-formula"] = ("AlliedForces.DoubleBenefit", NavigationFormulaKind.DeterministicGrant),
            ["emergency-formula"] = ("AlliedForces.EmergencyCall", NavigationFormulaKind.DeterministicGrant),
            ["trait-formula"] = ("AlliedForces.TraitEngineering", NavigationFormulaKind.ResourceExchange),
            ["martial-formula"] = ("PathOfKings.MartialLaw", NavigationFormulaKind.RestrictedMultiplier),
            ["bounty-pity-formula"] = ("PathOfKings.BountyHunter", NavigationFormulaKind.BountyPity),
            ["royal-piecewise-formula"] = ("PathOfKings.RoyalLoader", NavigationFormulaKind.RoyalPiecewiseProc),
            ["casino-formula"] = ("Gambler.Casino", NavigationFormulaKind.SequentialOutcome),
            ["risk-formula"] = ("Gambler.RiskHedge", NavigationFormulaKind.SequentialOutcome),
            ["continuous-formula"] = ("Gambler.ContinuousBetting", NavigationFormulaKind.MilestoneModulo),
            ["maximum-output-formula"] = ("BestHelp.MaximumOutput", NavigationFormulaKind.HelperObjectEffects),
            ["alchemy-formula"] = ("BestHelp.Alchemy", NavigationFormulaKind.DirectRecipeChildren),
            ["reverse-formula"] = ("BestHelp.ReverseThinking", NavigationFormulaKind.RestrictedImmediateGrant),
            ["random-blue-formula"] = ("Random.BlueFlavor", NavigationFormulaKind.UniformTwelveWayDispatch),
            ["random-green-formula"] = ("Random.GreenFlavor", NavigationFormulaKind.UniformTwelveWayDispatch),
            ["random-yellow-formula"] = ("Random.YellowFlavor", NavigationFormulaKind.UniformTwelveWayDispatch)
        };

    public static NavigationMechanicsProfile LoadFromDirectory(string directory)
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            var bytes = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                MaxDepth = 64
            });
            RejectDuplicateProperties(document.RootElement, "$" );
            var profile = JsonSerializer.Deserialize<NavigationMechanicsProfile>(bytes, JsonOptions)
                ?? throw new InvalidDataException("navigation profile is empty");
            Validate(profile);
            var hash = Convert.ToHexString(
                SHA256.HashData(CanonicalizeLineEndings(bytes))).ToLowerInvariant();
            if (!hash.Equals(ExpectedProfileHash, StringComparison.Ordinal))
                throw new InvalidDataException("navigation profile bytes are not approved");
            return profile;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or
            UnauthorizedAccessException or ArgumentException or FormatException or
            OverflowException or InvalidOperationException or NullReferenceException)
        {
            throw new InvalidDataException("navigation profile is invalid", exception);
        }
    }

    private static byte[] CanonicalizeLineEndings(byte[] bytes)
    {
        var firstCarriageReturn = Array.IndexOf(bytes, (byte)'\r');
        if (firstCarriageReturn < 0)
            return bytes;

        var canonical = new byte[bytes.Length];
        Buffer.BlockCopy(bytes, 0, canonical, 0, firstCarriageReturn);
        var written = firstCarriageReturn;
        for (var index = firstCarriageReturn; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\r')
            {
                canonical[written++] = (byte)'\n';
                if (index + 1 < bytes.Length && bytes[index + 1] == (byte)'\n')
                    index++;
            }
            else
            {
                canonical[written++] = bytes[index];
            }
        }

        return canonical[..written];
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = 64,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(
            namingPolicy: null, allowIntegerValues: false));
        return options;
    }

    private static void RejectDuplicateProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"duplicate JSON property at {path}.{property.Name}");
                RejectDuplicateProperties(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                RejectDuplicateProperties(item, $"{path}[{index++}]");
        }
    }

    private static void Validate(NavigationMechanicsProfile profile)
    {
        if (profile.Source is null || profile.ActionWindow is null || profile.Limits is null ||
            profile.Bounty is null || profile.Royal is null || profile.Alchemy is null ||
            profile.ContinuousBetting is null || profile.WaveCombat is null)
            throw new InvalidDataException("required navigation section is null");

        ValidateSource(profile.Source);
        ValidateCategoriesAndOptions(profile);
        ValidateMechanics(profile);
        ValidatePools(profile);
        ValidateTransitionsAndFormulas(profile);
        ValidateFixturesAndBindings(profile);
    }

    private static void ValidateSource(NavigationSource source)
    {
        if (source.MapVersion != "2.314" || source.JassSha256 != ExpectedJassHash ||
            source.JassLengthBytes != 3_069_240 || source.JassLineCount != 88_593 ||
            source.SourceArtifact != "war3map.j")
            throw new InvalidDataException("unverified navigation source identity");

        RequireArray(source.Members, 5, "source members");
        var expectedMembers = new[]
        {
            new NavigationSourceMember("war3map.w3a", NavigationSourceRole.AbilityObjectData,
                681_032, "095b9e1d61ba83084beac0a56827448cdb2d18619efba72a84aa014a65094919",
                NavigationExtractionMode.InMemoryReadOnlyMpqMember),
            new NavigationSourceMember("war3map.w3u", NavigationSourceRole.UnitObjectData,
                857_425, "a9aa2cb9c08130c3bee970aecb05b62fdc3db867685f4997dd2533735d2ea278",
                NavigationExtractionMode.InMemoryReadOnlyMpqMember),
            new NavigationSourceMember("war3map.wts", NavigationSourceRole.TriggerStrings,
                1_756_386, "bef1477f56bfffef3ef6b030f324b7fc75a69564760e185c8aa4da8a618a275c",
                NavigationExtractionMode.InMemoryReadOnlyMpqMember),
            new NavigationSourceMember("war3map.j", NavigationSourceRole.JassMechanics,
                3_069_240, ExpectedJassHash,
                NavigationExtractionMode.InMemoryReadOnlyMpqMember),
            new NavigationSourceMember("war3mapMisc.txt", NavigationSourceRole.GameplayConstants,
                1_183, "8428c5e040ac9a75ce37209145b25b6640c560f5d96e5f068e589946317bf140",
                NavigationExtractionMode.InMemoryReadOnlyMpqMember)
        };
        if (!source.Members.SequenceEqual(expectedMembers))
            throw new InvalidDataException("source member identity mismatch");

        RequireArray(source.Ranges, ExpectedRanges.Length, "source ranges");
        EnsureUnique(source.Ranges.Select(range => range.Id), "source range id");
        for (var index = 0; index < ExpectedRanges.Length; index++)
        {
            var actual = source.Ranges[index];
            var expected = ExpectedRanges[index];
            var expectedArtifact = expected.Id == "warcraft-attack-speed-constants"
                ? "war3mapMisc.txt"
                : "war3map.j";
            var maximumLine = expectedArtifact == "war3map.j" ? source.JassLineCount : 45;
            if (actual.Id != expected.Id || actual.Artifact != expectedArtifact ||
                actual.Function != expected.Function || actual.StartLine != expected.Start ||
                actual.EndLine != expected.End || actual.EvidenceLine != expected.Evidence ||
                actual.Purpose != expected.Purpose || actual.StartLine < 1 ||
                actual.StartLine > actual.EvidenceLine ||
                actual.EvidenceLine > actual.EndLine || actual.EndLine > maximumLine)
                throw new InvalidDataException("semantic source range mismatch");
        }

        RequireArray(source.Branches, 2, "source branches");
        EnsureUnique(source.Branches.Select(branch => branch.Id), "source branch id");
        var expectedBranches = new[]
        {
            ("path-martial-zero-top", NavigationPathTopBranchKind.MartialZeroTop,
                13616, 13618, 13618, new[] { "PathOfKings.MartialLaw" }),
            ("path-non-martial-one-top", NavigationPathTopBranchKind.NonMartialOneTop,
                13619, 13621, 13621,
                new[] { "PathOfKings.BountyHunter", "PathOfKings.RoyalLoader" })
        };
        for (var index = 0; index < expectedBranches.Length; index++)
        {
            var actual = source.Branches[index];
            var expected = expectedBranches[index];
            if (actual.Id != expected.Item1 ||
                actual.SourceRangeId != "path-top-restriction-runtime" ||
                actual.Kind != expected.Item2 || actual.StartLine != expected.Item3 ||
                actual.EndLine != expected.Item4 || actual.EvidenceLine != expected.Item5 ||
                !actual.OwnerOptionIds.SequenceEqual(expected.Item6))
                throw new InvalidDataException("source branch ownership mismatch");
        }
    }

    private static void ValidateCategoriesAndOptions(NavigationMechanicsProfile profile)
    {
        RequireArray(profile.CategoryBasics, 5, "categories");
        var expectedCategories = new[]
        {
            ("AlliedForces", -1), ("PathOfKings", 1), ("Gambler", -1),
            ("BestHelp", -1), ("Random", -1)
        };
        for (var index = 0; index < expectedCategories.Length; index++)
        {
            var category = profile.CategoryBasics[index];
            if (category is null || category.Id != expectedCategories[index].Item1 ||
                category.TopUnitLimit != expectedCategories[index].Item2)
                throw new InvalidDataException("category mechanics mismatch");
            ValidateTokens(category.Effects, allowEmpty: false, "category effects");
        }

        ValidateProbability(profile.RandomSelectionProbability, allowZero: false);
        if (profile.ActionWindow.PreviewRound != 20 ||
            !profile.ActionWindow.ActionableRounds.SequenceEqual([21, 22, 23]) ||
            profile.ActionWindow.ForcedRound != 24 ||
            profile.ForcedRound24OptionId != "AlliedForces.DoubleBenefit" ||
            profile.RandomSelectionProbability.Value != new Rational(1, 12) ||
            profile.RandomSelectionMass != new Rational(1, 1))
            throw new InvalidDataException("navigation selection window mismatch");

        RequireArray(profile.Options, ExpectedOptionIds.Length, "options");
        EnsureUnique(profile.Options.Select(option => option.Id), "option id");
        if (!profile.Options.Select(option => option.Id).SequenceEqual(ExpectedOptionIds))
            throw new InvalidDataException("option order or completeness mismatch");
        var categories = profile.CategoryBasics.Select(category => category.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var option in profile.Options)
        {
            if (option is null || string.IsNullOrWhiteSpace(option.Name) ||
                !categories.Contains(option.CategoryId) || option.TopUnitLimit is < -1 or > 1)
                throw new InvalidDataException("invalid option mechanics");
            ValidateTokens(option.Effects, allowEmpty: false, "option effects");
            ValidateTokens(option.Actions, allowEmpty: true, "option actions");
            ValidateTokens(option.DisabledActions, allowEmpty: true, "disabled actions");
            ValidateSourceReferences(option.SourceRangeIds, profile.Source);
        }
        var pathRestrictionOwners = profile.Options
            .Where(option => option.SourceRangeIds.Contains(
                "path-top-restriction-runtime", StringComparer.Ordinal))
            .Select(option => option.Id);
        if (!pathRestrictionOwners.SequenceEqual(
                ["PathOfKings.MartialLaw", "PathOfKings.BountyHunter",
                    "PathOfKings.RoyalLoader"]) ||
            !profile.Option("PathOfKings.MartialLaw").SourceRangeIds.SequenceEqual(
                ["path-top-restriction-runtime", "path-selection", "navigation-definitions"]) ||
            !profile.Option("PathOfKings.BountyHunter").SourceRangeIds.SequenceEqual(
                ["path-top-restriction-runtime", "bounty-pity", "bounty-chest-loot",
                    "path-selection", "navigation-definitions"]) ||
            !profile.Option("PathOfKings.RoyalLoader").SourceRangeIds.SequenceEqual(
                ["path-top-restriction-runtime", "royal-attack-speed-application",
                    "warcraft-attack-speed-constants", "royal-proc", "path-selection",
                    "navigation-definitions"]) ||
            !profile.Option("BestHelp.Alchemy").SourceRangeIds.SequenceEqual(
                ["alchemy-direct-child-runtime", "alchemy-decomposition",
                    "best-help-selection", "navigation-definitions"]))
            throw new InvalidDataException("runtime option source map mismatch");
    }

    private static void ValidateMechanics(NavigationMechanicsProfile profile)
    {
        if (profile.Limits.MaxGambleActions != 64 || profile.Limits.MaxFutureKills != 4096 ||
            profile.Limits.MaxStates != 65_536 || profile.Limits.MaxScenarios != 4096 ||
            profile.Limits.MaxRationalBits != 262_144)
            throw new InvalidDataException("arithmetic limits mismatch");

        if (profile.Bounty.InitialChanceNumerator != 1 ||
            profile.Bounty.DryStreakIncrement != 8 ||
            profile.Bounty.RollDenominator != 10_000 ||
            profile.Bounty.ResetDryStreak != 0 ||
            profile.Bounty.FutureKillHorizon != 4096 ||
            profile.Bounty.ChestLootPoolId != "bounty-chest-loot" ||
            !profile.Bounty.BaselineChestPoolRequiresNoTreasureMap ||
            !profile.Bounty.PityNumeratorCheckpoints.SequenceEqual([1, 9, 17]))
            throw new InvalidDataException("Bounty recurrence mismatch");
        ValidateSourceReferences(profile.Bounty.SourceRangeIds, profile.Source);

        ValidateProbability(profile.Royal.ProcProbability, allowZero: false);
        ValidateProbability(profile.Royal.CooldownThreshold, allowZero: false);
        ValidateProbability(profile.Royal.SlowCooldownCoefficient, allowZero: false);
        ValidateProbability(profile.Royal.CooldownCoefficient, allowZero: false);
        if (profile.Royal.AttackSpeedTransform is null ||
            profile.Royal.AttackSpeedTransform.BonusObject is null)
            throw new InvalidDataException("Royal attack-speed transform is missing");
        var attackSpeed = profile.Royal.AttackSpeedTransform;
        var bonusObject = attackSpeed.BonusObject;
        ValidateProbability(attackSpeed.PointScale, allowZero: false);
        ValidateProbability(bonusObject.Value, allowZero: false);
        if (attackSpeed.Kind != NavigationAttackSpeedFormulaKind.WarcraftCappedAdditive ||
            attackSpeed.CapAuthority !=
                NavigationAttackSpeedCapAuthority.WarcraftDefaultNoMapOverride ||
            attackSpeed.MinimumTotalAttackSpeedPoints != -80 ||
            attackSpeed.MaximumTotalAttackSpeedPoints != 400 ||
            attackSpeed.PointScale.Value != new Rational(1, 100) ||
            bonusObject.MemberName != "war3map.w3a" || bonusObject.BaseRawcode != "AIsx" ||
            bonusObject.ObjectRawcode != "A0IW" || bonusObject.FieldId != "Isx1" ||
            bonusObject.Level != 1 || bonusObject.DataPointer != 1 ||
            bonusObject.RecordStartOffset != 405_487 ||
            bonusObject.RecordEndOffset != 405_678 || bonusObject.ValueOffset != 405_523 ||
            bonusObject.Value.Value != new Rational(1, 4) ||
            bonusObject.RecordSha256 !=
                "213765f75b0a9e52fa7f412704a2458e337d57f582d388fb716ea1ae0e0a1229" ||
            !attackSpeed.SourceRangeIds.SequenceEqual(
                ["royal-attack-speed-application", "warcraft-attack-speed-constants"]))
            throw new InvalidDataException("Royal attack-speed transform mismatch");
        ValidateSourceReferences(attackSpeed.SourceRangeIds, profile.Source);
        if (profile.Royal.AttackSpeedPoints != 25 || profile.Royal.AttackDamage != 25_000 ||
            profile.Royal.ProcProbability.Value != new Rational(2, 25) ||
            profile.Royal.Radius != 400 || profile.Royal.BaseDamage != 255_000 ||
            profile.Royal.CooldownThreshold.Value != new Rational(19, 50) ||
            profile.Royal.SlowCooldownCoefficient.Value != new Rational(3_355_263, 5) ||
            profile.Royal.CooldownCoefficient.Value != new Rational(5_368_421, 8) ||
            profile.Royal.ProcCooldownInput != "baseWeaponCooldown" ||
            !profile.Royal.SourceRangeIds.SequenceEqual(
                ["royal-attack-speed-application", "warcraft-attack-speed-constants",
                    "royal-proc"]) ||
            profile.Royal.TargetCount is null ||
            profile.Royal.TargetCount.State != NavigationKnowledgeState.Unknown ||
            profile.Royal.TargetCount.Reason != NavigationUnknownReason.DynamicGroupEnumeration)
            throw new InvalidDataException("Royal mechanics mismatch");
        ValidateSourceReferences(profile.Royal.SourceRangeIds, profile.Source);
        ValidateSourceReferences(profile.Royal.TargetCount.SourceRangeIds, profile.Source);

        if (profile.Alchemy.ManaCost != 100 || profile.Alchemy.DecompositionKind != "direct_UNIT_recipe_children" ||
            !profile.Alchemy.AllowedTiers.SequenceEqual(["special", "rare"]) ||
            !profile.Alchemy.RequiredRootProtection ||
            !profile.Alchemy.SourceRangeIds.SequenceEqual(
                ["alchemy-direct-child-runtime", "alchemy-decomposition"]))
            throw new InvalidDataException("Alchemy mechanics mismatch");
        ValidateSourceReferences(profile.Alchemy.SourceRangeIds, profile.Source);

        if (!profile.ContinuousBetting.OutcomeOrder.SequenceEqual(
                ["failure", "fixed_ship", "random_middle"]) ||
            !profile.ContinuousBetting.MilestonePrecedence.SequenceEqual([12, 6, 4, 2]) ||
            !profile.ContinuousBetting.Milestones.Select(milestone => milestone.Attempts)
                .SequenceEqual([2, 4, 6, 12]))
            throw new InvalidDataException("Continuous Betting mechanics mismatch");
        ValidateSourceReferences(profile.ContinuousBetting.SourceRangeIds, profile.Source);
        foreach (var milestone in profile.ContinuousBetting.Milestones)
        {
            if (milestone is null)
                throw new InvalidDataException("null milestone");
            ValidateTokens(milestone.Rewards, allowEmpty: false, "milestone rewards");
            ValidateSourceReferences(milestone.SourceRangeIds, profile.Source);
        }

        RequireArray(profile.PathBosses, 3, "Path bosses");
        if (!profile.PathBosses.Select(boss => boss.Ordinal).SequenceEqual([1, 2, 3]) ||
            profile.PathBosses.Any(boss => boss is null || boss.LumberReward != 2))
            throw new InvalidDataException("Path boss rewards mismatch");
        for (var index = 0; index < profile.PathBosses.Length; index++)
        {
            var boss = profile.PathBosses[index];
            if (boss.RuntimeSourceRangeId != $"path-boss-{index + 1}-reward-runtime" ||
                !boss.PresentationSourceRangeIds.SequenceEqual(["navigation-definitions"]))
                throw new InvalidDataException("Path boss runtime/presentation source mismatch");
            ValidateSourceReferences([boss.RuntimeSourceRangeId], profile.Source);
            ValidateSourceReferences(boss.PresentationSourceRangeIds, profile.Source);
        }

        RequireArray(profile.HelperSpells, 6, "helper spells");
        EnsureUnique(profile.HelperSpells.Select(spell => spell.Id), "helper spell id");
        if (!profile.HelperSpells.Select(spell => spell.Id)
                .SequenceEqual(["A0BZ", "A0BY", "A07T", "A07W", "A07X", "A055"]))
            throw new InvalidDataException("helper spell completeness mismatch");
        foreach (var spell in profile.HelperSpells)
        {
            if (spell is null || spell.State != NavigationKnowledgeState.Known)
                throw new InvalidDataException("helper spell must be source known");
            ValidateLevelValues(spell.ManaCosts, "helper mana costs");
            ValidateLevelValues(spell.Cooldowns, "helper cooldowns");
            ValidateLevelValues(spell.Levels, "helper levels");
            ValidateSourceReferences(spell.SourceRangeIds, profile.Source);
        }

        var waveFields = new[]
        {
            profile.WaveCombat.RemainingEffectiveHp,
            profile.WaveCombat.TargetCounts,
            profile.WaveCombat.ControlDeficits,
            profile.WaveCombat.ActiveDuration
        };
        if (waveFields.Any(field => field is null ||
                field.State != NavigationKnowledgeState.Unknown))
            throw new InvalidDataException("unproven wave field must remain Unknown");
        foreach (var field in waveFields)
            ValidateSourceReferences(field.SourceRangeIds, profile.Source);
        if (profile.GetWaveDisposition().Disposition != WaveDisposition.NoSafeRecommendation)
            throw new InvalidDataException("unknown wave inputs must fail closed");
    }

    private static void ValidatePools(NavigationMechanicsProfile profile)
    {
        RequireArray(profile.Pools, ExpectedPoolOptions.Count, "pools");
        EnsureUnique(profile.Pools.Select(pool => pool.Id), "pool id");
        if (!profile.Pools.Select(pool => pool.Id).SequenceEqual(ExpectedPoolOptions.Keys))
            throw new InvalidDataException("pool order or completeness mismatch");
        foreach (var pool in profile.Pools)
        {
            if (pool is null || !ExpectedPoolOptions.TryGetValue(pool.Id, out var expectedOption) ||
                pool.OptionId != expectedOption)
                throw new InvalidDataException("pool option binding mismatch");
            RequireArray(pool.Members, minimumCount: 1, "pool members");
            EnsureUnique(pool.Members.Select(member => member.Id), $"member id in {pool.Id}");
            ValidateSourceReferences(pool.SourceRangeIds, profile.Source);
            var mass = new Rational(0, 1);
            foreach (var member in pool.Members)
            {
                if (member is null || string.IsNullOrWhiteSpace(member.Id))
                    throw new InvalidDataException("invalid pool member");
                ValidateProbability(member.Probability, allowZero: false);
                ValidateSourceReferences(member.SourceRangeIds, profile.Source);
                mass += member.Probability.Value;
            }
            if (mass != new Rational(1, 1))
                throw new InvalidDataException($"pool probability mass is not one: {pool.Id}");
        }
        var alchemy = profile.Pool("alchemy-decomposition");
        if (!alchemy.SourceRangeIds.SequenceEqual(
                ["alchemy-direct-child-runtime", "alchemy-decomposition"]) ||
            !alchemy.Members.Single().SourceRangeIds.SequenceEqual(
                ["alchemy-direct-child-runtime", "alchemy-decomposition"]))
            throw new InvalidDataException("Alchemy pool runtime source map mismatch");
    }

    private static void ValidateTransitionsAndFormulas(NavigationMechanicsProfile profile)
    {
        RequireArray(profile.Transitions, ExpectedTransitions.Count, "transitions");
        EnsureUnique(profile.Transitions.Select(transition => transition.Id), "transition id");
        if (!profile.Transitions.Select(transition => transition.Id)
                .SequenceEqual(ExpectedTransitions.Keys))
            throw new InvalidDataException("transition completeness mismatch");
        foreach (var transition in profile.Transitions)
        {
            if (transition is null ||
                !ExpectedTransitions.TryGetValue(transition.Id, out var expected) ||
                transition.OptionId != expected.Option || transition.Kind != expected.Kind)
                throw new InvalidDataException("transition option/kind binding mismatch");
            ValidateParameters(transition.Integers, transition.Probabilities);
            ValidateSourceReferences(transition.SourceRangeIds, profile.Source);
        }
        if (!profile.Transitions.Single(value => value.Id == "martial-top-restriction")
                .SourceRangeIds.SequenceEqual(
                    ["path-top-restriction-runtime", "path-selection"]) ||
            !profile.Transitions.Single(value => value.Id == "bounty-dry-streak")
                .SourceRangeIds.SequenceEqual(
                    ["path-top-restriction-runtime", "bounty-pity", "bounty-chest-loot",
                        "path-selection"]) ||
            !profile.Transitions.Single(value => value.Id == "royal-enhancement")
                .SourceRangeIds.SequenceEqual(
                    ["path-top-restriction-runtime", "royal-attack-speed-application",
                        "warcraft-attack-speed-constants", "royal-proc", "path-selection"]) ||
            !profile.Transitions.Single(value => value.Id == "alchemy-direct-unit")
                .SourceRangeIds.SequenceEqual(
                    ["alchemy-direct-child-runtime", "alchemy-decomposition", "best-help-selection"]))
            throw new InvalidDataException("runtime transition source map mismatch");

        RequireArray(profile.Formulas, ExpectedFormulas.Count, "formulas");
        EnsureUnique(profile.Formulas.Select(formula => formula.Id), "formula id");
        if (!profile.Formulas.Select(formula => formula.Id).SequenceEqual(ExpectedFormulas.Keys))
            throw new InvalidDataException("formula completeness mismatch");
        foreach (var formula in profile.Formulas)
        {
            if (formula is null || !ExpectedFormulas.TryGetValue(formula.Id, out var expected) ||
                formula.OptionId != expected.Option || formula.Kind != expected.Kind)
                throw new InvalidDataException("formula option/kind binding mismatch");
            ValidateParameters(formula.Integers, formula.Probabilities);
            ValidateSourceReferences(formula.SourceRangeIds, profile.Source);
        }
        if (!profile.Formulas.Single(value => value.Id == "martial-formula")
                .SourceRangeIds.SequenceEqual(
                    ["path-top-restriction-runtime", "path-selection"]) ||
            !profile.Formulas.Single(value => value.Id == "bounty-pity-formula")
                .SourceRangeIds.SequenceEqual(
                    ["path-top-restriction-runtime", "bounty-pity", "bounty-chest-loot"]) ||
            !profile.Formulas.Single(value => value.Id == "royal-piecewise-formula")
                .SourceRangeIds.SequenceEqual(
                    ["path-top-restriction-runtime", "royal-attack-speed-application",
                        "warcraft-attack-speed-constants", "royal-proc"]) ||
            !profile.Formulas.Single(value => value.Id == "alchemy-formula")
                .SourceRangeIds.SequenceEqual(
                    ["alchemy-direct-child-runtime", "alchemy-decomposition"]))
            throw new InvalidDataException("runtime formula source map mismatch");
    }

    private static void ValidateFixturesAndBindings(NavigationMechanicsProfile profile)
    {
        RequireArray(profile.VerificationFixtures, minimumCount: 30, "verification fixtures");
        if (profile.VerificationFixtures.Length != 30)
            throw new InvalidDataException("verification fixture completeness mismatch");
        EnsureUnique(profile.VerificationFixtures.Select(fixture => fixture.Id), "fixture id");
        var optionIds = profile.Options.Select(option => option.Id).ToHashSet(StringComparer.Ordinal);
        var poolIds = profile.Pools.Select(pool => pool.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var fixture in profile.VerificationFixtures)
        {
            if (fixture is null || fixture.Input is null || fixture.Expected is null ||
                fixture.SemanticInput is null || fixture.SemanticExpected is null ||
                !optionIds.Contains(fixture.OptionId) || !poolIds.Contains(fixture.Input.PoolId))
                throw new InvalidDataException("invalid verification fixture binding");
            ValidateSourceReferences(fixture.SourceRangeIds, profile.Source);
            ValidateProbability(fixture.Expected.Mass, allowZero: true);
            ValidateProbability(fixture.Expected.ExpectedValue, allowZero: true);
            ValidateProbability(fixture.Expected.Variance, allowZero: true);
            ValidateBasisPoints(fixture.Input.CoreFloorBp);
            ValidateBasisPoints(fixture.Input.StateUtilityBeforeBp);
            ValidateBasisPoints(fixture.Expected.FloorBp);
            ValidateBasisPoints(fixture.Expected.ValueBp);
            ValidateBasisPoints(fixture.Expected.UpperBp);
            ValidateBasisPoints(fixture.Expected.TopBp);
            ValidateBasisPoints(fixture.Expected.CaptureConflictBp);
            ValidateBasisPoints(fixture.Expected.NavigationScore);
            if (fixture.Input.GambleActions < 0 || fixture.Input.FutureKills < 0 ||
                fixture.Input.AggregatedStates < 0 || fixture.Input.CoupledScenarios < 0 ||
                fixture.Input.RationalBits < 0 || fixture.Input.InitialDryStreak < 0 ||
                fixture.Expected.VarianceDenominatorBeforeReductionBits < 0 ||
                fixture.Expected.StandardDeviation < 0 || fixture.Expected.Quantile95 < 0 ||
                fixture.Expected.ExecutedActions < 0 || fixture.Expected.ExecutedKills < 0 ||
                fixture.Expected.TerminalStateCount < 0 ||
                fixture.Expected.ObservedScenarioCount < 0 ||
                fixture.Expected.TransitionCount < 0 || fixture.Expected.MaxObservedRationalBits < 0)
                throw new InvalidDataException("fixture value is out of range");
            if (fixture.Kind == NavigationFixtureKind.ArithmeticBoundary &&
                fixture.LimitKind is null || fixture.Kind != NavigationFixtureKind.ArithmeticBoundary &&
                fixture.LimitKind is not null)
                throw new InvalidDataException("fixture limit kind mismatch");
            if ((fixture.Expected.ArithmeticDisposition == ArithmeticDisposition.ArithmeticLimitExceeded) !=
                (fixture.Expected.WaveDisposition == WaveDisposition.NoSafeRecommendation))
                throw new InvalidDataException("arithmetic failure must be NoSafeRecommendation");
            ValidateFixtureOwnershipAndSemantics(profile, fixture);
        }

        RequireArray(profile.Bindings, ExpectedOptionIds.Length, "mechanics bindings");
        EnsureUnique(profile.Bindings.Select(binding => binding.OptionId), "binding option id");
        if (!profile.Bindings.Select(binding => binding.OptionId).SequenceEqual(ExpectedOptionIds))
            throw new InvalidDataException("binding option order mismatch");
        foreach (var binding in profile.Bindings)
        {
            if (binding is null)
                throw new InvalidDataException("null mechanics binding");
            ValidateBindingIds(binding.PoolIds, ExpectedPoolOptions, binding.OptionId, "pool");
            ValidateBindingIds(binding.TransitionIds, ExpectedTransitions, binding.OptionId, "transition");
            ValidateBindingIds(binding.FormulaIds, ExpectedFormulas, binding.OptionId, "formula");
            RequireArray(binding.FixtureIds, minimumCount: 1, "binding fixtures");
            EnsureUnique(binding.FixtureIds, "binding fixture id");
            if (binding.FixtureIds.Any(id => profile.VerificationFixtures.All(fixture =>
                    fixture.Id != id || fixture.OptionId != binding.OptionId)))
                throw new InvalidDataException("fixture binding mismatch");
            ValidateSourceReferences(binding.SourceRangeIds, profile.Source);
        }

        if (!profile.Bindings.Single(value => value.OptionId == "PathOfKings.MartialLaw")
                .SourceRangeIds.SequenceEqual(
                    ["path-top-restriction-runtime", "path-selection",
                        "path-boss-1-reward-runtime", "path-boss-2-reward-runtime",
                        "path-boss-3-reward-runtime", "navigation-definitions"]) ||
            !profile.Bindings.Single(value => value.OptionId == "PathOfKings.BountyHunter")
                .SourceRangeIds.SequenceEqual(
                    ["path-top-restriction-runtime", "bounty-pity", "bounty-chest-loot",
                        "path-selection", "path-boss-1-reward-runtime",
                        "path-boss-2-reward-runtime", "path-boss-3-reward-runtime",
                        "navigation-definitions"]) ||
            !profile.Bindings.Single(value => value.OptionId == "PathOfKings.RoyalLoader")
                .SourceRangeIds.SequenceEqual(
                    ["path-top-restriction-runtime", "royal-attack-speed-application",
                        "warcraft-attack-speed-constants", "royal-proc", "path-selection",
                        "path-boss-1-reward-runtime", "path-boss-2-reward-runtime",
                        "path-boss-3-reward-runtime", "navigation-definitions"]))
            throw new InvalidDataException("Path binding source map mismatch");
        if (!profile.Bindings.Single(value => value.OptionId == "BestHelp.Alchemy")
                .SourceRangeIds.SequenceEqual(
                    ["alchemy-direct-child-runtime", "alchemy-decomposition",
                        "best-help-selection", "navigation-definitions"]))
            throw new InvalidDataException("Alchemy binding source map mismatch");

        EnsureBoundExactlyOnce(profile.Bindings.SelectMany(binding => binding.PoolIds),
            profile.Pools.Select(pool => pool.Id), "pool");
        EnsureBoundExactlyOnce(profile.Bindings.SelectMany(binding => binding.TransitionIds),
            profile.Transitions.Select(transition => transition.Id), "transition");
        EnsureBoundExactlyOnce(profile.Bindings.SelectMany(binding => binding.FormulaIds),
            profile.Formulas.Select(formula => formula.Id), "formula");
        EnsureBoundExactlyOnce(profile.Bindings.SelectMany(binding => binding.FixtureIds),
            profile.VerificationFixtures.Select(fixture => fixture.Id), "fixture");
    }

    private static void ValidateFixtureOwnershipAndSemantics(
        NavigationMechanicsProfile profile,
        NavigationVerificationFixture fixture)
    {
        var pool = profile.Pools.Single(value => value.Id == fixture.Input.PoolId);
        var transition = profile.Transitions.SingleOrDefault(value =>
            value.Id == fixture.TransitionId);
        var formula = profile.Formulas.SingleOrDefault(value =>
            value.Id == fixture.FormulaId);
        if (pool.OptionId != fixture.OptionId || transition is null || formula is null ||
            transition.OptionId != fixture.OptionId || formula.OptionId != fixture.OptionId)
            throw new InvalidDataException("fixture pool/transition/formula ownership mismatch");

        var allowedSources = pool.SourceRangeIds
            .Concat(transition.SourceRangeIds)
            .Concat(formula.SourceRangeIds)
            .ToHashSet(StringComparer.Ordinal);
        if (fixture.SourceRangeIds.Any(source => !allowedSources.Contains(source)))
            throw new InvalidDataException("fixture source map mismatch");

        if (fixture.SemanticInput.ContinuousAttempts.IsDefault ||
            fixture.SemanticInput.RoyalAttackScenarios.IsDefault ||
            fixture.SemanticExpected.ContinuousRewards.IsDefault ||
            fixture.SemanticExpected.RiskRawThresholds.IsDefault ||
            fixture.SemanticExpected.RiskEffectiveSuccessPercents.IsDefault ||
            fixture.SemanticExpected.RoyalBranches.IsDefault)
            throw new InvalidDataException("semantic fixture array is null");

        switch (fixture.Kind)
        {
            case NavigationFixtureKind.ContinuousSemantic:
                ValidateContinuousFixture(fixture);
                break;
            case NavigationFixtureKind.RiskProgression:
                ValidateRiskFixture(fixture);
                break;
            case NavigationFixtureKind.RoyalPiecewise:
                ValidateRoyalFixture(fixture);
                break;
            default:
                ValidateEmptySemantics(fixture);
                break;
        }
    }

    private static void ValidateContinuousFixture(NavigationVerificationFixture fixture)
    {
        if (fixture.Id != "continuous-vector-6-12-24" ||
            fixture.OptionId != "Gambler.ContinuousBetting" ||
            fixture.Input.PoolId != "continuous-middle-gamble" ||
            fixture.TransitionId != "continuous-exclusive-milestones" ||
            fixture.FormulaId != "continuous-formula" ||
            !fixture.SourceRangeIds.SequenceEqual(["continuous-betting"]) ||
            !fixture.SemanticInput.ContinuousAttempts.SequenceEqual([6, 12, 24]) ||
            fixture.SemanticExpected.ContinuousRewards.Length != 3)
            throw new InvalidDataException("Continuous semantic fixture mismatch");
        var expected = new[]
        {
            (6, new[] { ("random_wisp", 1), ("lumber", 1) }),
            (12, new[] { ("ship", 1) }),
            (24, new[] { ("ship", 1) })
        };
        for (var index = 0; index < expected.Length; index++)
        {
            var actual = fixture.SemanticExpected.ContinuousRewards[index];
            if (actual.Attempt != expected[index].Item1 ||
                !actual.Rewards.Select(reward => (reward.Id, reward.Count))
                    .SequenceEqual(expected[index].Item2))
                throw new InvalidDataException("Continuous reward vector mismatch");
        }
        if (fixture.SemanticInput.RiskInitialSuccessPercent != 0 ||
            fixture.SemanticInput.RiskFailureStepPercent != 0 ||
            fixture.SemanticInput.RiskRollMaximum != 0 ||
            fixture.SemanticInput.RiskProgressionLength != 0 ||
            fixture.SemanticInput.RoyalAttackScenarios.Length != 0 ||
            fixture.SemanticExpected.RiskRawThresholds.Length != 0 ||
            fixture.SemanticExpected.RiskEffectiveSuccessPercents.Length != 0 ||
            fixture.SemanticExpected.RoyalBranches.Length != 0)
            throw new InvalidDataException("Continuous fixture has unrelated semantics");
    }

    private static void ValidateRiskFixture(NavigationVerificationFixture fixture)
    {
        if (fixture.Id != "risk-world-progression-22-100" ||
            fixture.OptionId != "Gambler.RiskHedge" ||
            fixture.Input.PoolId != "risk-high-gamble" ||
            fixture.TransitionId != "risk-rising-hazard" ||
            fixture.FormulaId != "risk-formula" ||
            !fixture.SourceRangeIds.SequenceEqual(["world-gamble"]) ||
            fixture.SemanticInput.RiskInitialSuccessPercent != 22 ||
            fixture.SemanticInput.RiskFailureStepPercent != 11 ||
            fixture.SemanticInput.RiskRollMaximum != 100 ||
            fixture.SemanticInput.RiskProgressionLength != 9 ||
            !fixture.SemanticExpected.RiskRawThresholds.SequenceEqual(
                [22, 33, 44, 55, 66, 77, 88, 99, 110]) ||
            !fixture.SemanticExpected.RiskEffectiveSuccessPercents.SequenceEqual(
                [22, 33, 44, 55, 66, 77, 88, 99, 100]))
            throw new InvalidDataException("Risk progression fixture mismatch");
        if (fixture.SemanticInput.ContinuousAttempts.Length != 0 ||
            fixture.SemanticInput.RoyalAttackScenarios.Length != 0 ||
            fixture.SemanticExpected.ContinuousRewards.Length != 0 ||
            fixture.SemanticExpected.RoyalBranches.Length != 0)
            throw new InvalidDataException("Risk fixture has unrelated semantics");
    }

    private static void ValidateRoyalFixture(NavigationVerificationFixture fixture)
    {
        var expectedBaseCooldowns = new[]
        {
            new Rational(1, 5), new Rational(19, 50),
            new Rational(2, 5), new Rational(1, 1)
        };
        var expectedExistingPoints = new[] { 0, 50, 390, -20 };
        var expectedCooldownBefore = new[]
        {
            new Rational(1, 5), new Rational(19, 75),
            new Rational(4, 49), new Rational(5, 4)
        };
        var expectedCooldownAfter = new[]
        {
            new Rational(4, 25), new Rational(38, 175),
            new Rational(2, 25), new Rational(20, 21)
        };
        var expectedAttacksBefore = new[] { 50, 39, 122, 8 };
        var expectedAttacksAfter = new[] { 62, 46, 125, 10 };
        var expectedTargets = new[] { 0, 1, 0, 1 };
        var expectedProcDeltas = new[]
        {
            new Rational(0, 1), new Rational(2_345_999_977, 2_500),
            new Rational(0, 1), new Rational(335_526_306, 625)
        };
        var expectedDamage = new[]
        {
            new Rational(5_368_421, 40), new Rational(101_999_999, 400),
            new Rational(5_368_421, 20), new Rational(167_763_153, 250)
        };
        var expectedBranches = new[]
        {
            NavigationRoyalPiecewiseBranch.CooldownProductMinimum,
            NavigationRoyalPiecewiseBranch.CooldownProductMinimum,
            NavigationRoyalPiecewiseBranch.CooldownProductMinimum,
            NavigationRoyalPiecewiseBranch.SlowCooldownMinimum
        };
        if (fixture.Id != "royal-piecewise-proc-branches" ||
            fixture.OptionId != "PathOfKings.RoyalLoader" ||
            fixture.Input.PoolId != "royal-proc" ||
            fixture.TransitionId != "royal-enhancement" ||
            fixture.FormulaId != "royal-piecewise-formula" ||
            !fixture.SourceRangeIds.SequenceEqual(
                ["royal-attack-speed-application", "warcraft-attack-speed-constants",
                    "royal-proc"]) ||
            fixture.Input.CoupledScenarios != expectedTargets.Length ||
            fixture.Expected.ObservedScenarioCount != expectedTargets.Length ||
            fixture.SemanticInput.RoyalAttackScenarios.Length !=
                expectedBaseCooldowns.Length ||
            fixture.SemanticExpected.RoyalBranches.Length != expectedBranches.Length)
            throw new InvalidDataException("Royal semantic fixture mismatch");
        for (var index = 0; index < expectedBaseCooldowns.Length; index++)
        {
            var input = fixture.SemanticInput.RoyalAttackScenarios[index];
            var actual = fixture.SemanticExpected.RoyalBranches[index];
            if (input is null || actual is null)
                throw new InvalidDataException("Royal branch fixture is null");
            ValidateProbability(input.BaseWeaponCooldown, allowZero: false);
            ValidateProbability(input.EngagedHorizon, allowZero: false);
            ValidateProbability(actual.ProcDamage, allowZero: false);
            ValidateProbability(actual.ProcProbability, allowZero: false);
            ValidateProbability(actual.ProcBaseWeaponCooldown, allowZero: false);
            ValidateProbability(actual.CooldownBefore, allowZero: false);
            ValidateProbability(actual.CooldownAfter, allowZero: false);
            ValidateProbability(actual.EngagedHorizon, allowZero: false);
            ValidateProbability(actual.ProcDelta, allowZero: true);
            if (input.BaseWeaponCooldown.Value != expectedBaseCooldowns[index] ||
                input.ExistingAttackSpeedPoints != expectedExistingPoints[index] ||
                input.MinimumAttackSpeedPoints != -80 ||
                input.MaximumAttackSpeedPoints != 400 ||
                input.AddedAttackSpeedPoints != 25 ||
                input.EngagedHorizon.Value != new Rational(10, 1) ||
                input.TargetsWithin400 != expectedTargets[index] ||
                actual.Branch != expectedBranches[index] ||
                actual.ProcDamage.Value != expectedDamage[index] ||
                actual.ProcProbability.Value != new Rational(2, 25) ||
                actual.ProcCooldownInput != "baseWeaponCooldown" ||
                actual.ProcBaseWeaponCooldown.Value != expectedBaseCooldowns[index] ||
                actual.CooldownBefore.Value != expectedCooldownBefore[index] ||
                actual.CooldownAfter.Value != expectedCooldownAfter[index] ||
                actual.EngagedHorizon.Value != new Rational(10, 1) ||
                actual.AttacksBefore != expectedAttacksBefore[index] ||
                actual.AttacksAfter != expectedAttacksAfter[index] ||
                actual.AttackCountDelta !=
                    expectedAttacksAfter[index] - expectedAttacksBefore[index] ||
                actual.TargetsWithin400 != expectedTargets[index] ||
                actual.ProcDelta.Value != expectedProcDeltas[index] ||
                actual.AttackDamage != 25_000 || actual.Radius != 400)
                throw new InvalidDataException("Royal branch fixture mismatch");
        }
        if (fixture.SemanticInput.ContinuousAttempts.Length != 0 ||
            fixture.SemanticInput.RiskInitialSuccessPercent != 0 ||
            fixture.SemanticInput.RiskFailureStepPercent != 0 ||
            fixture.SemanticInput.RiskRollMaximum != 0 ||
            fixture.SemanticInput.RiskProgressionLength != 0 ||
            fixture.SemanticExpected.ContinuousRewards.Length != 0 ||
            fixture.SemanticExpected.RiskRawThresholds.Length != 0 ||
            fixture.SemanticExpected.RiskEffectiveSuccessPercents.Length != 0)
            throw new InvalidDataException("Royal fixture has unrelated semantics");
    }

    private static void ValidateEmptySemantics(NavigationVerificationFixture fixture)
    {
        if (fixture.SemanticInput.ContinuousAttempts.Length != 0 ||
            fixture.SemanticInput.RiskInitialSuccessPercent != 0 ||
            fixture.SemanticInput.RiskFailureStepPercent != 0 ||
            fixture.SemanticInput.RiskRollMaximum != 0 ||
            fixture.SemanticInput.RiskProgressionLength != 0 ||
            fixture.SemanticInput.RoyalAttackScenarios.Length != 0 ||
            fixture.SemanticExpected.ContinuousRewards.Length != 0 ||
            fixture.SemanticExpected.RiskRawThresholds.Length != 0 ||
            fixture.SemanticExpected.RiskEffectiveSuccessPercents.Length != 0 ||
            fixture.SemanticExpected.RoyalBranches.Length != 0)
            throw new InvalidDataException("non-semantic fixture contains semantic values");
    }

    private static void ValidateBindingIds(
        ImmutableArray<string> ids,
        IReadOnlyDictionary<string, string> expected,
        string optionId,
        string kind)
    {
        RequireArray(ids, minimumCount: 1, $"binding {kind}s");
        EnsureUnique(ids, $"binding {kind} id");
        if (ids.Any(id => !expected.TryGetValue(id, out var boundOption) ||
                boundOption != optionId))
            throw new InvalidDataException($"{kind} binding mismatch");
    }

    private static void ValidateBindingIds<TKind>(
        ImmutableArray<string> ids,
        IReadOnlyDictionary<string, (string Option, TKind Kind)> expected,
        string optionId,
        string kind)
    {
        RequireArray(ids, minimumCount: 1, $"binding {kind}s");
        EnsureUnique(ids, $"binding {kind} id");
        if (ids.Any(id => !expected.TryGetValue(id, out var binding) ||
                binding.Option != optionId))
            throw new InvalidDataException($"{kind} binding mismatch");
    }

    private static void EnsureBoundExactlyOnce(
        IEnumerable<string> boundIds,
        IEnumerable<string> expectedIds,
        string kind)
    {
        var bound = boundIds.ToArray();
        var expected = expectedIds.ToArray();
        if (bound.Length != expected.Length ||
            bound.Distinct(StringComparer.Ordinal).Count() != bound.Length ||
            !bound.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            throw new InvalidDataException($"{kind} must be bound exactly once");
    }

    private static void ValidateParameters(
        ImmutableArray<NavigationIntegerParameter> integers,
        ImmutableArray<NavigationProbabilityParameter> probabilities)
    {
        if (integers.IsDefault || probabilities.IsDefault)
            throw new InvalidDataException("parameter array is null");
        EnsureUnique(integers.Select(parameter => parameter.Name), "integer parameter name");
        EnsureUnique(probabilities.Select(parameter => parameter.Name), "probability parameter name");
        if (integers.Any(parameter => parameter is null ||
                string.IsNullOrWhiteSpace(parameter.Name) || parameter.Value < 0))
            throw new InvalidDataException("invalid integer parameter");
        foreach (var parameter in probabilities)
        {
            if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name))
                throw new InvalidDataException("invalid probability parameter");
            ValidateProbability(parameter.Value, allowZero: true);
        }
    }

    private static void ValidateLevelValues(
        ImmutableArray<NavigationLevelValue> values,
        string name)
    {
        if (values.IsDefault)
            throw new InvalidDataException($"{name} is null");
        EnsureUnique(values.Select(value => value.Level.ToString(CultureInfo.InvariantCulture)), name);
        if (values.Any(value => value is null || value.Level <= 0 ||
                string.IsNullOrWhiteSpace(value.Value)))
            throw new InvalidDataException($"invalid {name}");
    }

    private static void ValidateProbability(
        NavigationProbability probability,
        bool allowZero)
    {
        if (probability is null ||
            !BigInteger.TryParse(probability.Numerator, NumberStyles.None,
                CultureInfo.InvariantCulture, out var numerator) ||
            !BigInteger.TryParse(probability.Denominator, NumberStyles.None,
                CultureInfo.InvariantCulture, out var denominator) ||
            numerator < 0 || denominator <= 0 || !allowZero && numerator == 0 ||
            numerator.ToString(CultureInfo.InvariantCulture) != probability.Numerator ||
            denominator.ToString(CultureInfo.InvariantCulture) != probability.Denominator ||
            BigInteger.GreatestCommonDivisor(numerator, denominator) != BigInteger.One)
            throw new InvalidDataException("rational is not normalized and reduced");
    }

    private static void ValidateSourceReferences(
        ImmutableArray<string> references,
        NavigationSource source)
    {
        RequireArray(references, minimumCount: 1, "source references");
        EnsureUnique(references, "source reference");
        var known = source.Ranges.Select(range => range.Id).ToHashSet(StringComparer.Ordinal);
        if (references.Any(reference => !known.Contains(reference)))
            throw new InvalidDataException("unknown source range reference");
    }

    private static void ValidateTokens(
        ImmutableArray<string> values,
        bool allowEmpty,
        string name)
    {
        if (values.IsDefault || !allowEmpty && values.Length == 0 ||
            values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException($"invalid {name}");
    }

    private static void ValidateBasisPoints(int value)
    {
        if (value is < 0 or > 10_000)
            throw new InvalidDataException("basis points out of range");
    }

    private static void RequireArray<T>(
        ImmutableArray<T> values,
        int minimumCount,
        string name)
    {
        if (values.IsDefault || values.Length < minimumCount)
            throw new InvalidDataException($"{name} is null or incomplete");
    }

    private static void EnsureUnique(IEnumerable<string> values, string name)
    {
        var materialized = values.ToArray();
        if (materialized.Any(string.IsNullOrWhiteSpace) ||
            materialized.Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new InvalidDataException($"duplicate or empty {name}");
    }
}
