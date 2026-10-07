namespace OrandOverlay;

public sealed record NormalCraftPlan(
    IReadOnlyList<NormalCraftStep> Steps,
    IReadOnlyList<RecipeLeafProgress> MissingMaterials,
    ResourceRequirements ResourceRequirements,
    bool GoalOwned,
    string Caveat);

public sealed record NormalCraftIngredient(string UnitId, string Name, string Tier,
    long RequiredCount, long OwnedCount, long PriorStepCount, bool IsResource)
{
    public long MissingCount => Math.Max(0, RequiredCount - OwnedCount - PriorStepCount);
}

public sealed record NormalCraftStep(string UnitId, string Name, string Tier, string Image,
    long CombineCount, long OutputCount, IReadOnlyList<NormalCraftIngredient> Ingredients,
    string? SelectionUnitId, string SelectionName, string? CombineKey,
    IReadOnlyList<string> CombineCommands, RecipeConditionResult Conditions,
    bool IsMaterialReady, string StatusText);
