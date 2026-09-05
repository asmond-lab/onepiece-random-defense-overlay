using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AdaptiveRouteHistoryIndependenceTests
{
    [Fact]
    public void VerifiedAutomaticRouteDoesNotRequireAnyRecordedClear()
    {
        var goal = Unit("goal", "초월 [물딜]", "GOAL");
        var input = new AdaptiveRouteEvaluationInput
        {
            Units = new Dictionary<string, UnitDefinition> { [goal.Id] = goal },
            Inventory = new Dictionary<string, int>(),
            Candidates = [new AdaptiveRouteCandidate { GoalUnitId = goal.Id, VariantId = "auto" }],
            IsAutomatic = true,
            ClearStats = ClearBuildStats.Empty
        };

        var result = new AdaptiveRouteEvaluator().Evaluate(input);

        Assert.NotNull(result.Selected);
        Assert.Equal(10000, result.Selected.EvidenceConfidenceBp);
        Assert.Equal(0, result.Selected.AffinityBp);
    }

    [Fact]
    public void RecordedCoreDoesNotHideReadyUnrecordedOptionalSupport()
    {
        var goal = Unit("goal", "초월 [물딜]", "GOAL");
        var ready = Unit("ready", "전설", "NEW1", new() { ["leaf"] = 1 });
        var recorded = Unit("recorded", "전설", "OLD1", new() { ["missing"] = 1 });
        var units = new[] { goal, ready, recorded, Unit("leaf"), Unit("missing") }
            .ToDictionary(unit => unit.Id);
        var stats = ClearBuildStats.FromSamples(Enumerable.Range(0, 20).Select(index =>
            new ClearSample($"affinity-{index}", DateTimeOffset.UnixEpoch, "악몽", 2,
            [new ClearSampleUnit("GOAL", 1, goal.Tier), new ClearSampleUnit("OLD1", 1, recorded.Tier)])));
        var input = new AdaptiveRouteEvaluationInput
        {
            Units = units, Inventory = new Dictionary<string, int> { ["leaf"] = 1 },
            Candidates = [], ClearStats = stats
        };

        var affinity = AdaptiveRouteAffinityCalculator.Score(input, goal, ["ready"], [],
            new Dictionary<string, long> { ["leaf"] = 1 },
            new RecipeCompletionCalculator(id => units[id]));

        Assert.Equal(10000, affinity.Bp);
    }

    private static UnitDefinition Unit(string id, string tier = "흔함", string? code = null,
        Dictionary<string, int>? recipe = null) => new()
    {
        Id = id, Name = id, Tier = tier, Rawcodes = code is null ? [] : [code],
        Recipe = recipe ?? []
    };
}
