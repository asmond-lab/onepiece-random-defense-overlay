using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecommendationUrgencyPolicyTests
{
    private readonly DataCatalog _catalog;

    public RecommendationUrgencyPolicyTests()
    {
        _catalog = new DataCatalog();
        _catalog.Load(loadCarryPolicy: false);
    }

    [Fact]
    public void Before_survival_round_preserves_existing_order()
    {
        var goal = BossGoal();
        var source = Recommendations(goal, Neutral(), BossSupport());

        var result = RecommendationUrgencyPolicy.Apply(
            _catalog, goal, [], source, round: 29, completedStoryStage: 0,
            difficulty: "악몽");

        Assert.Equal(RecommendationUrgency.None, result.Urgency);
        Assert.Equal(source.Select(item => item.Route.Id),
            result.Recommendations.Select(item => item.Route.Id));
    }

    [Fact]
    public void Boss_survival_reorders_support_without_displacing_primary_goal()
    {
        var goal = BossGoal();
        var neutral = Neutral();
        var boss = BossSupport();

        var result = RecommendationUrgencyPolicy.Apply(
            _catalog, goal, [], Recommendations(goal, neutral, boss),
            round: 30, completedStoryStage: 13, difficulty: "악몽");

        Assert.Equal(RecommendationUrgency.BossSurvival, result.Urgency);
        Assert.Equal([goal.Id, boss.Id, neutral.Id],
            result.Recommendations.Select(item => item.Route.GoalUnitId));
        Assert.Contains(result.Reason!, result.Recommendations[0].Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Story_deadline_reorders_damage_support_for_physical_and_magic_goals(
        bool magic)
    {
        var goal = GoalForFamily(magic);
        var neutral = Neutral();
        var damage = StoryDamageSupport();

        var result = RecommendationUrgencyPolicy.Apply(
            _catalog, goal, BossReadyInventory(),
            Recommendations(goal, neutral, damage),
            round: 30, completedStoryStage: 12, difficulty: "악몽");

        Assert.Equal(RecommendationUrgency.StoryDeadline, result.Urgency);
        Assert.Equal([goal.Id, damage.Id, neutral.Id],
            result.Recommendations.Select(item => item.Route.GoalUnitId));
        Assert.Contains("35라운드", result.Reason, StringComparison.Ordinal);
        Assert.Contains("13단계", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Completed_story_and_ready_boss_preserve_order()
    {
        var goal = GoalForFamily(magic: false);
        var source = Recommendations(goal, Neutral(), StoryDamageSupport());

        var result = RecommendationUrgencyPolicy.Apply(
            _catalog, goal, BossReadyInventory(), source,
            round: 35, completedStoryStage: 13,
            difficulty: "악몽");

        Assert.Equal(RecommendationUrgency.None, result.Urgency);
        Assert.Equal(source.Select(item => item.Route.Id),
            result.Recommendations.Select(item => item.Route.Id));
    }

    [Theory]
    [InlineData("쉬움")]
    [InlineData("보통")]
    [InlineData("어려움")]
    [InlineData("지옥")]
    [InlineData("신")]
    [InlineData("unknown")]
    public void OtherDifficultiesDoNotInheritNightmareStoryDeadline(string difficulty)
    {
        var goal = GoalForFamily(magic: false);
        var source = Recommendations(goal, Neutral(), StoryDamageSupport());
        var result = RecommendationUrgencyPolicy.Apply(_catalog, goal, BossReadyInventory(),
            source, round: 34, completedStoryStage: 12, difficulty: difficulty);
        Assert.Equal(RecommendationUrgency.None, result.Urgency);
        Assert.Equal(source.Select(item => item.Route.Id), result.Recommendations.Select(item => item.Route.Id));
        Assert.Null(result.Reason);
    }

    private UnitDefinition BossGoal() => _catalog.AllUnits.First(unit =>
        (GoalStrategyCalculator.StrategyProfileFor(unit)?.BossControlTarget ?? 0) > 0 ||
        (GoalStrategyCalculator.StrategyProfileFor(unit)?.BerserkBossControlTarget ?? 0) > 0);

    private UnitDefinition GoalForFamily(bool magic) => _catalog.AllUnits.First(unit =>
        GoalStrategyCalculator.IsMagicDamageTier(unit.Tier) == magic &&
        TopGradePolicy.IsTopGrade(unit.Tier));

    private IReadOnlyList<InventoryEntry> BossReadyInventory()
    {
        var boss = _catalog.AllUnits.First(unit =>
            GoalStrategyCalculator.StrategyMetricsFor(unit).BossControl > 0);
        var berserk = _catalog.AllUnits.First(unit =>
            GoalStrategyCalculator.StrategyMetricsFor(unit).BerserkBossControl > 0);
        return
        [
            new InventoryEntry { UnitId = boss.Id, Count = 100 },
            new InventoryEntry { UnitId = berserk.Id, Count = 100 }
        ];
    }

    private UnitDefinition BossSupport() => _catalog.AllUnits.First(unit =>
    {
        var metrics = GoalStrategyCalculator.StrategyMetricsFor(unit);
        return metrics.BossControl > 0 || metrics.BerserkBossControl > 0;
    });

    private UnitDefinition StoryDamageSupport() => _catalog.AllUnits.First(unit =>
    {
        var metrics = GoalStrategyCalculator.StrategyMetricsFor(unit);
        return metrics.SingleDamage > 0 || metrics.FinisherDamage > 0;
    });

    private UnitDefinition Neutral() => _catalog.AllUnits.First(unit =>
    {
        var metrics = GoalStrategyCalculator.StrategyMetricsFor(unit);
        return metrics == new StrategyMetrics() && !TopGradePolicy.IsTopGrade(unit.Tier);
    });

    private static IReadOnlyList<Recommendation> Recommendations(
        params UnitDefinition[] units) => units.Select((unit, index) => new Recommendation
    {
        Route = new RouteDefinition
        {
            Id = $"route-{index}-{unit.Id}",
            GoalUnitId = unit.Id,
            Name = unit.Name
        },
        Score = units.Length - index
    }).ToList();
}
