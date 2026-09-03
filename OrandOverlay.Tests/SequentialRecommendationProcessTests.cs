using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class SequentialRecommendationProcessTests
{
    [Fact]
    public void FirstRareValuesUpcomingWispOutcomesBeforeShowingTopAndNavigation()
    {
        var decision = StoryRewardSequencePlanner.Evaluate(Input(
            PlannerPhase.AccumulateSpecialUncommon,
            activeStage: 4,
            rewards: [Reward("e016", 2), Reward("e017", 1)]));

        Assert.Equal(StorySequenceAction.WaitForStoryReward, decision.Action);
        Assert.Equal(RecommendationSequenceStage.StoryReward, decision.Stage);
        Assert.Equal("legend-a", decision.RecommendedLegendId);
        Assert.Equal(8750, decision.ExpectedUsefulUnitBp);
        Assert.Contains("특별위습 2", decision.ClearRewardSummary, StringComparison.Ordinal);
        Assert.Contains("안흔위습 1", decision.ClearRewardSummary, StringComparison.Ordinal);
        Assert.Contains("특별 유효 특별 A", decision.OutcomeValueSummary,
            StringComparison.Ordinal);
        Assert.Contains("안흔 유효 안흔 A", decision.OutcomeValueSummary,
            StringComparison.Ordinal);
        Assert.Contains("2 스토리 보상 [현재]", decision.StepSummary, StringComparison.Ordinal);
        Assert.False(decision.TopNavigationUnlocked);
    }

    [Fact]
    public void SharedRewardOutcomeIsAttributedToOnlyOneLegendCandidate()
    {
        var units = new[]
        {
            Unit("rare-a", "첫 희귀", "희귀함"),
            Unit("shared-special", "공유 특별", "특별함"),
            Unit("exclusive-special", "전용 특별", "특별함"),
            Unit("uncommon-a", "안흔 A", "안흔함"),
            Unit("legend-a", "전설 A", "전설", new Dictionary<string, int>
            {
                ["rare-a"] = 1,
                ["shared-special"] = 1,
                ["uncommon-a"] = 1
            }),
            Unit("legend-b", "전설 B", "전설", new Dictionary<string, int>
            {
                ["rare-a"] = 1,
                ["shared-special"] = 1,
                ["exclusive-special"] = 1
            })
        }.ToDictionary(unit => unit.Id, StringComparer.OrdinalIgnoreCase);
        var input = new StoryRewardSequenceInput
        {
            Phase = PlannerPhase.AccumulateSpecialUncommon,
            Round = 10,
            ActiveStoryStage = 4,
            CompletedStoryStage = 3,
            RewardWisps = ImmutableDictionary<string, int>.Empty,
            Inventory = [new InventoryEntry { UnitId = "rare-a", Count = 1 }],
            Units = units,
            StoryStages = [Stage(4, [Reward("e016", 1)])]
        };

        var decision = StoryRewardSequencePlanner.Evaluate(input);

        Assert.Equal("legend-a", decision.RecommendedLegendId);
        Assert.Equal(2500, decision.ExpectedUsefulUnitBp);
        Assert.Equal(["legend-a", "legend-b"], decision.RankedLegendIds.ToArray());
        Assert.Contains("공유 특별", decision.OutcomeValueSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("전용 특별", decision.OutcomeValueSummary,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CraftableLegendIsRecommendedNowToProtectStorySpeed()
    {
        var input = Input(PlannerPhase.AccumulateSpecialUncommon,
            activeStage: 4,
            rewards: [Reward("e016", 2), Reward("e017", 1)]);
        input = input with
        {
            Inventory = input.Inventory.Concat(
                [new InventoryEntry { UnitId = "ready-material", Count = 1 }]).ToArray()
        };

        var decision = StoryRewardSequencePlanner.Evaluate(input);

        Assert.Equal(StorySequenceAction.CraftLegendNow, decision.Action);
        Assert.Equal(RecommendationSequenceStage.FirstLegend, decision.Stage);
        Assert.Equal("legend-ready", decision.RecommendedLegendId);
        Assert.Contains("스토리 속도", decision.ActionSummary, StringComparison.Ordinal);
        Assert.False(decision.TopNavigationUnlocked);
    }

    [Theory]
    [InlineData("rayleigh-rare", "기타", "X50h")]
    [InlineData("base-ship", "기타", "060h")]
    [InlineData("pirate-ship", "해적선", "R30h")]
    [InlineData("seraphim", "세라핌", "3A0h")]
    [InlineData("changed", "변화된", "S50h")]
    public void FirstLegendSkipsRecipesThatNeedUnavailableEarlyMaterials(
        string materialId, string materialTier, string rawcode)
    {
        var units = new[]
        {
            Unit("rare-a", "첫 희귀", "희귀함"),
            Unit(materialId, "초반 불가 재료", materialTier, rawcodes: [rawcode]),
            Unit("ordinary-a", "일반 재료 A", "흔함"),
            Unit("ordinary-b", "일반 재료 B", "흔함"),
            Unit("blocked-legend", "초반 불가 전설", "전설",
                new Dictionary<string, int> { [materialId] = 1 }),
            Unit("eligible-legend", "초반 가능 전설", "전설",
                new Dictionary<string, int>
                {
                    ["ordinary-a"] = 1,
                    ["ordinary-b"] = 1
                })
        }.ToDictionary(unit => unit.Id, StringComparer.OrdinalIgnoreCase);
        var input = Input(PlannerPhase.AccumulateSpecialUncommon,
            activeStage: 4) with { Units = units };

        var decision = StoryRewardSequencePlanner.Evaluate(input);

        Assert.Equal("eligible-legend", decision.RecommendedLegendId);
    }

    [Fact]
    public void VanderDeckenRareRoutesToARealEarlyLegendInsteadOfRayleighRare()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var units = catalog.AllUnits.GroupBy(unit => unit.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var vanderDeckenRare = units.Values.First(unit =>
            unit.Rawcodes.Contains("T10h", StringComparer.OrdinalIgnoreCase));
        var rayleighLegend = units.Values.First(unit =>
            unit.Rawcodes.Contains("A30h", StringComparer.OrdinalIgnoreCase));
        var input = new StoryRewardSequenceInput
        {
            Phase = PlannerPhase.AccumulateSpecialUncommon,
            Round = 10,
            ActiveStoryStage = 4,
            CompletedStoryStage = 3,
            RewardWisps = ImmutableDictionary<string, int>.Empty,
            Inventory = [new InventoryEntry { UnitId = vanderDeckenRare.Id, Count = 1 }],
            Units = units,
            StoryStages = [Stage(4, [])]
        };

        var decision = StoryRewardSequencePlanner.Evaluate(input);

        Assert.False(FirstLegendRecommendationPolicy.IsEligible(rayleighLegend, units));
        Assert.NotEqual(rayleighLegend.Id, decision.RecommendedLegendId);
        Assert.Contains(vanderDeckenRare.Id,
            units[decision.RecommendedLegendId!].Recipe.Keys,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectlyOwnedRayleighRareDoesNotRelaxFirstLegendStoryContract()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var units = catalog.AllUnits.GroupBy(unit => unit.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var rayleighRare = units.Values.First(unit =>
            unit.Rawcodes.Contains("X50h", StringComparer.OrdinalIgnoreCase));
        var rayleighLegend = units.Values.First(unit =>
            unit.Rawcodes.Contains("A30h", StringComparer.OrdinalIgnoreCase));
        var input = new StoryRewardSequenceInput
        {
            Phase = PlannerPhase.AccumulateSpecialUncommon,
            Round = 10,
            ActiveStoryStage = 4,
            CompletedStoryStage = 3,
            RewardWisps = ImmutableDictionary<string, int>.Empty,
            Inventory = [new InventoryEntry { UnitId = rayleighRare.Id, Count = 1 }],
            Units = units,
            StoryStages = [Stage(4, [])]
        };

        var decision = StoryRewardSequencePlanner.Evaluate(input);

        Assert.False(FirstLegendRecommendationPolicy.IsEligible(rayleighLegend, units));
        Assert.NotEqual(rayleighLegend.Id, decision.RecommendedLegendId);
        Assert.False(decision.TopNavigationUnlocked);
        Assert.Contains("3 첫 전설", decision.StepSummary, StringComparison.Ordinal);
        Assert.Contains("5 상위+항법", decision.StepSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstLegendSurfaceShowsOnlyTheRecommendedLegendCard()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var units = catalog.AllUnits.GroupBy(unit => unit.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var vanderDeckenRare = units.Values.First(unit =>
            unit.Rawcodes.Contains("T10h", StringComparer.OrdinalIgnoreCase));
        var input = new StoryRewardSequenceInput
        {
            Phase = PlannerPhase.AccumulateSpecialUncommon,
            Round = 10,
            ActiveStoryStage = 4,
            CompletedStoryStage = 3,
            RewardWisps = ImmutableDictionary<string, int>.Empty,
            Inventory = [new InventoryEntry { UnitId = vanderDeckenRare.Id, Count = 1 }],
            Units = units,
            StoryStages = [Stage(4, [])]
        };
        var decision = StoryRewardSequencePlanner.Evaluate(input);
        var engine = new RecommendationEngine(catalog);

        var recommendations = engine.RecommendNearestCrafts(
            decision.RecommendedLegendId!, input.Inventory,
            navigationMode: "PathOfKings.BountyHunter",
            suppressSeraphim: true);

        var recommendation = Assert.Single(recommendations);
        Assert.Equal(decision.RecommendedLegendId, recommendation.Route.GoalUnitId);
        Assert.Equal("전설", units[recommendation.Route.GoalUnitId].Tier
            .Split('[', 2)[0].Trim());
        Assert.DoesNotContain(recommendations, item =>
            units[item.Route.GoalUnitId].Rawcodes.Contains(
                "W50h", StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(recommendations, item =>
            units[item.Route.GoalUnitId].Rawcodes.Contains(
                "740h", StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void RareStoryRewardsRemainVisibleUntilTheirActualResultsAreSpent()
    {
        var waiting = StoryRewardSequencePlanner.Evaluate(Input(
            PlannerPhase.AwaitMarineford,
            activeStage: 7,
            rewards: [Reward("e019", 2)]));
        var spending = StoryRewardSequencePlanner.Evaluate(Input(
            PlannerPhase.SpendRares,
            activeStage: 9,
            observedWisps: ImmutableDictionary<string, int>.Empty.Add("e019", 1)));

        Assert.Equal(StorySequenceAction.PushStoryForRareReward, waiting.Action);
        Assert.Equal(RecommendationSequenceStage.RareReward, waiting.Stage);
        Assert.Contains("희귀위습 2", waiting.ClearRewardSummary, StringComparison.Ordinal);
        Assert.False(waiting.TopNavigationUnlocked);
        Assert.Equal(StorySequenceAction.SpendRareWisps, spending.Action);
        Assert.Contains("실제 결과", spending.ActionSummary, StringComparison.Ordinal);
        Assert.False(spending.TopNavigationUnlocked);
    }

    [Fact]
    public void StageNineAfterMarinefordIsLabeledFishmanIsland()
    {
        var decision = StoryRewardSequencePlanner.Evaluate(Input(
            PlannerPhase.SpendRares, activeStage: 9));

        Assert.Equal("어인섬 · 스토리 9", decision.CurrentStoryLabel);
    }

    [Fact]
    public void SpentMarinefordRewardsUnlockTopRecommendationsBeforeCommit()
    {
        Assert.Equal(RecommendationSurface.FastRare,
            RecommendationSequencePolicy.Surface(true, PlannerPhase.AwaitFirstRare,
                ManualLatches.None));
        Assert.Equal(RecommendationSurface.StoryLegend,
            RecommendationSequencePolicy.Surface(true, PlannerPhase.AwaitMarineford,
                ManualLatches.None));
        Assert.Equal(RecommendationSurface.TopAndNavigation,
            RecommendationSequencePolicy.Surface(true, PlannerPhase.Committed,
                ManualLatches.None));

        var rewardsSpent = StoryRewardSequencePlanner.Evaluate(Input(
            PlannerPhase.SpendRares, activeStage: 9));
        Assert.Equal(RecommendationSequenceStage.TopAndNavigation, rewardsSpent.Stage);
        Assert.True(rewardsSpent.TopNavigationUnlocked);
        Assert.Equal(RecommendationSurface.TopAndNavigation,
            RecommendationSequencePolicy.Surface(rewardsSpent));

        var committed = StoryRewardSequencePlanner.Evaluate(Input(
            PlannerPhase.Committed, activeStage: 9));
        Assert.Equal(StorySequenceAction.RecommendTopAndNavigation, committed.Action);
        Assert.True(committed.TopNavigationUnlocked);
        Assert.Equal(RecommendationSurface.TopAndNavigation,
            RecommendationSequencePolicy.Surface(committed));
    }

    [Fact]
    public void PlannerLayoutShowsFiveStepProcessAndLocksLaterEvidence()
    {
        var decision = StoryRewardSequencePlanner.Evaluate(Input(
            PlannerPhase.AccumulateSpecialUncommon,
            activeStage: 4,
            rewards: [Reward("e016", 2), Reward("e017", 1)]));

        var view = RecommendationPresentation.PlannerEvidence(
            10, null, false, storySequence: decision);

        Assert.Equal("planner-sequence", view[PlannerEvidenceFieldKind.Sequence].AutomationId);
        Assert.Equal("planner-story-stage", view[PlannerEvidenceFieldKind.StoryStage].AutomationId);
        Assert.Equal("planner-story-reward", view[PlannerEvidenceFieldKind.StoryReward].AutomationId);
        Assert.Equal("planner-reward-value", view[PlannerEvidenceFieldKind.RewardValue].AutomationId);
        Assert.Equal("planner-story-decision", view[PlannerEvidenceFieldKind.StoryDecision].AutomationId);
        Assert.Contains("1 첫 희귀함", view[PlannerEvidenceFieldKind.Sequence].DisplayValue,
            StringComparison.Ordinal);
        Assert.Contains("5 상위+항법", view[PlannerEvidenceFieldKind.Sequence].DisplayValue,
            StringComparison.Ordinal);
        Assert.Equal("마지막 단계에서 공개",
            view[PlannerEvidenceFieldKind.NavigationOption].DisplayValue);
    }

    private static StoryRewardSequenceInput Input(
        PlannerPhase phase,
        int activeStage,
        StoryRewardComponent[]? rewards = null,
        ImmutableDictionary<string, int>? observedWisps = null)
    {
        var units = Units();
        return new StoryRewardSequenceInput
        {
            Phase = phase,
            Round = 10,
            ActiveStoryStage = activeStage,
            CompletedStoryStage = Math.Max(0, activeStage - 1),
            RewardWisps = observedWisps ?? ImmutableDictionary<string, int>.Empty,
            Inventory = [new InventoryEntry { UnitId = "rare-a", Count = 1 }],
            Units = units,
            StoryStages = [Stage(activeStage, rewards ?? [])]
        };
    }

    private static IReadOnlyDictionary<string, UnitDefinition> Units()
    {
        var units = new[]
        {
            Unit("rare-a", "첫 희귀", "희귀함"),
            Unit("special-a", "특별 A", "특별함"),
            Unit("special-b", "특별 B", "특별함"),
            Unit("uncommon-a", "안흔 A", "안흔함"),
            Unit("ready-material", "즉시 재료", "흔함"),
            Unit("legend-a", "전설 A", "전설", new Dictionary<string, int>
            {
                ["special-a"] = 1,
                ["uncommon-a"] = 1
            }),
            Unit("legend-ready", "즉시 전설", "전설", new Dictionary<string, int>
            {
                ["ready-material"] = 1
            })
        };
        return units.ToDictionary(unit => unit.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static UnitDefinition Unit(string id, string name, string tier,
        Dictionary<string, int>? recipe = null,
        IReadOnlyList<string>? rawcodes = null) => new()
    {
        Id = id,
        Name = name,
        Tier = tier,
        Recipe = recipe ?? [],
        Rawcodes = rawcodes?.ToList() ?? []
    };

    private static StoryStage Stage(int ordinal, StoryRewardComponent[] rewards) => new(
        ordinal, $"stage-{ordinal}", 5,
        ordinal == 9 ? "Marineford" : $"stage{ordinal}",
        rewards.Select(reward => reward.Id switch
        {
            "e016" => "special",
            "e017" => "uncommon",
            "e019" => "rare",
            _ => "unknown"
        }).ToImmutableArray(),
        new RewardComponentGroups(rewards.ToImmutableArray(), [], [], []));

    private static StoryRewardComponent Reward(string id, int count) => new(
        "Unit", id, count, 0, 1, 1, "ActivePlayer",
        [new JassSourcePin("fixture", 1, 1, 1)]);
}
