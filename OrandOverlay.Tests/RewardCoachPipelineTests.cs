using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RewardCoachPipelineTests
{
    [Fact]
    public void RealPipelineKeepsCandidatesWhileWaitingAndResumesFromFreshRewardResults()
    {
        var catalog = new DataCatalog();
        var units = (Dictionary<string, UnitDefinition>)catalog.UnitsById;
        foreach (var (id, tier) in new[] { ("rare", "희귀함"), ("common", "흔함"),
                     ("ready", "특별함"), ("reward", "특별함"), ("uncommon", "안흔함"), ("legend", "전설") })
            units[id] = new UnitDefinition { Id = id, Name = id, Tier = tier, CombineCommands = ["fixture-" + id] };
        units["ready"].Recipe.Add("common", 1);
        foreach (var id in new[] { "rare", "ready", "reward", "uncommon" }) units["legend"].Recipe.Add(id, 1);
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        Assert.True(hotkeys.HasData);
        var engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys);
        var combine = new AutoCombinePlanner(catalog, hotkeys);
        var session = new BeginnerCoachSession(catalog);
        var input = new StoryRewardSequenceInput
        {
            Phase = PlannerPhase.AccumulateSpecialUncommon, Round = 10,
            ActiveStoryStage = 4, CompletedStoryStage = 3,
            RewardWisps = ImmutableDictionary<string, int>.Empty,
            Inventory = [new InventoryEntry { UnitId = "rare", Count = 1 }, new InventoryEntry { UnitId = "common", Count = 1 }],
            Units = units,
            StoryStages = [new StoryStage(4, "stage4", 5, "stage4", [], new RewardComponentGroups(
                [new StoryRewardComponent("Unit", "e016", 2, 0, 1, 1, "ActivePlayer", []),
                 new StoryRewardComponent("Unit", "e017", 1, 0, 1, 1, "ActivePlayer", [])], [], [], []))]
        };
        var wait = Pipeline(input, 1);
        Assert.Equal(StorySequenceAction.WaitForStoryReward, wait.Story!.Action);
        Assert.NotEmpty(wait.Recommendations);
        Assert.NotEmpty(wait.CraftSteps);
        Assert.Equal(CoachActionKind.Story, session.Update(wait).Kind);
        var received = Pipeline(input with { RewardWisps = new Dictionary<string, int> { ["e016"] = 1, ["e017"] = 1 } }, 2);
        Assert.Equal(StorySequenceAction.SpendStoryWisps, received.Story!.Action);
        Assert.Equal(CoachActionKind.Reward, session.Update(received).Kind);
        Assert.Equal(CoachActionKind.Recognition, session.Update(received with { Revision = 3, IsCurrent = false }).Kind);
        var fresh = Pipeline(input with { Inventory = input.Inventory.Concat(new[]
            { new InventoryEntry { UnitId = "reward", Count = 1 }, new InventoryEntry { UnitId = "uncommon", Count = 1 } }).ToArray() }, 4);
        Assert.Equal(StorySequenceAction.CraftLegendNow, fresh.Story!.Action);
        Assert.Equal(wait.Story.RankedLegendIds.ToArray(), fresh.Story.RankedLegendIds.ToArray());
        Assert.Equal(CoachActionKind.Recognition, session.Update(fresh with { IsCurrent = false }).Kind);
        var resumed = session.Update(fresh with { Revision = 5 });
        Assert.Equal(CoachActionKind.Craft, resumed.Kind);
        Assert.False(resumed.CraftDeferredForReward);
        Assert.Equal(fresh.CraftSteps[0].TargetUnitId, resumed.TargetUnitId);

        CoachFrame Pipeline(StoryRewardSequenceInput observation, long revision)
        {
            var story = StoryRewardSequencePlanner.Evaluate(observation);
            var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
            {
                Engine = engine, Goal = units["legend"], Inventory = observation.Inventory,
                InitialSurface = RecommendationSurface.StoryLegend, StorySequence = story,
                NavigationMode = "Unselected", Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId,
                Difficulty = "신", Round = observation.Round, CompletedStoryStage = observation.CompletedStoryStage
            });
            var result = RecommendationPipeline.Finalize(candidates, catalog, units["legend"], observation.Inventory,
                new FirstRareTargetPolicy(), observation.Round, observation.CompletedStoryStage, "신");
            return BeginnerCoachPlannerTests.ReadyFrame() with
            {
                Revision = revision, Round = observation.Round, CompletedStoryStage = observation.CompletedStoryStage,
                GoalId = "legend", Difficulty = "신", Story = result.StorySequence,
                Inventory = observation.Inventory.ToImmutableDictionary(item => item.UnitId, item => item.Count),
                RewardWisps = observation.RewardWisps.ToImmutableDictionary(), Recommendations = result.Recommendations,
                CraftSteps = combine.Plan(result.Recommendations.Take(1).ToList(), observation.Inventory)
            };
        }
    }
}
