using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GuideCraftProgressTests
{
    [Fact]
    public void MaterialsCompleteStillCountsIntermediateAndFinalCrafts()
    {
        var frame = new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, IsCurrent = true,
            MatchGeneration = 1, Revision = 1, Round = 19, CompletedStoryStage = 8,
            Inventory = ImmutableDictionary<string, int>.Empty,
            GuidePlan = new(BulletGuideStage.FirstLegend, "legend", false),
            Recommendations = [new Recommendation
            {
                Route = new() { Id = "route", Name = "route", GoalUnitId = "legend" },
                RecipeTree = new() { UnitId = "legend", Name = "목표 전설", RequiredCount = 1 },
                RemainingCraftSteps =
                [
                    new() { UnitId = "a", Name = "중간 재료 A", RequiredCount = 2, OwnedCount = 0 },
                    new() { UnitId = "b", Name = "중간 재료 B", RequiredCount = 2, OwnedCount = 1 }
                ]
            }]
        };
        var decision = new CoachDecision(CoachActionKind.Craft, "craft", "", "", "", "", "")
            { MaterialCompletion = 1 };
        var display = CoachPresentation.Create(decision, frame);
        Assert.Equal(4, display.RemainingGuideCrafts);
        Assert.Equal("목표 전설", display.GuideCraftTargetName);
        var acquired = CoachPresentation.Create(decision, frame with
        {
            Inventory = ImmutableDictionary<string, int>.Empty.Add("legend", 1)
        });
        Assert.Equal(0, acquired.RemainingGuideCrafts);
        Assert.Null(CoachPresentation.Create(decision, frame with { IsCurrent = false }).RemainingGuideCrafts);
        Assert.Null(CoachPresentation.Create(decision, frame with { Mode = PlayMode.Normal }).RemainingGuideCrafts);
    }
}
