using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideMirrorTests
{
    [Fact]
    public void ProductionPlannerUsesMirrorOnlyAfterSharedSafetyGates()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var planner = new BeginnerCoachPlanner(catalog);
        var frame = Frame() with { ConfirmedNavigation = BulletGuidePolicy.NavigationId };
        Assert.Equal("guide1:mirror-caesar", planner.Decide(frame).Id);
        Assert.Equal(CoachActionKind.Reward, planner.Decide(frame with
            { RewardWisps = frame.RewardWisps.Add("e019", 1) }).Kind);
        Assert.Equal(CoachActionKind.Recognition, planner.Decide(frame with { IsCurrent = false }).Kind);
    }

    [Theory]
    [InlineData("R50h")]
    [InlineData("H50h")]
    [InlineData("O50h")]
    [InlineData("Q50h")]
    public void PreparedMirrorRespectsObservedMissionSpecialException(string special)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var inventory = Frame().Inventory.Add("rawcode:" + special, 1)
            .Add("rawcode:HA0h", 1).Add("rawcode:MC0h", 1);
        var plan = new BulletGuidePolicy(catalog).Plan(25, 10, inventory, "악몽");
        var frame = Frame() with { Inventory = inventory, GuidePlan = plan,
            ConfirmedNavigation = BulletGuidePolicy.NavigationId };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.NotEqual("guide1:mirror-caesar", decision.Id);
        Assert.Contains("브륄레를 무리하게 소모하지", decision.OperationGuide);
        Assert.Equal(1, frame.Inventory["rawcode:S80h"]);
    }

    [Fact]
    public void RequiresOwnedBruleeActualLegendTargetAndTraitAfterPunkHazard()
    {
        var frame = Frame();
        var decision = BulletGuideMirrorPolicy.Decide(frame);
        Assert.NotNull(decision);
        Assert.Equal("rawcode:830h", decision.TargetUnitId);
        Assert.Equal("rawcode:S80h", decision.ConsumedUnitId);
        Assert.Null(BulletGuideMirrorPolicy.Decide(frame with { CompletedStoryStage = 9 }));
        Assert.Null(BulletGuideMirrorPolicy.Decide(frame with { IsCurrent = false }));
        Assert.Null(BulletGuideMirrorPolicy.Decide(frame with { Paused = true }));
        Assert.Null(BulletGuideMirrorPolicy.Decide(frame with { Signals = frame.Signals.SetItem("trait-points", 0) }));
        Assert.Null(BulletGuideMirrorPolicy.Decide(frame with { CombatObservations = [] }));
        Assert.Null(BulletGuideMirrorPolicy.Decide(frame with
        {
            CombatObservations = frame.CombatObservations.SetItem(1,
                frame.CombatObservations[1] with { LegendMarked = false })
        }));
        Assert.Null(BulletGuideMirrorPolicy.Decide(frame with
        {
            CombatObservations = frame.CombatObservations.SetItem(1,
                frame.CombatObservations[1] with { Owner = 1 })
        }));
        Assert.Null(BulletGuideMirrorPolicy.Decide(frame with
        {
            CombatObservations = frame.CombatObservations.SetItem(0,
                frame.CombatObservations[0] with { MirrorAbility = new("A114", 1, 5) })
        }));
    }

    private static CoachFrame Frame() => new()
    {
        Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
        Round = 25, CompletedStoryStage = 10, IsCurrent = true, Difficulty = "악몽",
        Inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:S80h", 1),
        GuidePlan = new(BulletGuideStage.AirFoundation, "rawcode:930h", false) { AirCount = 1 },
        Signals = ImmutableDictionary<string, long?>.Empty.Add("trait-points", 1),
        CombatObservations =
        [
            new(1, "h08S", 0, null, CombatUnitKind.LocalUnit, new(0, 0), 100, 100, 0, false, false)
                { MirrorAbility = new("A114", 1, 0) },
            new(2, "h038", 7, null, CombatUnitKind.RecipeExemplar, new(100, 0), 100, 100, 0, false, false)
                { LegendMarked = true }
        ]
    };
}
