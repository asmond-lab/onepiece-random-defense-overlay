using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BeginnerCoachReplayTests
{
    [Fact]
    public void IdenticalHandAndRewardTraceProducesIdenticalActionsThroughExplicitOutcome()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("yamato_transcendent");
        var samples = Enumerable.Range(0, 12).Select(index => new ClearSample(
            $"replay-{index}", DateTimeOffset.UnixEpoch, "악몽", 1,
            [new ClearSampleUnit(goal.Rawcodes[0], 1, goal.Tier)]));
        var stats = ClearBuildStats.FromSamples(samples);
        var inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:S20h", 1);
        var initial = new CoachFrame
        {
            MatchGeneration = 1, Revision = 1, Round = 0, CompletedStoryStage = 0,
            IsCurrent = false, Difficulty = "악몽", Inventory = ImmutableDictionary<string, int>.Empty
        };
        var frames = new[]
        {
            initial,
            initial with { Revision = 2, IsCurrent = true, Round = 9, CompletedStoryStage = 7, Inventory = inventory,
                Story = new StoryRewardSequenceDecision(RecommendationSequenceStage.RareReward,
                    StorySequenceAction.SpendRareWisps, "", "", "", "", "", null, null, 0, false),
                RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e019", 1) },
            initial with { Revision = 3, IsCurrent = true, Round = 20, CompletedStoryStage = 9, Inventory = inventory },
            initial with { Revision = 4, IsCurrent = true, Round = 21, CompletedStoryStage = 9, Inventory = inventory,
                SuggestedNavigation = "PathOfKings.BountyHunter" },
            initial with { Revision = 5, IsCurrent = true, Round = 22, CompletedStoryStage = 9, Inventory = inventory,
                ConfirmedNavigation = "PathOfKings.BountyHunter" },
            initial with { Revision = 6, Round = 22, CompletedStoryStage = 9, Inventory = inventory },
            initial with { Revision = 7, IsCurrent = true, Round = 34, CompletedStoryStage = 12, Inventory = inventory,
                ConfirmedNavigation = "PathOfKings.BountyHunter" },
            initial with { Revision = 8, IsCurrent = true, Round = 65, CompletedStoryStage = 14,
                Inventory = inventory.Add(goal.Id, 1), ConfirmedNavigation = "PathOfKings.BountyHunter", Outcome = "clear" }
        };
        var first = Replay();
        Assert.Equal(first.Select(item => item.Id), Replay().Select(item => item.Id));
        Assert.Equal(CoachActionKind.Waiting, first[0].Kind);
        Assert.Equal(CoachActionKind.Reward, first[1].Kind);
        Assert.Equal(CoachActionKind.Navigation, first[3].Kind);
        Assert.Equal(CoachActionKind.Recognition, first[5].Kind);
        Assert.Equal(CoachActionKind.Story, first[6].Kind);
        Assert.Equal(CoachActionKind.Finished, first[7].Kind);
        Assert.Null(first[7].TargetUnitId);

        CoachDecision[] Replay()
        {
            var policy = new BeginnerGoalPolicy(catalog, stats, initial.Difficulty);
            var session = new BeginnerCoachSession(catalog);
            var decisions = frames.Select(frame =>
            {
                var selected = frame.IsCurrent && frame.Round >= 20
                    ? policy.Select(frame.Inventory.Select(pair =>
                        new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray())
                    : null;
                return session.Update(frame with { GoalId = selected?.Id ?? policy.CommittedGoalId });
            }).ToArray();
            Assert.Equal(goal.Id, policy.CommittedGoalId);
            return decisions;
        }
    }

    [Fact]
    public void Round65WithoutExplicitOutcomeNeverClaimsClear()
    {
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with
            { Round = 65, Outcome = "unknown", CraftSteps = [] };
        Assert.NotEqual(CoachActionKind.Finished, new BeginnerCoachPlanner(new DataCatalog()).Decide(frame).Kind);
    }
}
