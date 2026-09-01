namespace OrandOverlay;

internal sealed class FirstRareTargetPolicy
{
    private string? _pinnedGoalUnitId;

    public IReadOnlyList<Recommendation> Apply(RecommendationSurface surface,
        IReadOnlyList<Recommendation> recommendations, int take = 5)
    {
        if (surface != RecommendationSurface.FastRare)
        {
            Reset();
            return recommendations;
        }

        if (_pinnedGoalUnitId is null &&
            recommendations.FirstOrDefault(item =>
                item.RecipeProgress.OwnedLeafCount > 0) is { } progressed)
            _pinnedGoalUnitId = progressed.Route.GoalUnitId;

        var pinned = recommendations.FirstOrDefault(item => item.Route.GoalUnitId.Equals(
            _pinnedGoalUnitId, StringComparison.OrdinalIgnoreCase));
        return (pinned is null
                ? recommendations
                : new[] { pinned }.Concat(recommendations.Where(item =>
                    !item.Route.GoalUnitId.Equals(pinned.Route.GoalUnitId,
                        StringComparison.OrdinalIgnoreCase))))
            .Take(Math.Max(1, take))
            .ToList();
    }

    public void Select(string routeId, IReadOnlyList<Recommendation> recommendations)
    {
        var selected = recommendations.FirstOrDefault(item =>
            item.Route.Id.Equals(routeId, StringComparison.OrdinalIgnoreCase) ||
            item.Route.GoalUnitId.Equals(routeId, StringComparison.OrdinalIgnoreCase));
        if (selected is not null) _pinnedGoalUnitId = selected.Route.GoalUnitId;
    }

    public void Reset() => _pinnedGoalUnitId = null;
}
