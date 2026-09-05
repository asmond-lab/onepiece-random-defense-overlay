using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class NavigationQuestContextTests
{
    [Fact]
    public void SavedCandidateDoesNotConfirmGameSelectionAndResetClearsConfirmation()
    {
        var session = new NavigationSessionState();
        Assert.Equal("조로 · 항법 미선택", session.Header("조로"));
        Assert.DoesNotContain("바운티헌터", NavigationSessionState.Candidate(null));
        session.Confirm("PathOfKings.BountyHunter");
        Assert.Equal("조로 · 바운티헌터 (선택 확인)", session.Header("조로"));
        session.Reset();
        Assert.Null(session.ConfirmedOptionId);
        Assert.Throws<ArgumentException>(() => session.Confirm("invalid"));
        Assert.Equal(int.MaxValue, NavigationProfiles.Find("Unselected").TopUnitLimit);
    }

    [Fact]
    public void TwoQuestGoalExcludesSingleTopNavigationAndAllocatesSharedMaterialsOnce()
    {
        var source = Source() with { RouteQuests = Quests(RouteQuestStatus.Active),
            PursueBothRouteQuests = true };
        var quest = RouteQuestEvaluation.Evaluate(source);
        Assert.Equal(2, quest.PlannedTopCount);
        Assert.Equal(3, quest.FutureCommonWisps);
        Assert.Equal(0, quest.SurplusCommonWisps);
        Assert.Contains("부족 재료 4개", quest.Description);
        var options = quest.Apply(Options());
        Assert.False(options.Single(x => x.OptionId == "PathOfKings.BountyHunter").TopCompatible);
        Assert.False(options.Single(x => x.OptionId == "PathOfKings.RoyalLoader").TopCompatible);
        Assert.True(options.Single(x => x.OptionId == "AlliedForces.DoubleBenefit").TopCompatible);
    }

    [Theory]
    [InlineData(RouteQuestStatus.Unknown)]
    [InlineData(RouteQuestStatus.Inactive)]
    [InlineData(RouteQuestStatus.Completed)]
    public void UnconfirmedOrAlreadyClaimedQuestDoesNotCreateRewards(RouteQuestStatus status)
    {
        var quest = RouteQuestEvaluation.Evaluate(Source() with { RouteQuests = Quests(status) });
        Assert.Equal(0, quest.FutureCommonWisps);
        Assert.Equal(0, quest.FutureBuildUpperBp);
    }

    [Fact]
    public void QuestRewardIsFutureUpperOnlyAndCannotFundItsOwnCraft()
    {
        var quest = RouteQuestEvaluation.Evaluate(Source() with
        {
            RouteQuests = Quests(RouteQuestStatus.Active),
            Inventory = [new InventoryEntry { UnitId = "common", Count = 3 }]
        });
        Assert.Equal(2, quest.FutureCommonWisps);
        var before = Options();
        var after = quest.Apply(before);
        Assert.Equal(before[0].CoupledScenarios[0], after[0].CoupledScenarios[0]);
        Assert.Equal(2, after[0].CoupledScenarios.Length);
        Assert.True(after[0].CoupledScenarios[1].Outcomes[0].AfterBuildBp >
            before[0].CoupledScenarios[0].Outcomes[0].AfterBuildBp);
    }

    [Fact]
    public void CompletedTopIsNotDoubleCountedFromInventoryAndHistory()
    {
        var quest = RouteQuestEvaluation.Evaluate(Source() with
        {
            Inventory = [new InventoryEntry { UnitId = "zoro", Count = 1 }],
            CompletedTopUnitIds = ["zoro"]
        });
        Assert.Equal(1, quest.PlannedTopCount);
    }

    [Fact]
    public void AlreadyOwnedTargetDoesNotPromiseASecondQuestPayout()
    {
        var quest = RouteQuestEvaluation.Evaluate(Source() with
        {
            RouteQuests = Quests(RouteQuestStatus.Active),
            Inventory = [new InventoryEntry { UnitId = "zoro", Count = 1 }]
        });
        Assert.Equal(0, quest.FutureCommonWisps);
        Assert.Contains("목표 이미 보유", quest.Description);
    }

    [Fact]
    public void ContinuousEvaluationIgnoresOldRecommendationLockAndRound24Fallback()
    {
        var result = NavigationIntervalScorer.Score(new NavigationIntervalScoringRequest
        {
            Round = 30, EvaluateContinuously = true,
            ManualNavigationOverride = true, ManualOverlayOptionId = "PathOfKings.BountyHunter",
            LockedOverlayRecommendationId = "PathOfKings.BountyHunter",
            RouteConfidenceBp = 10_000, MechanicsConfidenceBp = 10_000,
            Options = RouteQuestEvaluation.Evaluate(Source() with
            {
                RouteQuests = Quests(RouteQuestStatus.Active), PursueBothRouteQuests = true
            }).Apply(Options())
        });
        Assert.DoesNotContain(result.Options, x => x.OptionId == "PathOfKings.BountyHunter");
        Assert.NotEmpty(result.Options);
        Assert.NotEqual(NavigationRecommendationState.SourceExpectedForced, result.State);
        Assert.NotEqual(NavigationRecommendationState.ManualOverride, result.State);
    }

    private static RouteQuestSnapshot Quests(RouteQuestStatus transcendent) =>
        transcendent == RouteQuestStatus.Unknown ? RouteQuestSnapshot.Unknown :
        RouteQuestSnapshot.FromVerifiedSlots([
            new(0, transcendent == RouteQuestStatus.Inactive ? "Q002" : "Q008",
                transcendent == RouteQuestStatus.Completed),
            new(1, "Q011", false), new(2, "Q016", false)]);

    private static AdaptivePlanningInputSource Source() => new()
    {
        MatchGeneration = 1, RecognitionRevision = 1, Round = 21, Phase = PlannerPhase.Committed,
        GoalUnitId = "zoro", NavigationOptionId = "PathOfKings.BountyHunter",
        GoroseiMode = GoroseiMode.None, ManualLatches = ManualLatches.None,
        Inventory = [new InventoryEntry { UnitId = "common", Count = 1 }],
        Units = new Dictionary<string, UnitDefinition>
        {
            ["common"] = new() { Id = "common", Name = "루피", Tier = "흔함" },
            ["zoro"] = new() { Id = "zoro", Name = "조로", Tier = "초월", Recipe = new() { ["common"] = 3 } },
            ["limited"] = new() { Id = "limited", Name = "제한 목표", Tier = "제한됨", Recipe = new() { ["common"] = 2 } }
        }
    };

    private static ImmutableArray<NavigationIntervalOptionInput> Options() =>
        NavigationIntervalScorer.RequiredOptionIds.Select((id, index) =>
            new NavigationIntervalOptionInput(id, index, true, [10_000], [],
                NavigationRiskPosture.Balanced,
                [new(id, [new(new Rational(1, 1), 1000, 1000, 1000)])])).ToImmutableArray();
}
