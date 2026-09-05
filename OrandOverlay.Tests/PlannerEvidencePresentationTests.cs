using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class PlannerEvidencePresentationTests
{
    public static TheoryData<int, AdaptivePlanningApplied?, bool, PlannerEvidenceState> StateCases =>
        new()
        {
            { 10, null, false, PlannerEvidenceState.Waiting },
            { 10, Applied(NavigationRecommendationState.NoSafeRecommendation,
                PlannerPhase.AwaitMarineford,
                blockers: [AdaptiveBuildBlocker.AwaitingMarineford]),
                false, PlannerEvidenceState.Blocked },
            { 20, Applied(NavigationRecommendationState.Provisional,
                PlannerPhase.CommitRound20), false, PlannerEvidenceState.Round20Preview },
            { 21, Applied(NavigationRecommendationState.Actionable,
                PlannerPhase.Committed), false, PlannerEvidenceState.Round21Actionable },
            { 22, Applied(NavigationRecommendationState.Locked,
                PlannerPhase.Committed), false, PlannerEvidenceState.Committed },
            { 22, Applied(NavigationRecommendationState.ManualOverride,
                PlannerPhase.ManualOverride, manual: new ManualLatches(true, true)),
                false, PlannerEvidenceState.ManualOverride },
            { 24, Applied(NavigationRecommendationState.SourceExpectedForced,
                PlannerPhase.Committed, forced: "AlliedForces.DoubleBenefit"),
                false, PlannerEvidenceState.Round24Forced },
            { 20, null, true, PlannerEvidenceState.Unknown }
        };

    [Theory]
    [MemberData(nameof(StateCases))]
    public void PlannerStatesExposeTheSameMachineFieldMatrix(int round,
        AdaptivePlanningApplied? applied, bool signalsUnknown, PlannerEvidenceState expected)
    {
        var view = RecommendationPresentation.PlannerEvidence(round, applied, signalsUnknown);

        Assert.Equal(expected, view.State);
        Assert.Equal(Enum.GetValues<PlannerEvidenceFieldKind>(),
            view.Fields.Select(field => field.Kind));
        Assert.Equal(view.Fields.Length,
            view.Fields.Select(field => field.AutomationId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(view.Fields, field =>
        {
            Assert.False(string.IsNullOrWhiteSpace(field.DisplayValue));
            Assert.False(string.IsNullOrWhiteSpace(field.AccessibilityName));
            Assert.False(string.IsNullOrWhiteSpace(field.AccessibilityValue));
        });
    }

    [Fact]
    public void ManualGoalWithUnknownSignalsKeepsGoalStatusAndExplainsAutomaticLimits()
    {
        var view = RecommendationPresentation.PlannerEvidence(19,
            Applied(NavigationRecommendationState.NoSafeRecommendation,
                PlannerPhase.CommitRound20,
                blockers: [AdaptiveBuildBlocker.UnknownStoryStage,
                    AdaptiveBuildBlocker.UnknownRareWisps],
                manual: new ManualLatches(true, false)), true);

        Assert.Equal(PlannerEvidenceState.ManualOverride, view.State);
        Assert.Equal("수동 목표 유지", view[PlannerEvidenceFieldKind.Phase].DisplayValue);
        Assert.Contains("조합", view[PlannerEvidenceFieldKind.Action].DisplayValue);
        Assert.Contains("스토리", view[PlannerEvidenceFieldKind.Blocker].DisplayValue);
        Assert.Contains("희귀 위습", view[PlannerEvidenceFieldKind.Blocker].DisplayValue);
        Assert.DoesNotContain("개 입력", view[PlannerEvidenceFieldKind.Blocker].DisplayValue);
        Assert.Contains("상대 상위 유닛 수", view[PlannerEvidenceFieldKind.UnknownSignals].DisplayValue);
    }

    [Fact]
    public void NavigationEvidenceRemainsRecommendationOnlyAndCarriesAuditableNumbers()
    {
        var view = RecommendationPresentation.PlannerEvidence(21,
            Applied(NavigationRecommendationState.Actionable, PlannerPhase.Committed), false);

        Assert.True(view.IsRecommendationOnly);
        Assert.False(view.ClaimsRuntimeSelection);
        Assert.Equal("planner-physical-route", view[PlannerEvidenceFieldKind.PhysicalRoute].AutomationId);
        Assert.Equal("planner-magic-route", view[PlannerEvidenceFieldKind.MagicRoute].AutomationId);
        Assert.Equal("planner-navigation", view[PlannerEvidenceFieldKind.Navigation].AutomationId);
        Assert.Equal("planner-forced-manual", view[PlannerEvidenceFieldKind.ForcedManual].AutomationId);
        Assert.Contains("7600", view[PlannerEvidenceFieldKind.PhysicalRoute].MachineValue);
        Assert.Contains("7100", view[PlannerEvidenceFieldKind.MagicRoute].MachineValue);
        Assert.Contains("8200", view[PlannerEvidenceFieldKind.Confidence].MachineValue);
        Assert.Contains("1/2", view[PlannerEvidenceFieldKind.Recovery].MachineValue);
    }

    [Fact]
    public void ForcedSourceIdStaysMachineOnly()
    {
        const string forcedId = "AlliedForces.DoubleBenefit";
        var field = RecommendationPresentation.PlannerEvidence(24,
            Applied(NavigationRecommendationState.SourceExpectedForced,
                PlannerPhase.Committed, forced: forcedId), false)
            [PlannerEvidenceFieldKind.ForcedManual];

        Assert.DoesNotContain(forcedId, field.DisplayValue, StringComparison.Ordinal);
        Assert.Contains(forcedId, field.MachineValue, StringComparison.Ordinal);
    }

    [Fact]
    public void KoreanEvidenceWordsUseInvisibleNoBreakBoundaries()
    {
        var rendered = RecommendationBoard.KeepKoreanWordsTogether("마지막 안전 추천을 유지합니다.");

        Assert.Equal("마\u2060지\u2060막 안\u2060전 추\u2060천\u2060을 유\u2060지\u2060합\u2060니\u2060다.", rendered);
        Assert.Equal("마지막 안전 추천을 유지합니다.",
            rendered.Replace("\u2060", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void RecoveryConclusionKeepsItsSemanticPhraseTogether()
    {
        var rendered = RecommendationBoard.KeepKoreanWordsTogether(
            "신호가 안정되면 하한·평균·상한과 회복 가능성을 다시 계산합니다.");

        Assert.Contains(
            "회\u2060복\u00A0가\u2060능\u2060성\u2060을\u00A0다\u2060시\u00A0계\u2060산\u2060합\u2060니\u2060다.", rendered,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LiveAdaptivePlanningRenderSeamForwardsAppliedState()
    {
        var expected = Applied(NavigationRecommendationState.Actionable, PlannerPhase.Committed);
        int? renderedRound = null;
        AdaptivePlanningApplied? rendered = null;
        bool? renderedUnknown = null;
        string? renderedReason = "not-called";
        StoryRewardSequenceDecision? renderedSequence = null;

        MainWindow.DispatchPlannerEvidence((round, applied, unknown, reason, sequence) =>
        {
            renderedRound = round;
            rendered = applied;
            renderedUnknown = unknown;
            renderedReason = reason;
            renderedSequence = sequence;
        }, 21, expected);

        Assert.Equal(21, renderedRound);
        Assert.Same(expected, rendered);
        Assert.False(renderedUnknown);
        Assert.Null(renderedReason);
        Assert.Null(renderedSequence);
    }

    [Fact]
    public void PlannerPrimitiveConsumesTheDocumentedSemanticTokens()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = RecommendationPresentation.PlannerEvidence(10, null, false);
                var block = Assert.IsType<Border>(RecommendationBoard.PlannerEvidenceBlock(view));
                var stack = Assert.IsType<StackPanel>(block.Child);
                var header = Assert.IsType<Grid>(stack.Children[0]);
                var title = Assert.IsType<TextBlock>(header.Children[0]);
                var state = Assert.IsType<TextBlock>(header.Children[1]);
                var row = Assert.IsType<Grid>(stack.Children[1]);
                var label = Assert.IsType<TextBlock>(row.Children[0]);
                var value = Assert.IsType<TextBlock>(row.Children[1]);

                Assert.Equal(OverlayTheme.PlannerBlockMargin, block.Margin);
                Assert.Equal(OverlayTheme.PlannerBlockPadding, block.Padding);
                Assert.Equal(OverlayTheme.PlannerHeaderMargin, header.Margin);
                Assert.Equal(OverlayTheme.PlannerRowMargin, row.Margin);
                Assert.Equal(OverlayTheme.PlannerLabelColumnWidth,
                    row.ColumnDefinitions[0].Width.Value);
                Assert.Equal(OverlayTheme.PlannerTitleTypeSize, title.FontSize);
                Assert.Equal(OverlayTheme.PlannerStateTypeSize, state.FontSize);
                Assert.Equal(OverlayTheme.PlannerLabelTypeSize, label.FontSize);
                Assert.Equal(OverlayTheme.PlannerValueTypeSize, value.FontSize);

                Assert.Equal(new Thickness(0, 0, 0, 8), OverlayTheme.PlannerBlockMargin);
                Assert.Equal(new Thickness(0, 0, 0, 6), OverlayTheme.PlannerHeaderMargin);
                Assert.Equal(new Thickness(0, 0, 0, 4), OverlayTheme.PlannerRowMargin);
                Assert.Equal(new Thickness(8), OverlayTheme.PlannerBlockPadding);
                Assert.Equal(88, OverlayTheme.PlannerLabelColumnWidth);
                Assert.Equal(12, OverlayTheme.PlannerTitleTypeSize);
                Assert.Equal(9.5, OverlayTheme.PlannerStateTypeSize);
                Assert.Equal(10, OverlayTheme.PlannerLabelTypeSize);
                Assert.Equal(10.5, OverlayTheme.PlannerValueTypeSize);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static AdaptivePlanningApplied Applied(NavigationRecommendationState navigationState,
        PlannerPhase phase, ImmutableArray<AdaptiveBuildBlocker> blockers = default,
        ManualLatches manual = default, string? forced = null)
    {
        var route = new AdaptiveRouteLockCandidate(DamageLane.Physical,
            "top_physical", "package_control", 6400, true);
        var state = AdaptiveBuildState.Initial(1) with
        {
            Phase = phase,
            LockedFirstLegendId = "legend_anchor",
            RouteLock = route,
            NavigationLockId = navigationState == NavigationRecommendationState.Locked
                ? "PathOfKings.BountyHunter" : null,
            ManualLatches = manual
        };
        var option = new NavigationIntervalOptionScore(
            "PathOfKings.BountyHunter", 1, 8200, NavigationRiskPosture.FloorDefense, 900,
            new NavigationBasisPointInterval(6100, 6500),
            new NavigationBasisPointInterval(6800, 7200),
            new NavigationBasisPointInterval(7600, 8100),
            new NavigationBasisPointInterval(0, 400),
            new NavigationBasisPointInterval(6500, 7300),
            [new NavigationScenarioScore("fixture", 6100, 7000, 7900, 7200, 200, 6900,
                new NavigationSignedRational(7, 10), new Rational(1, 4), 500, 800,
                new Rational(1, 2))], ["enemy-top-count"], ["source.fixture"]);
        var navigation = new NavigationIntervalScoringResult(navigationState,
            NavigationScoringRegime.GuaranteedRecovery,
            navigationState == NavigationRecommendationState.NoSafeRecommendation
                ? null : option.OptionId,
            navigationState == NavigationRecommendationState.Locked, false, [option],
            navigationState == NavigationRecommendationState.NoSafeRecommendation
                ? [NavigationScoreBlocker.NonDominantIntervals] : [],
            ["enemy-top-count"]);
        var trace = new AdaptiveDecisionEvent(new AdaptiveDecisionEventInput(
            "fixture-v1", "profile", "map", 1, "input", phase, [],
            [new AdaptiveRouteComponent("top_physical", DamageLane.Physical, 6800, 6400, 7600),
             new AdaptiveRouteComponent("top_magic", DamageLane.Magic, 6200, 6000, 7100)],
            [option.OptionId], 8200, false,
            [new AdaptiveNavigationScenario(option.OptionId, 5000,
                new AdaptiveDecisionInterval(300, 700), new AdaptiveDecisionInterval(900, 1300))],
            AdaptiveNavigationRegime.GuaranteedRecovery,
            navigationState == NavigationRecommendationState.Locked, manual, forced));
        return new AdaptivePlanningApplied("input", state, navigation, trace)
        {
            SuggestedLegendId = "legend_anchor",
            Blockers = blockers.IsDefault ? [] : blockers
        };
    }
}
