using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AdaptiveRouteEvaluatorScoringTests
{
    [Fact]
    public void Evaluate_RoundsGoalPackageAffinityAndBaseAwayFromZero()
    {
        var units = Units(
            Unit("physical", "초월 [물딜]", Recipe(("g", 3))),
            Unit("g"),
            Unit("required", recipe: Recipe(("p", 2))),
            Unit("p"),
            Unit("affinity", recipe: Recipe(("a", 3))),
            Unit("a"));
        var input = Input(units,
            Inventory(("g", 2), ("p", 1), ("a", 1)),
            Candidate("physical", required: ["required"], optional: ["affinity"]));

        var score = new AdaptiveRouteEvaluator().Evaluate(input).Selected!;

        Assert.Equal(6667, score.GoalBp);
        Assert.Equal(5000, score.RecipePackageBp);
        Assert.Equal(5000, score.PackageBp);
        Assert.Equal(3333, score.AffinityBp);
        Assert.Equal(5667, score.BaseBp);
    }

    [Fact]
    public void Evaluate_AllocatesGoalThenRequiredThenAffinityWithoutDoubleSpend()
    {
        var units = Units(
            Unit("physical", "초월 [물딜]", Recipe(("shared", 1))),
            Unit("required", recipe: Recipe(("shared", 1))),
            Unit("affinity", recipe: Recipe(("shared", 1))),
            Unit("shared"));
        var input = Input(units, Inventory(("shared", 1)),
            Candidate("physical", required: ["required"], optional: ["affinity"]));

        var score = new AdaptiveRouteEvaluator().Evaluate(input).Selected!;

        Assert.Equal(10000, score.GoalBp);
        Assert.Equal(0, score.RecipePackageBp);
        Assert.Equal(0, score.AffinityBp);
        Assert.Equal(5500, score.BaseBp);
        Assert.Equal(1, score.ConsumedInventory["shared"]);
    }

    [Fact]
    public void Evaluate_ReversePrunesRedundantStrategySupports()
    {
        var units = Units(
            Unit("physical", "초월 [물딜]", abilities: [Ability("바제스", "가능")]),
            Unit("stun-a", abilities: CompleteStrategyAbilities()),
            Unit("stun-b", abilities: CompleteStrategyAbilities()));
        var input = Input(units, Inventory(),
            Candidate("physical", supportPool: ["stun-b", "stun-a"]));

        var score = new AdaptiveRouteEvaluator().Evaluate(input).Selected!;

        Assert.Single(score.SynthesizedSupportUnitIds);
        Assert.Equal("stun-a", score.SynthesizedSupportUnitIds[0]);
        Assert.Equal(10000, score.StrategyPackageBp);
    }

    [Fact]
    public void Evaluate_ChargesExactNonrecursiveSharedMaterialOpportunityLoss()
    {
        var units = Units(
            Unit("a", "초월 [물딜]", Recipe(("shared", 1))),
            Unit("b", "초월 [물딜]", Recipe(("shared", 1))),
            Unit("shared"));
        var input = Input(units, Inventory(("shared", 1)), Candidate("a"), Candidate("b"));

        var result = new AdaptiveRouteEvaluator().Evaluate(input);
        var a = Assert.Single(result.PhysicalCandidates, score => score.GoalUnitId == "a");

        Assert.Equal(1100, a.OpportunityPenaltyBp);
        Assert.Equal(7400, a.FinalLowerBp);
        Assert.Equal(a.FinalLowerBp, a.FinalUpperBp);
    }

    [Fact]
    public void Evaluate_CapsAbandonmentAndPreservesAssignmentInterval()
    {
        var units = Units(
            Unit("physical", "초월 [물딜]"),
            Unit("attached", "전설"),
            Unit("abandoned", "전설", Recipe(("x", 4))),
            Unit("x"), Unit("affinity"));
        var input = Input(units,
            Inventory(("physical", 1), ("attached", 1), ("affinity", 1)),
            Candidate("physical", required: ["attached"], optional: ["affinity"])) with
        {
            FirstObservedLegendIds = ImmutableArray.Create("attached", "abandoned")
        };

        var score = new AdaptiveRouteEvaluator().Evaluate(input).Selected!;

        Assert.Equal(0, score.AbandonmentLowerBp);
        Assert.Equal(2500, score.AbandonmentUpperBp);
        Assert.Equal(7500, score.FinalLowerBp);
        Assert.Equal(10000, score.FinalUpperBp);
    }

    [Fact]
    public void Evaluate_UsesBestLegalLaneRouteRatherThanCandidateCountSum()
    {
        var units = Units(
            Unit("p1", "초월 [물딜]", Recipe(("p", 1))),
            Unit("p2", "초월 [물딜]", Recipe(("p2leaf", 1))),
            Unit("m", "초월 [마딜]", Recipe(("mleaf", 1)), CompleteMagicAbilities()),
            Unit("p"), Unit("p2leaf"), Unit("mleaf"));
        var input = Input(units, Inventory(("p", 1), ("p2leaf", 1), ("mleaf", 1)),
            Candidate("p1"), Candidate("p2"), Candidate("m"));

        var result = new AdaptiveRouteEvaluator().Evaluate(input);

        Assert.Equal(8500, result.BestPhysical!.FinalLowerBp);
        Assert.Equal(8500, result.BestMagic!.FinalLowerBp);
        Assert.Equal("m", result.Selected!.GoalUnitId);
    }

    private static AdaptiveRouteEvaluationInput Input(
        IReadOnlyDictionary<string, UnitDefinition> units,
        IReadOnlyDictionary<string, int> inventory,
        params AdaptiveRouteCandidate[] candidates) => new()
        {
            Units = units,
            Inventory = inventory,
            Candidates = candidates.ToImmutableArray(),
            IsAutomatic = false
        };

    private static AdaptiveRouteCandidate Candidate(string goal,
        string[]? required = null, string[]? supportPool = null, string[]? optional = null) => new()
        {
            GoalUnitId = goal,
            VariantId = "default",
            ExplicitRequiredRootIds = (required ?? []).ToImmutableArray(),
            StrategySupportPoolIds = (supportPool ?? []).ToImmutableArray(),
            OptionalAffinityRootIds = (optional ?? []).ToImmutableArray()
        };

    private static IReadOnlyDictionary<string, UnitDefinition> Units(params UnitDefinition[] units) =>
        units.ToDictionary(unit => unit.Id, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, int> Inventory(params (string Id, int Count)[] values) =>
        values.ToDictionary(value => value.Id, value => value.Count, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, int> Recipe(params (string Id, int Count)[] values) =>
        values.ToDictionary(value => value.Id, value => value.Count, StringComparer.OrdinalIgnoreCase);

    private static UnitAbilityDisplay Ability(string name, string value) =>
        new() { Name = name, DisplayValue = value };

    private static List<UnitAbilityDisplay> CompleteStrategyAbilities() =>
    [
        Ability("보스 잡기", "1"),
        Ability("광폭화 잡기", "1"),
        Ability("이동속도 감소", "102"),
        Ability("스턴", "1.4"),
        Ability("방어력 감소", "211"),
        Ability("공중이동", "가능")
    ];

    private static List<UnitAbilityDisplay> CompleteMagicAbilities() =>
    [
        Ability("보스 잡기", "1"),
        Ability("광폭화 잡기", "2"),
        Ability("이동속도 감소", "102"),
        Ability("스턴", "1.4"),
        Ability("마법방어력 감소", "1"),
        Ability("공중이동", "가능")
    ];

    private static UnitDefinition Unit(string id, string tier = "일반",
        Dictionary<string, int>? recipe = null, List<UnitAbilityDisplay>? abilities = null) => new()
        {
            Id = id,
            Name = id,
            Tier = tier,
            Recipe = recipe ?? [],
            OfficialAbilities = abilities ?? []
        };
}
