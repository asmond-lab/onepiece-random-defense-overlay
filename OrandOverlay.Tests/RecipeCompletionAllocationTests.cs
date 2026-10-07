using System.Text.Json;
using System.Collections.Generic;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecipeCompletionAllocationTests
{
    [Fact]
    public void Calculate_ExpandsNestedRecipeAndCountsOwnedLeaves()
    {
        var units = FixtureUnits();
        var calculator = new RecipeCompletionCalculator(id => units[id]);

        var progress = calculator.Calculate(["goal"], new Dictionary<string, int>
        {
            ["leaf-a"] = 1
        });

        Assert.Equal(2, progress.RequiredLeafCount);
        Assert.Equal(1, progress.OwnedLeafCount);
        Assert.Equal(1, progress.Leaves.Single(leaf => leaf.UnitId == "leaf-a").OwnedCount);
        Assert.Equal(0, progress.Leaves.Single(leaf => leaf.UnitId == "leaf-b").OwnedCount);
    }

    [Fact]
    public void CalculateAllocation_ConsumesSharedLeavesOnceAndReturnsExactRemainder()
    {
        var units = FixtureUnits();
        var calculator = new RecipeCompletionCalculator(id => units[id]);

        var allocation = calculator.CalculateAllocation(["sibling-a", "sibling-b"],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["shared"] = 1,
                ["extra"] = 2
            });

        Assert.Equal(2, allocation.Progress.RequiredLeafCount);
        Assert.Equal(1, allocation.Progress.OwnedLeafCount);
        Assert.Equal(1, allocation.ConsumedByUnitId["shared"]);
        Assert.Equal(0, allocation.RemainingInventory["shared"]);
        Assert.Equal(2, allocation.RemainingInventory["extra"]);
    }

    [Fact]
    public void CalculateAllocation_UsesOrdinalRootOrderForDuplicateDemand()
    {
        var units = FixtureUnits();
        var calculator = new RecipeCompletionCalculator(id => units[id]);
        var inventory = new Dictionary<string, int> { ["shared"] = 1 };

        var forward = calculator.CalculateAllocation(["sibling-b", "sibling-a"], inventory);
        var reverse = calculator.CalculateAllocation(["sibling-a", "sibling-b"], inventory);

        Assert.Equal(forward.Progress.OwnedLeafCount, reverse.Progress.OwnedLeafCount);
        Assert.Equal(forward.ConsumedByUnitId, reverse.ConsumedByUnitId);
        Assert.Equal(forward.RemainingInventory, reverse.RemainingInventory);
    }

    [Fact]
    public void CalculateAllocation_DuplicateRootDemandConsumesEachAvailableCardOnce()
    {
        var units = FixtureUnits();
        var allocation = new RecipeCompletionCalculator(id => units[id])
            .CalculateAllocation(["sibling-a", "sibling-a"],
                new Dictionary<string, int> { ["shared"] = 1 });

        Assert.Equal(2, allocation.Progress.RequiredLeafCount);
        Assert.Equal(1, allocation.Progress.OwnedLeafCount);
        Assert.Equal(1, allocation.ConsumedByUnitId["shared"]);
        Assert.Equal(0, allocation.RemainingInventory["shared"]);
    }

    [Fact]
    public void CalculateAllocation_DirectIntermediateReplacesItsLeafSubtree()
    {
        var units = FixtureUnits();
        var allocation = new RecipeCompletionCalculator(id => units[id])
            .CalculateAllocation(["goal"],
                new Dictionary<string, int> { ["branch"] = 1 });

        Assert.Equal(2, allocation.Progress.RequiredLeafCount);
        Assert.Equal(1, allocation.Progress.OwnedLeafCount);
        Assert.Equal(1, allocation.ConsumedByUnitId["branch"]);
        Assert.Equal(0, allocation.RemainingInventory["branch"]);
    }

    [Fact]
    public void CalculateAllocation_EmptyDemandIgnoresDirtyInventoryForCardProgress()
    {
        var units = FixtureUnits();
        var allocation = new RecipeCompletionCalculator(id => units[id])
            .CalculateAllocation([], new Dictionary<string, int>
            {
                ["shared"] = -2,
                ["extra"] = 3
            });

        Assert.Empty(allocation.Progress.Leaves);
        Assert.Equal(0, allocation.Progress.RequiredLeafCount);
        Assert.Equal(0, allocation.Progress.OwnedLeafCount);
        Assert.Empty(allocation.ConsumedByUnitId);
        Assert.Equal(0, allocation.RemainingInventory["shared"]);
        Assert.Equal(3, allocation.RemainingInventory["extra"]);
    }

    [Fact]
    public void CalculateAllocation_ExposesResourceRequirementsSeparatelyFromCards()
    {
        var units = FixtureUnits();
        var calculator = new RecipeCompletionCalculator(id => units[id]);

        var allocation = calculator.CalculateAllocation(["resource-goal"], new Dictionary<string, int>());

        Assert.Equal(2, allocation.ResourceRequirements["GOLD"]);
        Assert.Equal(1, allocation.ResourceRequirements["LUMBER"]);
        Assert.Empty(allocation.Progress.Leaves);
        Assert.Empty(allocation.ConsumedByUnitId);
    }

    [Fact]
    public void CalculateAllocation_SnapshotsRejectCollectionMutationAndInputChanges()
    {
        var units = FixtureUnits();
        var inventory = new Dictionary<string, int>
        {
            ["shared"] = 1,
            ["extra"] = 2
        };
        var allocation = new RecipeCompletionCalculator(id => units[id])
            .CalculateAllocation(["sibling-a", "sibling-b"], inventory);

        Assert.ThrowsAny<Exception>(() =>
            ((IDictionary<string, long>)(object)allocation.ConsumedByUnitId)["shared"] = 99);
        Assert.ThrowsAny<Exception>(() =>
            ((IDictionary<string, long>)(object)allocation.RemainingInventory)["shared"] = 99);
        Assert.ThrowsAny<Exception>(() =>
            ((IDictionary<string, long>)(object)allocation.ResourceRequirements)["GOLD"] = 99);
        Assert.ThrowsAny<Exception>(() =>
            ((IList<RecipeLeafProgress>)(object)allocation.Progress.Leaves).Clear());
        Assert.ThrowsAny<Exception>(() =>
            ((IList<RecipeLeafProgress>)(object)allocation.Progress.MissingLeaves).Clear());

        inventory["shared"] = 99;
        inventory["extra"] = 0;

        Assert.Equal(1, allocation.ConsumedByUnitId["shared"]);
        Assert.Equal(0, allocation.RemainingInventory["shared"]);
        Assert.Equal(2, allocation.RemainingInventory["extra"]);
        Assert.Equal(2, allocation.Progress.RequiredLeafCount);
        Assert.Equal(1, allocation.Progress.OwnedLeafCount);
        Assert.Collection(allocation.Progress.Leaves, _ => { });
    }

    [Fact]
    public void SurfaceAllocation_WritesMachineEvidenceWhenArtifactRequested()
    {
        var artifact = Environment.GetEnvironmentVariable("ORAND_QA_ARTIFACT");
        if (string.IsNullOrWhiteSpace(artifact)) return;

        var allocation = new RecipeCompletionCalculator(id => FixtureUnits()[id])
            .CalculateAllocation(["sibling-a", "sibling-b"],
                new Dictionary<string, int> { ["shared"] = 1, ["extra"] = 2 });
        Directory.CreateDirectory(Path.GetDirectoryName(artifact)!);
        var evidence = new
        {
            progress = new
            {
                required = allocation.Progress.RequiredLeafCount,
                owned = allocation.Progress.OwnedLeafCount
            },
            consumed = allocation.ConsumedByUnitId,
            remaining = allocation.RemainingInventory
        };
        File.WriteAllText(artifact, JsonSerializer.Serialize(evidence));
        var mainArtifact = Path.Combine(Path.GetDirectoryName(artifact)!,
            "task-4-round-20-adaptive-build-routing.json");
        File.WriteAllText(mainArtifact, JsonSerializer.Serialize(new
        {
            task = 4,
            surface = Path.GetFileName(artifact),
            evidence = evidence
        }));
    }

    private static Dictionary<string, UnitDefinition> FixtureUnits() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["goal"] = Unit("goal", new() { ["branch"] = 1, ["leaf-b"] = 1 }),
            ["branch"] = Unit("branch", new() { ["leaf-a"] = 1 }),
            ["leaf-a"] = Unit("leaf-a"),
            ["leaf-b"] = Unit("leaf-b"),
            ["sibling-a"] = Unit("sibling-a", new() { ["shared"] = 1 }),
            ["sibling-b"] = Unit("sibling-b", new() { ["shared"] = 1 }),
            ["shared"] = Unit("shared"),
            ["extra"] = Unit("extra"),
            ["resource-goal"] = Unit("resource-goal",
                new() { ["rawcode:GOLD"] = 2, ["rawcode:LUMBER"] = 1 }),
            ["rawcode:GOLD"] = Unit("rawcode:GOLD", tier: "자원"),
            ["rawcode:LUMBER"] = Unit("rawcode:LUMBER", tier: "자원")
        };

    private static UnitDefinition Unit(string id, Dictionary<string, int>? recipe = null,
        string tier = "일반") =>
        new() { Id = id, Name = id, Tier = tier, Recipe = recipe ?? [] };
}
