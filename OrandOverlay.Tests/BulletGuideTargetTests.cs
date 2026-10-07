using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideTargetTests
{
    [Fact]
    public void ProductionPlannerPreservesRewardAndRecognitionGatesBeforeTargetAdvice()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var planner = new BeginnerCoachPlanner(catalog);
        var frame = Frame() with { ConfirmedNavigation = BulletGuidePolicy.NavigationId };
        Assert.Equal("guide1:line-target:3", planner.Decide(frame).Id);
        Assert.Equal(CoachActionKind.Reward, planner.Decide(frame with
            { RewardWisps = frame.RewardWisps.Add("e019", 1) }).Kind);
        Assert.Equal(CoachActionKind.Recognition, planner.Decide(frame with { IsCurrent = false }).Kind);
    }

    [Fact]
    public void PrefersHighLifeMarkedLocalLaneTargetWithinHakiRange()
    {
        var frame = Frame();
        var decision = BulletGuideTargetPolicy.Decide(frame);
        Assert.NotNull(decision);
        Assert.Equal("guide1:line-target:3", decision.Id);
        Assert.Null(BulletGuideTargetPolicy.Decide(frame with { CombatObservations = [] }));
        Assert.Null(BulletGuideTargetPolicy.Decide(frame with { IsCurrent = false }));
        Assert.Null(BulletGuideTargetPolicy.Decide(frame with { Paused = true }));
        Assert.Null(BulletGuideTargetPolicy.Decide(frame with
            { Inventory = ImmutableDictionary<string, int>.Empty }));
    }

    [Fact]
    public void DoesNotUseFreshForeignDeadOrOutOfRangeTargets()
    {
        var frame = Frame();
        var bullet = frame.CombatObservations[0];
        var candidate = frame.CombatObservations[2];
        foreach (var invalid in new[]
        {
            candidate with { ArmorBreakStacks = 0 },
            candidate with { LaneSlot = 1 },
            candidate with { Life = 0 },
            candidate with { Position = new(401, 0) },
            candidate with { Kind = CombatUnitKind.SharedBoss }
        })
            Assert.Null(BulletGuideTargetPolicy.Decide(frame with { CombatObservations = [bullet, invalid] }));
    }

    private static CoachFrame Frame() => new()
    {
        Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
        Round = 60, CompletedStoryStage = 13, IsCurrent = true, Difficulty = "악몽",
        Inventory = ImmutableDictionary<string, int>.Empty.Add(BulletGuidePolicy.GoalId, 1),
        GuidePlan = new(BulletGuideStage.Operating, null, true),
        CombatObservations =
        [
            new(1, "h081", 0, null, CombatUnitKind.Bullet, new(0, 0), 100, 100, 0, false, false),
            new(2, "o020", 6, 0, CombatUnitKind.LaneMonster, new(100, 0), 1000, 2000, 75, false, false),
            new(3, "o020", 6, 0, CombatUnitKind.LaneMonster, new(200, 0), 1500, 2000, 30, false, false),
            new(4, "o020", 6, 0, CombatUnitKind.LaneMonster, new(300, 0), 2000, 2000, 0, false, false)
        ]
    };
}
