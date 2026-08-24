using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CraftPresentationTests
{
    [Fact]
    public void MissingCompanionDoesNotClaimCraftIsReady()
    {
        var step = BrookStep(zoroOwned: 1, chopperOwned: 0);

        Assert.Null(RecommendationPresentation.CraftSelectUnitName(step));
        Assert.Equal("쵸파", RecommendationPresentation.CraftMissingIngredientNames(step));
        Assert.Contains("먼저 확보: 쵸파",
            RecommendationPresentation.CraftIngredientLine(step));
    }

    [Fact]
    public void ReadyBrookStepShowsZoroAsTriggerAndChopperAsCompanion()
    {
        var step = BrookStep(zoroOwned: 1, chopperOwned: 1);

        Assert.Equal("조로", RecommendationPresentation.CraftSelectUnitName(step));
        Assert.Equal("쵸파", RecommendationPresentation.CraftCompanionNames(step));
    }

    private static RecipeCraftStep BrookStep(int zoroOwned, int chopperOwned) =>
        new()
        {
            UnitId = "rawcode:D00h",
            Name = "브룩",
            Tier = "안흔함",
            RequiredCount = 4,
            OwnedCount = 0,
            Ingredients =
            [
                new RecipeCraftIngredient
                {
                    UnitId = "rawcode:200h",
                    Name = "조로",
                    Tier = "흔함",
                    RequiredCount = 1,
                    OwnedCount = zoroOwned,
                    SelectionOrder = 0
                },
                new RecipeCraftIngredient
                {
                    UnitId = "rawcode:800h",
                    Name = "쵸파",
                    Tier = "흔함",
                    RequiredCount = 1,
                    OwnedCount = chopperOwned,
                    SelectionOrder = 1
                }
            ]
        };
}
