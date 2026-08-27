using System.Collections.Immutable;

namespace OrandOverlay;

public static class NavigationIntervalScorer
{
    public const int MinimumRecommendationConfidenceBp = 8_000;

    public static readonly ImmutableArray<string> RequiredOptionIds =
    [
        "AlliedForces.DoubleBenefit", "AlliedForces.EmergencyCall",
        "AlliedForces.TraitEngineering", "PathOfKings.MartialLaw",
        "PathOfKings.BountyHunter", "PathOfKings.RoyalLoader",
        "Gambler.Casino", "Gambler.RiskHedge", "Gambler.ContinuousBetting",
        "BestHelp.MaximumOutput", "BestHelp.Alchemy", "BestHelp.ReverseThinking",
        "Random.BlueFlavor", "Random.GreenFlavor", "Random.YellowFlavor"
    ];

    public static NavigationIntervalScoringResult Score(
        NavigationIntervalScoringRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        if (request.InputState == NavigationScoringInputState.Transient)
            return request.PreviousResult ?? Terminal(
                NavigationRecommendationState.NoSafeRecommendation, null,
                NavigationScoreBlocker.InputNotReady, isFrozen: true);
        if (request.Round >= 24)
            return Terminal(NavigationRecommendationState.SourceExpectedForced,
                request.ForcedRound24OptionId, null);
        if (request.ManualNavigationOverride)
            return Terminal(NavigationRecommendationState.ManualOverride,
                request.ManualOverlayOptionId, NavigationScoreBlocker.ManualOverride);
        if (request.Round is >= 21 and <= 23 &&
            !string.IsNullOrWhiteSpace(request.LockedOverlayRecommendationId))
        {
            return request.PreviousResult is { } previous &&
                previous.RecommendedOptionId == request.LockedOverlayRecommendationId
                ? previous
                : Terminal(NavigationRecommendationState.Locked,
                    request.LockedOverlayRecommendationId, null);
        }
        if (request.Round < 20)
            return Terminal(NavigationRecommendationState.Waiting, null,
                NavigationScoreBlocker.BeforeRound20);
        if (request.InputState != NavigationScoringInputState.Ready)
            return Terminal(NavigationRecommendationState.NoSafeRecommendation, null,
                NavigationScoreBlocker.InputNotReady);
        if (!HasCompleteOptionSet(request.Options))
            return Terminal(NavigationRecommendationState.NoSafeRecommendation, null,
                NavigationScoreBlocker.NoEligibleOptions);

        var scenarioCount = request.Options.Sum(option => option.CoupledScenarios.Length);
        var outcomeCount = request.Options.Sum(option => option.CoupledScenarios.Sum(
            scenario => scenario.Outcomes.Length));
        if (scenarioCount > request.MaxCoupledScenarios ||
            outcomeCount > request.MaxOutcomeStates)
            return Terminal(NavigationRecommendationState.NoSafeRecommendation, null,
                NavigationScoreBlocker.ArithmeticLimitExceeded);

        var scored = ImmutableArray.CreateBuilder<NavigationIntervalOptionScore>();
        var beforeUtility = NavigationIntervalScorerMath.StateUtilityBp(
            request.BeforeBuildBp, request.BeforeCoreBp, request.BeforeCombatBp);
        foreach (var option in request.Options.Where(option => option.TopCompatible))
        {
            if (!TryScoreOption(request, option, beforeUtility, out var result))
                return Terminal(NavigationRecommendationState.NoSafeRecommendation, null,
                    NavigationScoreBlocker.ArithmeticLimitExceeded);
            scored.Add(result!);
        }
        if (scored.Count == 0)
            return Terminal(NavigationRecommendationState.NoSafeRecommendation, null,
                NavigationScoreBlocker.NoEligibleOptions);

        var eligible = scored.Where(option =>
                option.ConfidenceBp >= MinimumRecommendationConfidenceBp)
            .ToImmutableArray();
        if (eligible.IsDefaultOrEmpty)
            return NoSafe(scored.ToImmutable(), NavigationScoreBlocker.InsufficientConfidence);

        var regime = DetermineRegime(request.BeforeCoreBp, eligible);
        var contenders = regime == NavigationScoringRegime.GuaranteedRecovery
            ? eligible.Where(GuaranteesRecovery).ToImmutableArray()
            : eligible;
        var winner = NavigationIntervalScorerComparator.FindRobustWinner(contenders, regime,
            request.CurrentOverlayRecommendationId);
        if (winner is null)
            return NoSafe(scored.ToImmutable(), NavigationScoreBlocker.NonDominantIntervals,
                regime);

        var state = request.Round == 20
            ? NavigationRecommendationState.Provisional
            : NavigationRecommendationState.Actionable;
        return new NavigationIntervalScoringResult(
            state, regime, winner.OptionId,
            ShouldLockOverlayRecommendation: state == NavigationRecommendationState.Actionable,
            IsFrozen: false, scored.ToImmutable(), [],
            winner.MissingSignalIds);
    }

    private static bool TryScoreOption(NavigationIntervalScoringRequest request,
        NavigationIntervalOptionInput option, int beforeUtility,
        out NavigationIntervalOptionScore? result)
    {
        if (string.IsNullOrWhiteSpace(option.OptionId) || option.OrdinalId < 0 ||
            option.CoupledScenarios.IsDefaultOrEmpty)
            throw new ArgumentException("Every option needs identity and coupled scenarios.");
        var scenarioScores = ImmutableArray.CreateBuilder<NavigationScenarioScore>();
        foreach (var scenario in option.CoupledScenarios)
        {
            if (!NavigationIntervalScorerScenario.TryEvaluate(
                    scenario, beforeUtility, request.MaxRationalBits, out var score))
            {
                result = null;
                return false;
            }
            scenarioScores.Add(score!);
        }
        var scenarios = scenarioScores.ToImmutable();
        var confidence = new[] { request.RouteConfidenceBp, request.MechanicsConfidenceBp }
            .Concat(option.ConfidenceFactorsBp.IsDefault
                ? [] : option.ConfidenceFactorsBp)
            .Min();
        var minScore = scenarios.Min(score => score.NavigationScoreBp);
        var maxScore = scenarios.Max(score => score.NavigationScoreBp);
        result = new NavigationIntervalOptionScore(
            option.OptionId, option.OrdinalId, confidence, option.Posture,
            maxScore - minScore,
            Interval(scenarios, value => value.FloorBp),
            Interval(scenarios, value => value.ValueBp),
            Interval(scenarios, value => value.UpperBp),
            Interval(scenarios, value => value.CaptureConflictBp),
            new NavigationBasisPointInterval(minScore, maxScore),
            scenarios,
            option.MissingSignalIds.IsDefault ? [] : option.MissingSignalIds,
            option.SourceReasonIds.IsDefault ? [] : option.SourceReasonIds);
        return true;
    }

    private static NavigationBasisPointInterval Interval(
        ImmutableArray<NavigationScenarioScore> scenarios,
        Func<NavigationScenarioScore, int> selector) =>
        new(scenarios.Min(selector), scenarios.Max(selector));

    private static NavigationScoringRegime DetermineRegime(int beforeCoreBp,
        ImmutableArray<NavigationIntervalOptionScore> options)
    {
        if (beforeCoreBp == 10_000)
            return NavigationScoringRegime.SecureCore;
        return options.Any(GuaranteesRecovery)
            ? NavigationScoringRegime.GuaranteedRecovery
            : NavigationScoringRegime.DesperationRecovery;
    }

    private static bool GuaranteesRecovery(NavigationIntervalOptionScore option) =>
        option.Scenarios.All(scenario =>
            NavigationIntervalScorerMath.Compare(
                scenario.RecoveryProbability, new Rational(1, 1)) == 0);

    private static bool HasCompleteOptionSet(
        ImmutableArray<NavigationIntervalOptionInput> options) =>
        !options.IsDefault && options.Length == RequiredOptionIds.Length &&
        options.Select(option => option.OptionId)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(RequiredOptionIds.Order(StringComparer.Ordinal),
                StringComparer.Ordinal) &&
        options.All(option => option.OrdinalId ==
            RequiredOptionIds.IndexOf(option.OptionId));

    private static NavigationIntervalScoringResult NoSafe(
        ImmutableArray<NavigationIntervalOptionScore> options,
        NavigationScoreBlocker blocker,
        NavigationScoringRegime regime = NavigationScoringRegime.None) =>
        new(NavigationRecommendationState.NoSafeRecommendation, regime, null, false,
            false, options, [blocker], options.SelectMany(option => option.MissingSignalIds)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray());

    private static NavigationIntervalScoringResult Terminal(
        NavigationRecommendationState state, string? optionId,
        NavigationScoreBlocker? blocker, bool isFrozen = false) =>
        new(state, NavigationScoringRegime.None, optionId, false, isFrozen, [],
            blocker is { } value ? [value] : [], []);

    private static void ValidateRequest(NavigationIntervalScoringRequest request)
    {
        if (request.Round < 0 || request.MaxCoupledScenarios <= 0 ||
            request.MaxOutcomeStates <= 0 ||
            request.MaxRationalBits <= 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        NavigationIntervalScorerMath.ValidateBasisPoints(request.BeforeBuildBp);
        NavigationIntervalScorerMath.ValidateBasisPoints(request.BeforeCoreBp);
        NavigationIntervalScorerMath.ValidateBasisPoints(request.BeforeCombatBp);
        NavigationIntervalScorerMath.ValidateBasisPoints(request.RouteConfidenceBp);
        NavigationIntervalScorerMath.ValidateBasisPoints(request.MechanicsConfidenceBp);
        foreach (var confidence in request.Options.SelectMany(option =>
                     option.ConfidenceFactorsBp.IsDefault ? [] : option.ConfidenceFactorsBp))
            NavigationIntervalScorerMath.ValidateBasisPoints(confidence);
    }

}
