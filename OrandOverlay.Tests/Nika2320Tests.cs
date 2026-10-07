using Xunit;
namespace OrandOverlay.Tests;

public sealed class Nika2320Tests
{
    private static DataCatalog Catalog(string version = "2.320") { var c = new DataCatalog(); c.Load(false, version); return c; }
    private static RecipeConditionContext Context(int? flag = 1, bool? token = true, int? wood = 5)
    {
        var now = DateTimeOffset.UtcNow;
        return new("2.320", "test-session", 0, now, new("2.320", "test-session", 0, now,
            flag, token.HasValue ? new Dictionary<string, bool> { ["AI01"] = token.Value } : null, wood));
    }
    [Fact] public void CatalogKeepsLegacyAndOverridesBothNikaVariantsOnlyWhenSelected()
    {
        var old = Catalog("2.314"); var current = Catalog();
        Assert.Equal("2.320", current.MapVersion);
        foreach (var code in new[] { "KB0H", "KB0H_" })
        {
            Assert.Null(old.Unit("rawcode:" + code).RecipeConditions);
            var nika = current.Unit("rawcode:" + code);
            Assert.NotNull(nika.RecipeConditions);
            Assert.Equal(1, nika.Recipe[RecipeWildcards.AnyNika]);
            Assert.DoesNotContain("rawcode:700I", nika.Recipe.Keys);
            Assert.Equal(new[] { "태양의신", "nika et" }, nika.CombineCommands);
        }
        Assert.Equal(old.Unit("rawcode:180h").Recipe, current.Unit("rawcode:180h").Recipe);
        Assert.Equal("2.314", old.MapVersion);
    }
    [Theory] [InlineData(0, false)] [InlineData(1, true)] [InlineData(2, true)]
    public void LiteralNonzeroFlagIsRequired(int flag, bool expected) =>
        Assert.Equal(expected, RecipeConditionEvaluator.Evaluate(Catalog().Unit("rawcode:KB0H"), Context(flag)).IsSatisfied);
    [Fact] public void UnknownStaleAndForeignConditionsFailClosed()
    {
        var nika = Catalog().Unit("rawcode:KB0H"); var c = Context();
        Assert.False(RecipeConditionEvaluator.Evaluate(nika).IsSatisfied);
        foreach (var bad in new[] { c with { OwnerId = 1 }, c with { SessionId = "other" },
            c with { MapVersion = "2.314" }, c with { Now = c.Now.AddMinutes(1) }, Context(null), Context(token: null), Context(wood: null) })
            Assert.Equal(RecipeConditionStatus.Unknown, RecipeConditionEvaluator.Evaluate(nika, bad).Status);
        Assert.False(RecipeConditionEvaluator.Evaluate(nika, Context(token: false)).IsSatisfied);
        Assert.False(RecipeConditionEvaluator.Evaluate(nika, Context(wood: 4)).IsSatisfied);
        Assert.False(RecipeConditionEvaluator.Evaluate(nika.RecipeConditions! with { TokenCount = 2 }, c).IsSatisfied);
    }
    [Theory] [InlineData("990H")] [InlineData("2B0H")]
    public void RealAlternativeAllocatesButFakeWildcardDoesNot(string hero)
    {
        var c = Catalog(); var calc = new RecipeCompletionCalculator(c.Unit);
        var nika = c.Unit("rawcode:KB0H");
        var inv = nika.Recipe.Where(p => p.Key != RecipeWildcards.AnyNika).ToDictionary(p => p.Key, p => p.Value);
        inv["rawcode:" + hero] = 1;
        var allocation = calc.CalculateAllocation([nika.Id], inv);
        Assert.Equal(1, allocation.Progress.CompletionRatio);
        Assert.Equal(1, allocation.ConsumedByUnitId["rawcode:" + hero]);
        var both = calc.CalculateAllocation([nika.Id, "rawcode:KB0H_"], inv);
        Assert.Equal(1, both.ConsumedByUnitId["rawcode:" + hero]);
        Assert.True(both.Progress.CompletionRatio < 1);
        var treeInventory = new Dictionary<string, int>(inv);
        var tree = new RecipeTreeBuilder(c, null).BuildRecipeTree(nika.Id, 1, treeInventory, new());
        Assert.Equal(0, treeInventory["rawcode:" + hero]);
        Assert.Equal(1, tree.Children.Single(x => x.UnitId == RecipeWildcards.AnyNika).OwnedCount);
        Assert.NotNull(RecipeWildcards.AllocateDirect(nika, inv, c.Unit));
        inv.Remove("rawcode:" + hero); inv[RecipeWildcards.AnyNika] = 99; inv["rawcode:700I"] = 99;
        Assert.Null(RecipeWildcards.AllocateDirect(nika, inv, c.Unit));
        Assert.True(calc.Calculate([nika.Id], inv).CompletionRatio < 1);
    }
    [Fact] public void CurrentCraftNeverTreatsMaterialsOrPhysicalItemAsConditionProof()
    {
        var c = Catalog(); var nika = c.Unit("rawcode:KB0H");
        var inv = nika.Recipe.Where(p => p.Key != RecipeWildcards.AnyNika).ToDictionary(p => p.Key, p => p.Value);
        inv["rawcode:990H"] = 1; inv["rawcode:700I"] = 9;
        CurrentCraftAssessment Assess(RecipeConditionContext? ctx) => new CurrentCraftPolicy(c.Unit, nika, inv,
            new GoalStrategyProfile(0, 0), 1, 0, new RecipeCompletionCalculator(c.Unit), ctx).Assess(nika);
        Assert.False(Assess(null).MaterialsReady);
        Assert.False(Assess(Context(0)).MaterialsReady);
        Assert.True(Assess(Context()).MaterialsReady);
        Assert.Equal(9, inv["rawcode:700I"]);
    }
    [Fact] public void DirectAllocationReservesExplicitHeroBeforeWildcard()
    {
        var hero = new UnitDefinition { Id = "hero", Name = "hero", Rawcodes = ["990H"] };
        var wildcard = new UnitDefinition { Id = RecipeWildcards.AnyNika, Name = "either" };
        var goal = new UnitDefinition { Id = "goal", Name = "goal", Recipe = new() { [wildcard.Id] = 1, [hero.Id] = 1 } };
        var units = new[] { hero, wildcard, goal }.ToDictionary(x => x.Id);
        Assert.Null(RecipeWildcards.AllocateDirect(goal, new Dictionary<string, int> { [hero.Id] = 1, [wildcard.Id] = 100 }, id => units[id]));
        Assert.Equal(2, RecipeWildcards.AllocateDirect(goal, new Dictionary<string, int> { [hero.Id] = 2 }, id => units[id])![hero.Id]);
    }
}
