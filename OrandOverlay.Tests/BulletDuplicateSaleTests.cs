using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletDuplicateSaleTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void OnlyUnreservedDuplicateOutsideRewardHoldCanBeSold(bool reserved, bool reward, bool sale)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var inventory = ImmutableDictionary<string, int>.Empty
            .Add(BulletGuidePolicy.GoalId, 1).Add("rawcode:D20h", 1)
            .Add("rawcode:Y00h", reserved ? 1 : 2);
        var frame = new CoachFrame
        {
            MatchGeneration = 1, Revision = 1, Mode = PlayMode.Guide, GuideNumber = 1,
            Round = 50, CompletedStoryStage = 13, IsCurrent = true, Difficulty = "악몽",
            GoalId = BulletGuidePolicy.GoalId, Inventory = inventory,
            CommittedCraftUnitId = reserved ? "rawcode:Y00h" : null,
            RewardWisps = reward ? ImmutableDictionary<string, int>.Empty.Add("e019", 1) :
                ImmutableDictionary<string, int>.Empty,
            ConfirmedNavigation = BulletGuidePolicy.NavigationId,
            GuidePlan = new BulletGuidePolicy(catalog).Plan(50, 13, inventory, "악몽")
        };
        var result = new BeginnerCoachPlanner(catalog).Decide(frame);
        if (sale)
        {
            Assert.Equal(CoachActionKind.Economy, result.Kind);
            Assert.Equal("rawcode:Y00h", result.TargetUnitId);
        }
        else Assert.NotEqual(CoachActionKind.Economy, result.Kind);
    }
}
