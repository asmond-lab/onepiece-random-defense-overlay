using System.Collections.Concurrent;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecommendationMatrixInvariantTests
{
    private static readonly HashSet<string> TopTiers =
    [
        "초월", "불멸", "영원", "제한됨", "신비함", "해적왕"
    ];

    [Fact]
    public void EveryGoalNavigationAndOwnershipStateKeepsBoardContracts()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var stats = ClearBuildStats.Load(
            [Path.Combine(AppContext.BaseDirectory, "Data", "tmo-clear-samples.json")]);
        var goals = catalog.AllUnits
            .Where(unit => TopTiers.Contains(BaseTier(unit.Tier)))
            .Where(unit => unit.Rawcodes.Count > 0)
            .DistinctBy(unit => unit.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var cases = (
            from goal in goals
            from navigation in NavigationProfiles.Options
            from ownsGoal in new[] { false, true }
            select (goal, navigation, ownsGoal)
        ).ToList();
        var failures = new ConcurrentBag<string>();

        Parallel.ForEach(cases,
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            item =>
            {
                var (goal, navigation, ownsGoal) = item;
                IReadOnlyList<InventoryEntry> inventory = ownsGoal
                    ?
                    [
                        new InventoryEntry { UnitId = goal.Id },
                        new InventoryEntry { UnitId = "item_greenblood" },
                        new InventoryEntry { UnitId = "rawcode:060h" },
                        new InventoryEntry { UnitId = "rawcode:Y50h" }
                    ]
                    : [];
                var engine = new RecommendationEngine(catalog, stats);
                var first = engine.RecommendNearestCrafts(
                    goal.Id, inventory, take: 8,
                    navigationMode: navigation.Id);
                var second = engine.RecommendNearestCrafts(
                    goal.Id, inventory, take: 8,
                    navigationMode: navigation.Id);
                var label =
                    $"{goal.Id}/{navigation.Id}/{(ownsGoal ? "owned" : "empty")}";

                var activeSupportCount = first.Count(row =>
                    row.ClusterParentUnitId is null &&
                    !row.Route.GoalUnitId.Equals(
                        goal.Id, StringComparison.OrdinalIgnoreCase));
                Check(activeSupportCount <= 12,
                    $"{label}: 활성 지원 보드 {activeSupportCount}칸");
                Check(first.Select(row => row.Route.GoalUnitId)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count() == first.Count,
                    $"{label}: 중복 추천");
                Check(first.Select(row => row.Route.GoalUnitId)
                        .SequenceEqual(second.Select(row => row.Route.GoalUnitId),
                            StringComparer.OrdinalIgnoreCase),
                    $"{label}: 비결정적 순서");
                if (ownsGoal)
                    Check(first.All(row => !row.Route.GoalUnitId.Equals(
                            goal.Id, StringComparison.OrdinalIgnoreCase)),
                        $"{label}: 보유 목표 재추천");

                var topCount = first.Count(row =>
                    TopTiers.Contains(BaseTier(
                        catalog.Unit(row.Route.GoalUnitId).Tier)));
                if (navigation.TopUnitLimit == 0)
                    Check(topCount == 0,
                        $"{label}: 상위 금지인데 {topCount}기 추천");
                else if (navigation.TopUnitLimit == 1)
                    Check(topCount <= (ownsGoal ? 0 : 1),
                        $"{label}: 1상위 제한인데 {topCount}기 추천");

                var seraphimCount = first
                    .Where(row => row.ClusterParentUnitId is null)
                    .Where(row => !row.Route.GoalUnitId.Equals(
                        goal.Id, StringComparison.OrdinalIgnoreCase))
                    .Count(row =>
                        BaseTier(catalog.Unit(row.Route.GoalUnitId).Tier) ==
                        "세라핌");
                Check(seraphimCount <= 1,
                    $"{label}: 세라핌 {seraphimCount}기 추천");

                foreach (var shipRawcode in new[] { "060h", "Y50h" })
                {
                    var shipDemand = first
                        .Where(row => row.ClusterParentUnitId is null)
                        .Where(row => !row.Route.GoalUnitId.Equals(
                            goal.Id, StringComparison.OrdinalIgnoreCase))
                        .Sum(row => IngredientDemand(
                            catalog, row.Route.GoalUnitId, shipRawcode,
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
                    var ownedShips = inventory
                        .Where(entry => entry.UnitId.Equals(
                            "rawcode:" + shipRawcode,
                            StringComparison.OrdinalIgnoreCase))
                        .Sum(entry => entry.Count);
                    Check(shipDemand <= ownedShips,
                        $"{label}: {shipRawcode} 배 {ownedShips}척에 {shipDemand}척 소비");
                }

                var expectedScope = navigation.AllowsMultipleTopUnits
                    ? TopScope.MultiTop
                    : navigation.CanCraftTopUnits
                        ? TopScope.SoloTop
                        : TopScope.Any;
                var activeProfile = stats.GoalProfile(goal.Rawcodes, expectedScope);
                if (activeProfile is not null)
                    Check(first.Where(row => row.ClearEvidence is not null)
                            .All(row => row.ClearEvidence!.Scope == activeProfile.Scope),
                        $"{label}: 클리어 근거 범위 불일치");

                var goalPhysical = goal.Tier.Contains("[물딜]",
                    StringComparison.Ordinal);
                var goalMagic = goal.Tier.Contains("[마딜]",
                    StringComparison.Ordinal);
                foreach (var row in first.Where(
                             row => row.ClusterParentUnitId is null))
                {
                    var candidate = catalog.Unit(row.Route.GoalUnitId);
                    if (goalPhysical)
                        Check(!candidate.Tier.Contains("[마딜]",
                                StringComparison.Ordinal),
                            $"{label}: 물딜에 마딜 {candidate.Name}");
                    if (goalMagic && TopTiers.Contains(BaseTier(candidate.Tier)))
                        Check(!candidate.Tier.Contains("[물딜]",
                                StringComparison.Ordinal),
                            $"{label}: 마딜에 물딜 상위 {candidate.Name}");
                }

                void Check(bool condition, string message)
                {
                    if (!condition) failures.Add(message);
                }
            });

        Assert.True(failures.IsEmpty,
            string.Join(Environment.NewLine,
                failures.Order(StringComparer.Ordinal).Take(200)));
    }

    private static string BaseTier(string tier) =>
        tier.Split('[', 2)[0].Trim();

    private static int IngredientDemand(DataCatalog catalog, string unitId,
        string rawcode, ISet<string> visiting)
    {
        var unit = catalog.Unit(unitId);
        if (!visiting.Add(unit.Id)) return 0;
        var total = 0;
        foreach (var (ingredientId, count) in unit.Recipe)
        {
            var ingredient = catalog.Unit(ingredientId);
            total += ingredient.Rawcodes.Contains(rawcode, StringComparer.Ordinal)
                ? count
                : count * IngredientDemand(catalog, ingredient.Id, rawcode, visiting);
        }
        visiting.Remove(unit.Id);
        return total;
    }
}
