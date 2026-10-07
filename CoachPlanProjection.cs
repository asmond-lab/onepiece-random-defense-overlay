namespace OrandOverlay;

// Display-only: never infer a craft receipt from component ownership.
public sealed record CoachPlanComponent(string UnitId, string Name, string Image,
    int RequiredCount, int? OwnedCount, IReadOnlyList<CoachPlanComponent> Children)
{
    public int? RemainingCount => OwnedCount is { } count ? Math.Max(0, RequiredCount - count) : null;
    public bool IsOwned => RemainingCount == 0;
}

public sealed record CoachPlanProjection(string GoalName, string? GoalId,
    int? Round, int? Story, int? SelectionWisps, bool IsCurrent,
    IReadOnlyList<CoachPlanComponent> Components, ObservedCraftProgress? CraftProgress,
    string NextTarget)
{
    public IReadOnlyList<string> RouteUnitIds { get; init; } = [];
    public string MainHeading { get; init; } = "";
    public string GoalImage { get; init; } = "";
    public string MainGoalLabel { get; init; } = "";
    public IReadOnlyList<CoachPlanComponent> MainPath { get; init; } = [];
    public static CoachPlanProjection Create(CoachDecision decision, CoachFrame frame, DataCatalog? catalog)
    {
        var current = frame.IsCurrent && decision.Id != "start" && decision.Kind != CoachActionKind.Finished;
        var goalId = frame.GoalId;
        var recommendation = frame.Recommendations.FirstOrDefault(item => item.Route.GoalUnitId == goalId)
            ?? frame.Recommendations.FirstOrDefault();
        var root = recommendation?.RecipeTree;
        if (catalog is not null && goalId is not null)
            root = new RecipeTreeBuilder(catalog, null).BuildRecipeTree(goalId, 1,
                frame.Inventory.ToDictionary(pair => pair.Key, pair => pair.Value), new HashSet<string>());
        var bullet = goalId == BulletGuidePolicy.GoalId;
        var componentOrder = new[] { "rawcode:U20h", "rawcode:930h", "rawcode:V20h" };
        var components = root?.Children.Select(Project).ToArray() ?? [];
        if (bullet) components = components.OrderBy(item =>
            Array.IndexOf(componentOrder, item.UnitId) is var index && index >= 0 ? index : int.MaxValue).ToArray();
        var path = current && root is not null && decision.TargetUnitId is { } target
            ? FindPath(root, target) : [];
        var next = string.Join(" → ", path.Select(Name));
        return new(root?.Name ?? decision.GoalLabel, goalId,
            current && frame.Round > 0 ? frame.Round : null,
            current ? frame.CompletedStoryStage : null,
            current ? frame.RewardWisps.GetValueOrDefault("e018") : null,
            current, components, current && !frame.Paused ? decision.CraftProgress : null, next)
            {
                RouteUnitIds = path.Select(node => node.UnitId).ToArray(),
                MainHeading = !current ? "게임 연결 대기" : (root?.Name ?? decision.GoalLabel) + " 조합 계획",
                GoalImage = root?.Image ?? "",
                MainGoalLabel = frame.Mode == PlayMode.Guide && GuideCatalog.Find(frame.GuideNumber) is { } option && option.GoalId == goalId
                    ? option.Name.Split('·').Last().Trim() : root?.Name ?? decision.GoalLabel,
                MainPath = path.Take(Math.Max(0, path.Count - 1)).Select(Project).ToArray()
            };

        CoachPlanComponent Project(RecipeTreeNode node) => new(node.UnitId,
            Name(node), node.Image,
            node.RequiredCount, current ? node.OwnedCount : null, node.Children.Select(Project).ToArray());
        string Name(RecipeTreeNode node) => bullet && node.UnitId == "rawcode:U20h" ? "검은수염" : node.Name;
    }

    private static List<RecipeTreeNode> FindPath(RecipeTreeNode node, string target)
    {
        if (node.UnitId == target) return [node];
        foreach (var child in node.Children)
        {
            var path = FindPath(child, target);
            if (path.Count == 0) continue;
            path.Add(node);
            return path;
        }
        return [];
    }
}
