using System.Collections.Immutable;
using Xunit;
namespace OrandOverlay.Tests;

public sealed class Nika2320ReviewTests
{
    private static RecipeConditionRequirements Requirements => new("2.320", true, "AI01", 1, 5);
    private static RecipeConditionContext Context(int wood = 5)
    {
        var now = DateTimeOffset.UtcNow;
        return new("2.320", "session", 0, now, new("2.320", "session", 0, now, 1,
            new Dictionary<string, bool> { ["AI01"] = true }, wood)
            { MatchGeneration = 3, RecognitionRevision = 7 })
            { MatchGeneration = 3, RecognitionRevision = 7 };
    }
    private static UnitDefinition Unit(string id, Dictionary<string, int>? recipe = null,
        RecipeConditionRequirements? conditions = null, params string[] codes) => new()
        { Id = id, Name = "검증 유닛", Tier = "희귀함", Rawcodes = codes.ToList(), Recipe = recipe ?? [],
            RecipeConditions = conditions, CombineCommands = ["검증"] };
    private static DataCatalog Catalog(params UnitDefinition[] units)
    {
        var catalog = new DataCatalog();
        var entries = (Dictionary<string, UnitDefinition>)catalog.UnitsById;
        foreach (var unit in units) entries[unit.Id] = unit;
        return catalog;
    }
    private static Recommendation Recommendation(UnitDefinition unit) => new()
    {
        Route = new() { Id = "route:" + unit.Id, GoalUnitId = unit.Id, Name = unit.Name },
        RemainingCraftSteps = [new() { UnitId = unit.Id, Name = unit.Name, RequiredCount = 1,
            Ingredients = unit.Recipe.Select(p => new RecipeCraftIngredient
                { UnitId = p.Key, Name = p.Key, RequiredCount = p.Value }).ToList() }]
    };
    [Fact] public void KoreanNameCloneRetainsRecipeConditions()
    {
        var unit = new UnitDefinition { Id = "conditioned", Name = "Nika 니카", RecipeConditions = Requirements };
        Assert.Same(unit.RecipeConditions, Catalog(unit).Unit(unit.Id).RecipeConditions);
    }
    [Fact] public void EngineReuseExpiresObservationWithInjectedClockWithoutChangingContextNow()
    {
        var context = Context(); var clock = context.Now;
        var unit = Unit("goal", new() { ["material"] = 1 }, Requirements);
        var catalog = Catalog(unit, Unit("material"));
        catalog.Data.Routes.Add(new() { Id = "route", GoalUnitId = unit.Id, Name = unit.Name });
        var engine = new RecommendationEngine(catalog, conditionContext: context, conditionClock: () => clock);
        var inventory = new[] { new InventoryEntry { UnitId = "material", Count = 1 } };
        Assert.DoesNotContain("조합 보류", Assert.Single(engine.Recommend(unit.Id, inventory)).NextAction);
        clock = clock.AddSeconds(6);
        Assert.Contains("조합 보류", Assert.Single(engine.Recommend(unit.Id, inventory)).NextAction);
        Assert.Equal(context.Now, context.Observation!.ObservedAt);
        Assert.True(RecipeConditionEvaluator.Evaluate(Requirements, context).IsSatisfied);
    }
    [Fact] public void TreeStepConditionsUseLiveClockToo()
    {
        var context = Context(); var clock = context.Now;
        var unit = Unit("goal", new() { ["material"] = 1 }, Requirements);
        var catalog = Catalog(unit, Unit("material"));
        var builder = new RecipeTreeBuilder(catalog, null, context, () => clock);
        var inventory = new Dictionary<string, int> { ["material"] = 1 };
        var tree = builder.BuildRecipeTree(unit.Id, 1, new Dictionary<string, int>(inventory), new());
        var calc = new RecipeCompletionCalculator(catalog.Unit);
        Assert.True(Assert.Single(builder.BuildRemainingCraftSteps(tree, inventory, calc)).Conditions!.IsSatisfied);
        clock = clock.AddSeconds(6);
        Assert.False(Assert.Single(builder.BuildRemainingCraftSteps(tree, inventory, calc)).Conditions!.IsSatisfied);
    }
    [Theory] [InlineData(5, false, 1)] [InlineData(6, false, 2)] [InlineData(6, true, 2)]
    public void AllCraftsShareOneWoodBudgetAndConditionCostIsNotDoubleCharged(int wood, bool reverse, int count)
    {
        var ordinary = Unit("ordinary", new() { ["m1"] = 1, ["wood"] = 1 });
        var nika = Unit("nika", new() { ["m2"] = 1, ["wood"] = 5 }, Requirements);
        var resource = new UnitDefinition { Id = "wood", Name = "목재", Tier = "자원", Rawcodes = ["LUMBER"] };
        var catalog = Catalog(ordinary, nika, resource, Unit("m1"), Unit("m2"));
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var recommendations = new[] { Recommendation(ordinary), Recommendation(nika) };
        if (reverse) Array.Reverse(recommendations);
        var inventory = new[] { new InventoryEntry { UnitId = "m1" }, new InventoryEntry { UnitId = "m2" } };
        var context = Context(wood);
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(recommendations, inventory, conditionContext: context);
        Assert.Equal(count, steps.Count);
        if (count == 1) Assert.Equal(ordinary.Id, steps[0].TargetUnitId);
        Assert.Equal(wood, context.Observation!.Lumber);
        Assert.True(context.Observation.Tokens!["AI01"]);
        Assert.Single(new AutoCombinePlanner(catalog, hotkeys).Plan([Recommendation(ordinary)], inventory));
    }
    [Theory] [InlineData(3, 7, true)] [InlineData(4, 7, false)] [InlineData(3, 8, false)] [InlineData(0, 0, false)]
    public void CoachRequiresConditionBindingToActualFrame(long match, long revision, bool canCraft)
    {
        var unit = Unit("goal", new() { ["material"] = 1 }, Requirements);
        var catalog = Catalog(unit, Unit("material"));
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with
        {
            MatchGeneration = match, RecognitionRevision = revision, Round = 9, CompletedStoryStage = 7,
            GoalId = unit.Id, Mode = PlayMode.Normal,
            Inventory = ImmutableDictionary<string, int>.Empty.Add("material", 1),
            Recommendations = [Recommendation(unit)],
            CraftSteps = [new(unit.Id, unit.Name, "material", "재료", "", "", ["검증"])]
        };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame, Context());
        Assert.Equal(canCraft, decision.Kind == CoachActionKind.Craft);
        if (!canCraft) Assert.Equal(CoachActionKind.Waiting, decision.Kind);
    }
    [Fact] public void CoachRejectsUnboundOrForeignObservationEvenWithMatchingContext()
    {
        var unit = Unit("goal", new() { ["material"] = 1 }, Requirements);
        var catalog = Catalog(unit, Unit("material")); var context = Context();
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with
        {
            MatchGeneration = 3, RecognitionRevision = 7, Round = 9, CompletedStoryStage = 7,
            GoalId = unit.Id, Mode = PlayMode.Normal,
            Inventory = ImmutableDictionary<string, int>.Empty.Add("material", 1),
            Recommendations = [Recommendation(unit)],
            CraftSteps = [new(unit.Id, unit.Name, "material", "재료", "", "", ["검증"])]
        };
        foreach (var bad in new[] {
            context with { MatchGeneration = null, RecognitionRevision = null },
            context with { Observation = context.Observation! with { MatchGeneration = null, RecognitionRevision = null } },
            context with { Observation = context.Observation! with { RecognitionRevision = 8 } },
            context with { Observation = context.Observation! with { MatchGeneration = 4 } } })
            Assert.Equal(CoachActionKind.Waiting, new BeginnerCoachPlanner(catalog).Decide(frame, bad).Kind);
    }
    [Fact] public void ExactSiblingHeroIsReservedBeforeWildcardInEveryAllocationPath()
    {
        var a = Unit("a", null, null, "990H");
        var b = new UnitDefinition { Id = "b", Name = "두번째 영웅", Rawcodes = ["2B0H"],
            OfficialAbilities = [new() { Name = "스턴", DisplayValue = "1" }] };
        var wildcard = Unit(RecipeWildcards.AnyNika);
        var goal = Unit("goal", new() { [wildcard.Id] = 1, [a.Id] = 1 });
        var catalog = Catalog(a, b, wildcard, goal);
        var inventory = new Dictionary<string, int> { [a.Id] = 1, [b.Id] = 1 };
        var direct = RecipeWildcards.AllocateDirect(goal, inventory, catalog.Unit)!;
        var calc = new RecipeCompletionCalculator(catalog.Unit);
        var allocation = calc.CalculateAllocation([goal.Id], inventory);
        Assert.Equal(1, allocation.Progress.CompletionRatio);
        Assert.Equal(direct.OrderBy(p => p.Key), allocation.ConsumedByUnitId.OrderBy(p => p.Key));
        var remaining = new Dictionary<string, int>(inventory);
        var tree = new RecipeTreeBuilder(catalog, null).BuildRecipeTree(goal.Id, 1, remaining, new());
        Assert.All(remaining.Values, value => Assert.Equal(0, value));
        Assert.Equal(a.Id, tree.Children[0].UnitId);
        Assert.Equal(b.Id, Assert.Single(tree.Children.Single(n => n.UnitId == wildcard.Id).Children).UnitId);
        var current = new CurrentCraftPolicy(catalog.Unit, goal, inventory, new GoalStrategyProfile(1, 0), 1, 0, calc).Assess(goal);
        Assert.True(current.MaterialsReady);
        Assert.Equal(1, current.LostCoverage.Stun);
        Assert.Equal(1, inventory[a.Id]); Assert.Equal(1, inventory[b.Id]);
    }
    [Theory] [InlineData("KING")] [InlineData("UBAN")] [InlineData(" ")]
    public void UnresolvedSourceConditionsAlwaysHoldWithExplanation(string sourceCondition)
    {
        var result = RecipeConditionEvaluator.Evaluate(Requirements with { UnresolvedSourceConditions = sourceCondition }, Context());
        Assert.Equal(RecipeConditionStatus.Unknown, result.Status);
        Assert.Contains(sourceCondition, result.Reason);
        Assert.True(RecipeConditionEvaluator.Evaluate(Requirements with { UnresolvedSourceConditions = "" }, Context()).IsSatisfied);
    }
}
