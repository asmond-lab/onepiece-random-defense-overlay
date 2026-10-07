using System.Collections.Immutable;
using System.Globalization;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class NavigationIntervalScorerDecisionTests
{
    private static readonly NavigationMechanicsProfile Profile =
        NavigationMechanicsProfileLoader.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void CompleteSourceOptionSetIsScoredInOrdinalOrder()
    {
        var all = NavigationIntervalScorer.RequiredOptionIds.Select((id, ordinal) =>
            Option(id, ordinal, Scenario(id,
                Outcome(1, 1, 5_000 + ordinal * 100, 10_000, 5_000))))
            .ToArray();

        var result = NavigationIntervalScorer.Score(Request(21, 10_000, all));

        Assert.Equal(15, result.Options.Length);
        Assert.Equal(NavigationIntervalScorer.RequiredOptionIds,
            result.Options.OrderBy(option => option.OrdinalId).Select(option => option.OptionId));
        Assert.Equal("Random.YellowFlavor", result.RecommendedOptionId);
    }

    [Fact]
    public void InsecureRouteChoosesGuaranteedEmergencyAndRoundWindowControlsLock()
    {
        var emergency = Option("AlliedForces.EmergencyCall", 1, Scenario("e", Outcome(1, 1, 9_000, 10_000, 8_000)));
        var alchemy = Option("BestHelp.Alchemy", 10, Scenario("a", Outcome(1, 1, 7_000, 10_000, 7_000)));

        var preview = NavigationIntervalScorer.Score(Request(20, 5_000, emergency, alchemy));
        var actionable = NavigationIntervalScorer.Score(Request(21, 5_000, emergency, alchemy));

        Assert.Equal(NavigationScoringRegime.GuaranteedRecovery, preview.Regime);
        Assert.Equal("AlliedForces.EmergencyCall", preview.RecommendedOptionId);
        Assert.Equal(NavigationRecommendationState.Provisional, preview.State);
        Assert.False(preview.ShouldLockOverlayRecommendation);
        Assert.Equal(NavigationRecommendationState.Actionable, actionable.State);
        Assert.True(actionable.ShouldLockOverlayRecommendation);
    }

    [Fact]
    public void SecureRouteUsesExpectedScoreAndDoomedRouteUsesRecoveryProbability()
    {
        var casino = Option("Gambler.Casino", 6, Scenario("casino", Outcome(1, 1, 9_000, 10_000, 9_000)));
        var floor = Option("Gambler.RiskHedge", 7, Scenario("floor", Outcome(1, 1, 7_000, 10_000, 7_000)));
        var secure = NavigationIntervalScorer.Score(Request(21, 10_000, casino, floor));

        var highRecovery = Option("Gambler.ContinuousBetting", 8, Scenario("high",
            Outcome(3, 4, 10_000, 10_000, 10_000), Outcome(1, 4, 0, 0, 0)));
        var lowRecovery = Option("Gambler.Casino", 6, Scenario("low",
            Outcome(1, 2, 10_000, 10_000, 10_000), Outcome(1, 2, 4_000, 0, 4_000)));
        var doomed = NavigationIntervalScorer.Score(Request(21, 0, highRecovery, lowRecovery));

        Assert.Equal(NavigationScoringRegime.SecureCore, secure.Regime);
        Assert.Equal("Gambler.Casino", secure.RecommendedOptionId);
        Assert.Equal(NavigationScoringRegime.DesperationRecovery, doomed.Regime);
        Assert.Equal("Gambler.ContinuousBetting", doomed.RecommendedOptionId);
    }

    [Fact]
    public void CoupledScenarioCornersAreNotIndependentlyMixedAndCrossingIntervalsRefuseGuess()
    {
        var crossing = Option("Gambler.Casino", 6,
            Scenario("coherent-high", Outcome(1, 1, 9_000, 10_000, 9_000)),
            Scenario("coherent-low", Outcome(1, 1, 1_000, 10_000, 1_000)));
        var middle = Option("Gambler.RiskHedge", 7,
            Scenario("middle", Outcome(1, 1, 5_000, 10_000, 5_000)));

        var result = NavigationIntervalScorer.Score(Request(21, 10_000, crossing, middle));
        var casino = result.Options.Single(option => option.OptionId == "Gambler.Casino");

        Assert.Equal(2, casino.Scenarios.Length);
        Assert.Equal(casino.Score.Upper - casino.Score.Lower, casino.UncertaintyBp);
        Assert.Equal(NavigationRecommendationState.NoSafeRecommendation, result.State);
        Assert.Contains(NavigationScoreBlocker.NonDominantIntervals, result.Blockers);
    }

    [Fact]
    public void ConfidenceIsMinimumAndMissingSignalReasonSurvivesNoSafeResult()
    {
        var option = Option("BestHelp.MaximumOutput", 9,
            Scenario("unknown-helper", Outcome(1, 1, 8_000, 10_000, 8_000))) with
        {
            ConfidenceFactorsBp = [9_000, 7_999],
            MissingSignalIds = ["helperMana"]
        };

        var result = NavigationIntervalScorer.Score(Request(21, 10_000, option));

        Assert.Equal(7_999, result.Options.Single().ConfidenceBp);
        Assert.Contains(NavigationScoreBlocker.InsufficientConfidence, result.Blockers);
        Assert.Collection(result.MissingSignalIds,
            signal => Assert.Equal("helperMana", signal));
    }

    [Fact]
    public void ExactTiesUseCurrentRecommendationLastThenOrdinalAcrossCultures()
    {
        var left = Option("AlliedForces.DoubleBenefit", 0,
            Scenario("same-left", Outcome(1, 1, 8_000, 10_000, 8_000))) with
        { Posture = NavigationRiskPosture.HighCeiling };
        var right = Option("AlliedForces.EmergencyCall", 1,
            Scenario("same-right", Outcome(1, 1, 8_000, 10_000, 8_000))) with
        { Posture = NavigationRiskPosture.FloorDefense };
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ko-KR");
            var ordinal = NavigationIntervalScorer.Score(Request(21, 10_000, left, right));
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var antiFlicker = NavigationIntervalScorer.Score(Request(21, 10_000, left, right) with
            { CurrentOverlayRecommendationId = right.OptionId });

            Assert.Equal(left.OptionId, ordinal.RecommendedOptionId);
            Assert.Equal(right.OptionId, antiFlicker.RecommendedOptionId);
            Assert.Equal(NavigationRiskPosture.FloorDefense,
                antiFlicker.Options.Single(option => option.OptionId == right.OptionId).Posture);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void CurrentRecommendationCannotOverrideAnyNumericComparatorDifference()
    {
        var stronger = Option("AlliedForces.DoubleBenefit", 0,
            Scenario("stronger", Outcome(1, 1, 9_000, 10_000, 9_000)));
        var current = Option("AlliedForces.EmergencyCall", 1,
            Scenario("current", Outcome(1, 1, 8_000, 10_000, 8_000)));

        var result = NavigationIntervalScorer.Score(Request(21, 10_000,
            stronger, current) with { CurrentOverlayRecommendationId = current.OptionId });

        Assert.Equal(stronger.OptionId, result.RecommendedOptionId);
    }

    [Fact]
    public void ManualLockedForcedAndTransientStatesNeverClaimMapSelection()
    {
        var candidate = Option("Gambler.Casino", 6,
            Scenario("candidate", Outcome(1, 1, 8_000, 10_000, 8_000)));
        var prior = NavigationIntervalScorer.Score(Request(21, 10_000, candidate));
        var manual = NavigationIntervalScorer.Score(Request(21, 10_000, candidate) with
        { ManualNavigationOverride = true, ManualOverlayOptionId = "BestHelp.Alchemy" });
        var locked = NavigationIntervalScorer.Score(Request(22, 10_000, candidate) with
        { LockedOverlayRecommendationId = prior.RecommendedOptionId, PreviousResult = prior });
        var forced = NavigationIntervalScorer.Score(Request(24, 10_000, candidate));
        var transient = NavigationIntervalScorer.Score(Request(24, 10_000, candidate) with
        {
            InputState = NavigationScoringInputState.Transient,
            ManualNavigationOverride = true,
            ManualOverlayOptionId = "BestHelp.Alchemy",
            LockedOverlayRecommendationId = "Gambler.RiskHedge",
            PreviousResult = prior
        });

        Assert.Equal(NavigationRecommendationState.ManualOverride, manual.State);
        Assert.Equal("BestHelp.Alchemy", manual.RecommendedOptionId);
        Assert.Same(prior, locked);
        Assert.Equal(NavigationRecommendationState.SourceExpectedForced, forced.State);
        Assert.Equal("AlliedForces.DoubleBenefit", forced.RecommendedOptionId);
        Assert.Same(prior, transient);
        Assert.All([manual, locked, forced, transient], result =>
        {
            Assert.True(result.IsRecommendationOnly);
            Assert.False(result.ClaimsRuntimeSelection);
        });
    }

    [Fact]
    public void RationalBitAndCoupledScenarioLimitsFailClosed()
    {
        var tooWide = Option("Gambler.Casino", 6, Scenario("wide",
            Outcome(1, 32, 10_000, 10_000, 10_000),
            Outcome(31, 32, 0, 10_000, 0)));
        var bitResult = NavigationIntervalScorer.Score(Request(21, 10_000, tooWide) with
        { MaxRationalBits = 4 });
        var scenarioResult = NavigationIntervalScorer.Score(Request(21, 10_000,
            Option("Gambler.Casino", 6,
                Scenario("one", Outcome(1, 1, 8_000, 10_000, 8_000)),
                Scenario("two", Outcome(1, 1, 8_000, 10_000, 8_000)))) with
        { MaxCoupledScenarios = 1 });
        var stateResult = NavigationIntervalScorer.Score(Request(21, 10_000,
            Option("Gambler.Casino", 6, Scenario("states",
                Outcome(1, 2, 8_000, 10_000, 8_000),
                Outcome(1, 2, 9_000, 10_000, 9_000)))) with
        { MaxOutcomeStates = 1 });

        Assert.All([bitResult, scenarioResult, stateResult], result =>
            Assert.Contains(NavigationScoreBlocker.ArithmeticLimitExceeded, result.Blockers));
    }

    [Fact]
    public void Full4096KillBountyDistributionPassesMomentsAndFinalScore()
    {
        var bounty = new PathOfKingsNavigationSimulation(Profile).SimulateBounty(
            new PathOfKingsSimulationInput(1, 0, 4_096, 0, false, [], []));
        var outcomes = bounty.Bounty!.TerminalStates.Select(state =>
        {
            var utility = 5_000 + state.DryStreak % 100;
            return new NavigationScoringOutcome(
                state.Probability, utility, utility, utility);
        }).ToArray();
        var option = Option("PathOfKings.BountyHunter", 4,
            Scenario("bounty-4096", outcomes));

        var result = NavigationIntervalScorer.Score(Request(21, 5_000, option));

        Assert.Equal(PathOfKingsDisposition.Eligible, bounty.Disposition);
        Assert.Equal(4_096, bounty.Bounty.ExecutedKills);
        Assert.DoesNotContain(NavigationScoreBlocker.ArithmeticLimitExceeded, result.Blockers);
        Assert.Single(result.Options.Single().Scenarios);
    }

    private static NavigationIntervalScoringRequest Request(int round, int beforeCore,
        params NavigationIntervalOptionInput[] active)
    {
        var byId = active.ToDictionary(option => option.OptionId, StringComparer.Ordinal);
        var options = NavigationIntervalScorer.RequiredOptionIds.Select((id, ordinal) =>
            byId.TryGetValue(id, out var option)
                ? option
                : new NavigationIntervalOptionInput(id, ordinal, false, [10_000], [],
                    NavigationRiskPosture.Balanced, [])).ToImmutableArray();
        return new NavigationIntervalScoringRequest
        {
            Round = round,
            BeforeBuildBp = 5_000,
            BeforeCoreBp = beforeCore,
            BeforeCombatBp = 5_000,
            RouteConfidenceBp = 10_000,
            MechanicsConfidenceBp = 10_000,
            Options = options
        };
    }

    private static NavigationIntervalOptionInput Option(string id, int ordinal,
        params NavigationCoupledScenario[] scenarios) => new(id, ordinal, true,
            [10_000], [], NavigationRiskPosture.Balanced, scenarios.ToImmutableArray());

    private static NavigationCoupledScenario Scenario(string id,
        params NavigationScoringOutcome[] outcomes) => new(id, outcomes.ToImmutableArray());

    private static NavigationScoringOutcome Outcome(long numerator, long denominator,
        int build, int core, int combat) =>
        new(new Rational(numerator, denominator), build, core, combat);
}
