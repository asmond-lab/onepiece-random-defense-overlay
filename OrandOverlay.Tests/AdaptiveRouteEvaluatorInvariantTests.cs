using System.Collections.Immutable;
using System.Globalization;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AdaptiveRouteEvaluatorInvariantTests
{
    [Fact]
    public void Evaluate_RetainsAtMostSixCandidatesPerLaneByPreRank()
    {
        var units = Enumerable.Range(0, 8)
            .Select(index => Unit($"p{index}", "초월 [물딜]", Recipe(($"l{index}", 1))))
            .Concat(Enumerable.Range(0, 8).Select(index => Unit($"l{index}")))
            .ToDictionary(unit => unit.Id, StringComparer.OrdinalIgnoreCase);
        var inventory = Enumerable.Range(0, 7)
            .ToDictionary(index => $"l{index}", _ => 1, StringComparer.OrdinalIgnoreCase);
        var input = Input(units, inventory,
            Enumerable.Range(0, 8).Select(index => Candidate($"p{index}")).ToArray());

        var result = new AdaptiveRouteEvaluator().Evaluate(input);

        Assert.Equal(6, result.PhysicalCandidates.Length);
        Assert.DoesNotContain(result.PhysicalCandidates, score => score.GoalUnitId == "p7");
    }

    [Fact]
    public void Evaluate_UnknownAndHybridDamageCannotAutomaticallyWin()
    {
        var units = Units(
            Unit("unknown", "초월"),
            Unit("hybrid", "초월 [물딜] [마딜]"),
            Unit("physical", "초월 [물딜]", Recipe(("missing", 1))),
            Unit("missing"));
        var input = Input(units, Inventory(),
            Candidate("unknown"), Candidate("hybrid"), Candidate("physical"));

        var result = new AdaptiveRouteEvaluator().Evaluate(input);

        Assert.Equal("physical", result.Selected!.GoalUnitId);
        Assert.Equal(DamageLane.Unknown, AdaptiveRouteEvaluator.ClassifyDamage(units["hybrid"]));
        Assert.Equal(DamageLane.Unknown, AdaptiveRouteEvaluator.ClassifyDamage(units["unknown"]));
    }

    [Fact]
    public void Evaluate_OppositeDamageRequiredTopHardBlocksRoute()
    {
        var units = Units(
            Unit("physical", "초월 [물딜]"),
            Unit("magic-top", "초월 [마딜]"));
        var input = Input(units, Inventory(),
            Candidate("physical", required: ["magic-top"]));

        var result = new AdaptiveRouteEvaluator().Evaluate(input);

        Assert.Null(result.Selected);
        Assert.True(result.PhysicalCandidates[0].HasHardBlocker);
    }

    [Fact]
    public void Evaluate_UsesAuthorityConfidenceFallbackAndMinimum()
    {
        var units = Units(Unit("physical", "초월 [물딜]"));
        var input = Input(units, Inventory(), Candidate("physical") with
        {
            RecognitionConfidenceBp = 7000,
            DamageConfidenceBp = 9000,
            RecipeConfidenceBp = 8000
        });

        var automatic = input with { IsAutomatic = true };
        var score = new AdaptiveRouteEvaluator().Evaluate(automatic).PhysicalCandidates[0];

        Assert.Equal(10000, score.EvidenceConfidenceBp);
        Assert.Equal(7000, score.ConfidenceBp);
        Assert.Null(new AdaptiveRouteEvaluator().Evaluate(automatic).Selected);
    }

    [Fact]
    public void Evaluate_OrdinalTieIsCultureOrderAndCatalogInvariant()
    {
        var baseUnits = Units(
            Unit("a", "초월 [물딜]"),
            Unit("z", "초월 [마딜]"));
        var expandedUnits = new Dictionary<string, UnitDefinition>(baseUnits,
            StringComparer.OrdinalIgnoreCase) { ["irrelevant"] = Unit("irrelevant") };
        var first = Input(baseUnits, Inventory(), Candidate("z"), Candidate("a"));
        var second = Input(expandedUnits, Inventory(), Candidate("a"), Candidate("z"));
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ko-KR");
            var korean = new AdaptiveRouteEvaluator().Evaluate(first).Selected!.GoalUnitId;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkish = new AdaptiveRouteEvaluator().Evaluate(second).Selected!.GoalUnitId;

            Assert.Equal("a", korean);
            Assert.Equal(korean, turkish);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Evaluate_MagicAttachedPackageBeatsNearerCoreIncompletePhysicalRoute()
    {
        var units = Units(
            Unit("physical", "초월 [물딜]", Recipe(("owned", 1))),
            Unit("physical-core", recipe: Recipe(("missing", 3))),
            Unit("magic", "초월 [마딜]", Recipe(("m", 2)), CompleteMagicAbilities()),
            Unit("magic-core"), Unit("owned"), Unit("missing"), Unit("m"));
        var input = Input(units, Inventory(("owned", 1), ("m", 1), ("magic-core", 1)),
            Candidate("physical", required: ["physical-core"]),
            Candidate("magic", required: ["magic-core"]));

        var result = new AdaptiveRouteEvaluator().Evaluate(input);

        Assert.Equal("magic", result.Selected!.GoalUnitId);
        Assert.Equal(DamageLane.Magic, result.Selected.Lane);
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

    private static AdaptiveRouteCandidate Candidate(string goal, string[]? required = null) => new()
    {
        GoalUnitId = goal,
        VariantId = "default",
        ExplicitRequiredRootIds = (required ?? []).ToImmutableArray()
    };

    private static IReadOnlyDictionary<string, UnitDefinition> Units(params UnitDefinition[] units) =>
        units.ToDictionary(unit => unit.Id, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, int> Inventory(params (string Id, int Count)[] values) =>
        values.ToDictionary(value => value.Id, value => value.Count, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, int> Recipe(params (string Id, int Count)[] values) =>
        values.ToDictionary(value => value.Id, value => value.Count, StringComparer.OrdinalIgnoreCase);

    private static UnitDefinition Unit(string id, string tier = "일반",
        Dictionary<string, int>? recipe = null, List<UnitAbilityDisplay>? abilities = null) => new()
        {
            Id = id,
            Name = id,
            Tier = tier,
            Recipe = recipe ?? [],
            OfficialAbilities = abilities ?? []
        };

    private static UnitAbilityDisplay Ability(string name, string value) =>
        new() { Name = name, DisplayValue = value };

    private static List<UnitAbilityDisplay> CompleteMagicAbilities() =>
    [
        Ability("보스 잡기", "1"),
        Ability("광폭화 잡기", "2"),
        Ability("이동속도 감소", "102"),
        Ability("스턴", "1.4"),
        Ability("마법방어력 감소", "1"),
        Ability("공중이동", "가능")
    ];
}
