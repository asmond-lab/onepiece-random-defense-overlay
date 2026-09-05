using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AutoStartInventoryTests
{
    [Fact]
    public void FirstRareCanSuggestGoalWithoutClearHistory()
    {
        var catalog = Catalog();
        var rare = FirstYamatoRare(catalog);

        var advice = AutoStartAdvisor.RecommendGoal(catalog, null, [rare.Id]);

        Assert.NotNull(advice);
        Assert.Equal(0, advice.Samples);
        Assert.True(AutoStartAdvisor.RequiresUnit(catalog, advice.Goal, rare.Id));
        Assert.NotEqual(DamageLane.Unknown, AdaptiveRouteEvaluator.ClassifyDamage(advice.Goal));
    }

    [Fact]
    public void ReadyUnrecordedGoalBeatsDistantRecordedGoal()
    {
        var catalog = Catalog();
        var ready = catalog.Unit("yamato_transcendent");
        var rare = FirstYamatoRare(catalog);
        var owned = ready.Recipe.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value))
            .Append(rare.Id).ToList();
        var counts = owned.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
        var calculator = new RecipeCompletionCalculator(catalog.Unit);
        var popular = catalog.AllUnits.First(unit =>
            unit.Tier.Split('[', 2)[0].Trim() is "초월" or "불멸" or "영원" or "제한됨" &&
            unit.Rawcodes.Count > 0 && unit.Recipe.Count > 0 &&
            AutoStartAdvisor.RequiresUnit(catalog, unit, rare.Id) &&
            calculator.Calculate([unit.Id], counts).CompletionRatio < 1);
        var stats = ClearBuildStats.FromSamples(Enumerable.Range(0, 20).Select(index =>
            new ClearSample($"auto-start-{index}", DateTimeOffset.UnixEpoch, "악몽", 1,
                [new ClearSampleUnit(popular.Rawcodes[0], 1, popular.Tier)])));

        var advice = AutoStartAdvisor.RecommendGoal(catalog, stats, owned);

        Assert.Equal(20, LearnedSelection.GoalSampleCount(stats, popular));
        Assert.NotNull(advice);
        Assert.Equal(1, calculator.Calculate([advice.Goal.Id], counts).CompletionRatio);
        Assert.Equal(0, advice.Samples);
        Assert.NotEqual(popular.Id, advice.Goal.Id);
    }

    private static UnitDefinition FirstYamatoRare(DataCatalog catalog) => catalog.AllUnits.First(unit =>
        unit.Tier.Split('[', 2)[0].Trim() == "희귀함" &&
        AutoStartAdvisor.RequiresUnit(catalog, catalog.Unit("yamato_transcendent"), unit.Id));

    private static DataCatalog Catalog()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        return catalog;
    }
}
