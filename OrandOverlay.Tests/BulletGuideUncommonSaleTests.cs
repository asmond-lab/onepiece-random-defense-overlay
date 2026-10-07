using System.Collections.Immutable;
using Xunit;
namespace OrandOverlay.Tests;
public sealed class BulletGuideUncommonSaleTests
{
    internal static DataCatalog Catalog() { var c = new DataCatalog(); c.Load(loadCarryPolicy: false); return c; }
    internal static CoachFrame Frame(string rawcode = "h00A", string id = "rawcode:A00h")
    {
        var unit = new CombatUnitState(1, rawcode, 0, null, CombatUnitKind.LocalUnit, null, 100, 100, 0, false, false)
            { UncommonSaleAbility = new("A0B8", 1, 0) };
        return new() { MatchGeneration = 1, Revision = 1, Round = 50, CompletedStoryStage = 13,
            Mode = PlayMode.Guide, GuideNumber = 1, IsCurrent = true, Difficulty = "악몽",
            ConfirmedNavigation = BulletGuidePolicy.NavigationId,
            Inventory = ImmutableDictionary<string,int>.Empty.Add(BulletGuidePolicy.GoalId, 1).Add(id, 1),
            GuidePlan = new(BulletGuideStage.Operating, null, true), CombatObservations = [unit] };
    }
    internal static string[] OperatingSupportCodes() =>
        new[] { "180h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Y30h", "Q30h", "K50h" }
            .Concat(Enumerable.Repeat("300h", 20)).ToArray();
    internal static CoachFrame PlannedFrame(string[] codes, CombatUnitState? unit = null,
        GoroseiMode gorosei = GoroseiMode.None)
    {
        var catalog = Catalog();
        var inventory = codes.GroupBy(code => code, StringComparer.Ordinal)
            .ToDictionary(group => catalog.AllUnits.First(item => item.Rawcodes.Contains(group.Key)).Id,
                group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var plan = new BulletGuidePolicy(catalog).Plan(50, 13, inventory, "악몽",
            BulletGuidePolicy.NavigationId, gorosei);
        return new()
        {
            MatchGeneration = 1, Revision = 1, Round = 50, CompletedStoryStage = 13,
            Mode = PlayMode.Guide, GuideNumber = 1, IsCurrent = true, Difficulty = "악몽",
            GoalId = BulletGuidePolicy.GoalId, ConfirmedNavigation = BulletGuidePolicy.NavigationId,
            Inventory = inventory.ToImmutableDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
            GuidePlan = plan, CombatObservations = unit is null ? [] : [unit]
        };
    }
    [Theory]
    [InlineData("round")][InlineData("bullet")][InlineData("owned")][InlineData("stale")]
    [InlineData("pause")][InlineData("fail")][InlineData("clear")][InlineData("difficulty")]
    [InlineData("reward")][InlineData("ability")][InlineData("cooldown")][InlineData("foreign")]
    [InlineData("dead")][InlineData("consumed")][InlineData("special")][InlineData("unlisted")]
    public void UnsafeOrUnknownSaleNeverReachesProduction(string blocked)
    {
        var f = Frame(); var u = f.CombatObservations[0];
        f = blocked switch {
            "round" => f with { Round = 49 }, "bullet" => f with { Inventory = f.Inventory.Remove(BulletGuidePolicy.GoalId) },
            "owned" => f with { GuidePlan = f.GuidePlan! with { OwnedBullet = false } },
            "stale" => f with { IsCurrent = false }, "pause" => f with { Paused = true },
            "fail" => f with { Outcome = "fail" }, "clear" => f with { Outcome = "clear" },
            "difficulty" => f with { Difficulty = "unknown" },
            "reward" => f with { RewardWisps = f.RewardWisps.Add("e019",1) },
            "ability" => f with { CombatObservations = [u with { UncommonSaleAbility = null }] },
            "cooldown" => f with { CombatObservations = [u with { UncommonSaleAbility = new("A0B8",1,null) }] },
            "foreign" => f with { CombatObservations = [u with { Kind = CombatUnitKind.RecipeExemplar, Owner = 7 }] },
            "dead" => f with { CombatObservations = [u with { Life = 0 }] },
            "consumed" => f with { Inventory = f.Inventory.Remove("rawcode:A00h") },
            "special" => Frame("h00B","rawcode:B00h"), "unlisted" => Frame("h00H","rawcode:H00h"), _ => f };
        Assert.DoesNotContain("sell-uncommon",new BeginnerCoachPlanner(Catalog()).Decide(f).Id);
    }
    [Theory]
    [InlineData("target")][InlineData("selected")][InlineData("committed")][InlineData("protected")]
    public void ReservationsCannotBeSold(string reservation)
    {
        var f = Frame();
        f = reservation switch {
            "target" => f with { GuidePlan = f.GuidePlan! with { TargetUnitId = "rawcode:A00h" } },
            "selected" => f with { SelectedGoalIds = ["rawcode:A00h"] },
            "committed" => f with { CommittedCraftUnitId = "rawcode:A00h" },
            _ => f with { GuidePlan = f.GuidePlan! with { ProtectedUnitIds = ["rawcode:A00h"] } } };
        Assert.Null(BulletGuideUncommonSalePolicy.Decide(f,Catalog()));
    }
    [Fact]
    public void ProtectedPackageReservesItsUncommonIngredients()
    {
        var f=Frame("h00K","rawcode:K00h");
        f=f with { GuidePlan=f.GuidePlan! with { ProtectedUnitIds=["rawcode:Q30h"] } };
        Assert.Null(BulletGuideUncommonSalePolicy.Decide(f,Catalog()));
    }
    [Fact]
    public void NativeCooldownIsNotClaimedAsCompleteButtonEligibility()
    {
        var catalog=Catalog();
        var sale=BulletGuideUncommonSalePolicy.Decide(Frame(),catalog)!;
        var ship=BulletGuideAncientShipPolicy.Decide(BulletGuideAncientShipTests.Frame(),catalog)!;
        Assert.Contains("사용 가능",sale.Controls);
        Assert.Contains("사용 가능",ship.Controls);
        Assert.Equal(sale.UnknownSignals, ship.UnknownSignals);
        Assert.Equal("guide1:sell-uncommon:rawcode:A00h", sale.Id);
        Assert.Equal("guide1:ancient-to-pirate", ship.Id);
        Assert.Equal("rawcode:A00h", sale.ConsumedUnitId);
        Assert.Equal("rawcode:Y50h", ship.ConsumedUnitId);
    }
    [Fact]
    public void OwnedBulletRound50UnallocatedUncommonGetsOneSale()
    {
        var frame = Frame();
        var decision = new BeginnerCoachPlanner(Catalog()).Decide(frame);
        Assert.Equal("guide1:sell-uncommon:rawcode:A00h", decision.Id);
        Assert.Equal("rawcode:A00h", decision.ConsumedUnitId);
        Assert.Contains("50%", decision.Reason);
        Assert.Contains("20%", decision.Reason);
        Assert.Equal(1, frame.Inventory["rawcode:A00h"]);
    }
    [Fact]
    public void LivePlanBossSupportReservesUsoppAndDoesNotSell()
    {
        var unit = Frame("h00N", "rawcode:N00h").CombatObservations[0];
        var frame = PlannedFrame(["180h", "N00h"], unit);
        Assert.Equal(BulletGuideStage.BossSupport, frame.GuidePlan!.Stage);
        Assert.Equal("rawcode:U30h", frame.GuidePlan.TargetUnitId);
        var remaining = new RecipeCompletionCalculator(Catalog().Unit)
            .CalculateAllocation(new[] { frame.GoalId, frame.GuidePlan.TargetUnitId }
                .OfType<string>().Concat(frame.GuidePlan.ProtectedUnitIds)
                .Distinct(StringComparer.OrdinalIgnoreCase), frame.Inventory)
            .RemainingInventory;
        Assert.True(remaining.GetValueOrDefault("rawcode:N00h") <= 0);
        Assert.DoesNotContain("sell-uncommon", new BeginnerCoachPlanner(Catalog()).Decide(frame).Id);
    }
    [Fact]
    public void LivePlanOperatingUnallocatedFukuroGetsOneSale()
    {
        var frame = PlannedFrame(OperatingSupportCodes().Concat(["A00h"]).ToArray(),
            Frame().CombatObservations[0]);
        Assert.Equal(BulletGuideStage.Operating, frame.GuidePlan!.Stage);
        Assert.Null(frame.GuidePlan.TargetUnitId);
        var remaining = new RecipeCompletionCalculator(Catalog().Unit)
            .CalculateAllocation(new[] { frame.GoalId, frame.GuidePlan.TargetUnitId }
                .OfType<string>().Concat(frame.GuidePlan.ProtectedUnitIds)
                .Distinct(StringComparer.OrdinalIgnoreCase), frame.Inventory)
            .RemainingInventory;
        Assert.True(remaining.GetValueOrDefault("rawcode:A00h") > 0);
        var decision = new BeginnerCoachPlanner(Catalog()).Decide(frame);
        Assert.Equal("guide1:sell-uncommon:rawcode:A00h", decision.Id);
        Assert.Equal("rawcode:A00h", decision.ConsumedUnitId);
    }
    [Fact]
    public void LivePlanWarcuryReadyUnallocatedFukuroGetsOneSale()
    {
        var frame = PlannedFrame(OperatingSupportCodes().Concat(["830h", "A00h"]).ToArray(),
            Frame().CombatObservations[0], GoroseiMode.Warcury);
        Assert.Equal(BulletGuideStage.Operating, frame.GuidePlan!.Stage);
        Assert.Null(frame.GuidePlan.TargetUnitId);
        Assert.Equal(120, frame.GuidePlan.Support!.ArmorTarget);
        Assert.True(frame.GuidePlan.Support.IsReady);
        var remaining = new RecipeCompletionCalculator(Catalog().Unit)
            .CalculateAllocation(new[] { frame.GoalId, frame.GuidePlan.TargetUnitId }
                .OfType<string>().Concat(frame.GuidePlan.ProtectedUnitIds)
                .Distinct(StringComparer.OrdinalIgnoreCase), frame.Inventory)
            .RemainingInventory;
        Assert.True(remaining.GetValueOrDefault("rawcode:A00h") > 0);
        Assert.Equal("guide1:sell-uncommon:rawcode:A00h",
            new BeginnerCoachPlanner(Catalog()).Decide(frame).Id);
    }
}
