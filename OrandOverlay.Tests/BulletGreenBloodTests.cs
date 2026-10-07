using Xunit;
using System.Collections.Immutable;

namespace OrandOverlay.Tests;

public sealed class BulletGreenBloodTests
{
    [Theory]
    [InlineData("rawcode:630h", "rawcode:B30h", "rawcode:630h")]
    [InlineData("rawcode:B30h", "rawcode:U30h", "rawcode:B30h")]
    [InlineData("rawcode:U30h", "rawcode:540h", "rawcode:U30h")]
    [InlineData("rawcode:540h", "rawcode:300h", "rawcode:540h")]
    public void OtherGoroseiUsesFirstOwnedGuideRecipient(string first, string second, string expected)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var entries = new[] { first, second, "item_greenblood" }
            .Select(id => new InventoryEntry { UnitId = id, Count = 1 });
        var result = new GreenBloodAdvisor(catalog).EvaluateBulletGuide(
            entries, 8, GoroseiMode.None, false, "악몽");
        Assert.Equal(expected, Assert.Single(result).UnitId);
    }

    [Theory]
    [InlineData(true, "rawcode:3A0h", true)]
    [InlineData(false, "rawcode:Z20h", false)]
    public void MihawkRequiresEstablishedStunPair(bool pair, string expected, bool seraphim)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var entries = new[] { "rawcode:340h", "rawcode:Z20h", "item_greenblood" }
            .Select(id => new InventoryEntry { UnitId = id, Count = 1 });
        var result = Assert.Single(new GreenBloodAdvisor(catalog).EvaluateBulletGuide(
            entries, 8, GoroseiMode.Nasjuro, pair, "악몽"));
        Assert.Equal(expected, result.UnitId);
        Assert.Equal(seraphim, result.Seraphim);
    }

    [Fact]
    public void GuideUsesObservedGreenBloodInsteadOfReturningGenericHold()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var inventory = ImmutableDictionary<string, int>.Empty
            .Add("rawcode:180h", 1).Add("rawcode:Z20h", 1).Add("item_greenblood", 1);
        var frame = new CoachFrame
        {
            MatchGeneration = 1, Revision = 1,
            Mode = PlayMode.Guide, GuideNumber = 1,
            GuidePlan = new BulletGuidePolicy(catalog).Plan(50, 13, inventory, "악몽"),
            Round = 50, CompletedStoryStage = 13, IsCurrent = true,
            Difficulty = "악몽", GoalId = BulletGuidePolicy.GoalId,
            ConfirmedNavigation = BulletGuidePolicy.NavigationId,
            Inventory = inventory, GreenBloodAvailable = true,
            GreenBlood = new GreenBloodAdvisor(catalog).EvaluateBulletGuide(
                inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }),
                13, GoroseiMode.Nasjuro, false, "악몽")
        };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Item, decision.Kind);
        Assert.Equal("rawcode:Z20h", decision.TargetUnitId);
    }

    [Fact]
    public void NasjuroSelectsOwnedBartolomeoBeforeDragon()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var inventory = new[]
        {
            new InventoryEntry { UnitId = "rawcode:Z20h", Count = 1 },
            new InventoryEntry { UnitId = "rawcode:W20h", Count = 1 },
            new InventoryEntry { UnitId = "item_greenblood", Count = 1 }
        };
        var advisor = new GreenBloodAdvisor(catalog);
        var result = advisor.EvaluateBulletGuide(inventory, 8,
            GoroseiMode.Nasjuro, false, "악몽");
        Assert.Equal("rawcode:Z20h", result[0].UnitId);
    }

    [Theory]
    [InlineData(7, true, "악몽")]
    [InlineData(8, false, "악몽")]
    [InlineData(8, true, "unknown")]
    public void UnconfirmedItemOrIneligibleContextDoesNotOfferUse(int story, bool item, string difficulty)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var inventory = new List<InventoryEntry>
        {
            new() { UnitId = "rawcode:Z20h", Count = 1 }
        };
        if (item) inventory.Add(new() { UnitId = "item_greenblood", Count = 1 });
        Assert.Empty(new GreenBloodAdvisor(catalog).EvaluateBulletGuide(
            inventory, story, GoroseiMode.Nasjuro, false, difficulty));
    }
}
