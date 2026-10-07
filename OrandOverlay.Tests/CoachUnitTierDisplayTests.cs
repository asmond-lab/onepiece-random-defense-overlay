using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CoachUnitTierDisplayTests
{
    private readonly DataCatalog _catalog = new();
    public CoachUnitTierDisplayTests() => _catalog.Load(loadCarryPolicy: false);

    [Theory]
    [InlineData("L50h", "희귀함")]
    [InlineData("M30h", "히든 [물딜]")]
    public void GuideGatherTitleUsesActualTargetCatalogTierWithoutHover(string rawcode, string tier)
    {
        var target = _catalog.Unit("rawcode:" + rawcode);
        Assert.Equal("사보", target.Name);
        Assert.Equal(tier, target.Tier);
        var frame = Frame(target);
        var decision = new BeginnerCoachPlanner(_catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Gather, decision.Kind);
        Assert.Equal(target.Id, decision.TargetUnitId);
        Assert.Contains(target.Name, decision.Title);
        Assert.Contains(tier, decision.Title);
        Assert.Equal("사보", _catalog.Unit(target.Id).Name);
    }

    [Theory]
    [InlineData("L50h")]
    [InlineData("M30h")]
    public void GuideCraftUsesCatalogIdentityInsteadOfPossiblyUntieredActionLabel(string rawcode)
    {
        var target = _catalog.Unit("rawcode:" + rawcode);
        var trigger = _catalog.Unit(target.Recipe.Keys.First());
        var action = new AutoCombineStep(target.Id, target.Name, trigger.Id, trigger.Name,
            trigger.Rawcodes.First(), "X", []);
        var frame = Frame(target) with
        {
            Round = 19,
            Inventory = target.Recipe.ToImmutableDictionary(p => p.Key, p => p.Value * 2)
                .Add("rawcode:180h", 1),
            CraftSteps = [action]
        };
        Assert.True(BulletGuideCraftSafety.Allows(_catalog, target.Id, frame.Inventory,
            frame.Round, frame.ConfirmedNavigation));
        var decision = new BeginnerCoachPlanner(_catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Craft, decision.Kind);
        Assert.Equal(target.Id, decision.TargetUnitId);
        Assert.Contains(target.Name, decision.Title);
        Assert.Contains(target.Tier, decision.Title);
        Assert.Equal(target.Id, decision.CraftRecipe!.UnitId);
        Assert.Equal(target.Tier, decision.CraftRecipe.Tier);
        Assert.Contains(trigger.Name, decision.Controls);
        Assert.Contains(action.Key, decision.Controls);
        Assert.Equal("사보", action.TargetName);
    }

    [Fact]
    public void GatherMaterialsDisambiguateBothSaboVariantsFromCatalogNotStaleLeafLabels()
    {
        var target = _catalog.Unit("rawcode:M30h");
        var frame = Frame(target) with { Recommendations = [new Recommendation
        {
            Route = new RouteDefinition { Id = "materials", Name = "materials fixture", GoalUnitId = target.Id },
            RecipeProgress = new RecipeProgress { Leaves = [
                new RecipeLeafProgress { UnitId = "rawcode:L50h", Name = "사보", RequiredCount = 1 },
                new RecipeLeafProgress { UnitId = "rawcode:M30h", Name = "사보", RequiredCount = 1 } ] }
        }] };
        var decision = new BeginnerCoachPlanner(_catalog).Decide(frame);
        Assert.Contains(_catalog.Unit("rawcode:L50h").Tier, decision.Controls);
        Assert.Contains(_catalog.Unit("rawcode:M30h").Tier, decision.Controls);
    }

    [Theory]
    [InlineData("사보", null, "사보")]
    [InlineData("사보", "", "사보")]
    [InlineData("사보", "  ", "사보")]
    [InlineData("사보 [희귀함]", "희귀함", "사보 [희귀함]")]
    [InlineData("사보 - 희귀함", "희귀함", "사보 [희귀함]")]
    [InlineData("사보 [히든 [물딜]]", "히든 [물딜]", "사보 [히든 [물딜]]")]
    [InlineData("히든 이름", "히든", "히든 이름 [히든]")]
    public void CoachLabelPreservesExactTierAndOnlyDeduplicatesExplicitMatchingSuffix(
        string name, string? tier, string expected) =>
        Assert.Equal(expected, RecommendationPresentation.CoachUnitName(name, tier));

    [Theory]
    [InlineData("L50h")]
    [InlineData("M30h")]
    public void GuideStaleCraftReservationHoldStillIdentifiesTargetTier(string rawcode)
    {
        var target = _catalog.Unit("rawcode:" + rawcode);
        var frame = Frame(target) with { CraftSteps = [new AutoCombineStep(
            target.Id, target.Name, "rawcode:X00h", "unused fixture trigger", "X00h", "X", [])] };
        var decision = new BeginnerCoachPlanner(_catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Waiting, decision.Kind);
        Assert.Contains(target.Name, decision.Title);
        Assert.Contains(target.Tier, decision.Title);
        Assert.StartsWith("guide1:reservation:", decision.Id);
    }

    [Fact]
    public void BothSurfacesConsumeSameProjectedTitleAndExactTierRecipeFormatter_SourceContract()
    {
        // Source wiring only: no WPF window, game process or live memory is touched.
        var root = Path.GetDirectoryName(SourceFile())!;
        string Source(string name) => File.ReadAllText(Path.Combine(root, "..", name));
        Assert.Contains("MainCoachView.Render(decision, frame,", Source("MainWindow.Coach.cs"));
        Assert.Contains("CoachView.Render(decision, frame, unit)", Source("OverlayWindow.Coach.cs"));
        var view = Source("BeginnerCoachView.xaml.cs");
        Assert.Contains("Set(ActionText, CoachPresentation.ActionTitle(decision, unit))", view);
        Assert.Contains("Set(ControlsText, decision.Controls)", view);
        Assert.Contains("RecommendationPresentation.CoachUnitName(item.Name, item.Tier)", view);
        Assert.DoesNotContain("RecommendationPresentation.CraftUnitName(item.Name, item.Tier)", view);
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private CoachFrame Frame(UnitDefinition target) => new()
    {
        Mode = PlayMode.Guide, GuideNumber = 1,
        GuidePlan = new(BulletGuideStage.ControlSupport, target.Id, true) { Round = 55 },
        MatchGeneration = 1, Revision = 1, RecognitionRevision = 1,
        Round = 55, CompletedStoryStage = 13, IsCurrent = true, Difficulty = "악몽",
        ConfirmedNavigation = BulletGuidePolicy.NavigationId,
        Inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:180h", 1),
        Recommendations = [new Recommendation
        {
            Route = new RouteDefinition { Id = "tier-display", Name = "tier display fixture", GoalUnitId = target.Id },
            RecipeProgress = new RecipeCompletionCalculator(_catalog.Unit).Calculate(
                [target.Id], ImmutableDictionary<string, int>.Empty)
        }]
    };
}
