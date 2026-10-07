namespace OrandOverlay;

internal static class NavigationAutomaticRecommendationPolicy
{
    public static bool ShouldApply(bool enabled, ManualLatches latches,
        PlannerPhase phase, NavigationRecommendationState state,
        string? recommendedOptionId) =>
        enabled && !latches.NavigationOverride &&
        (phase == PlannerPhase.Committed || latches.GoalOverride) &&
        !string.IsNullOrWhiteSpace(recommendedOptionId) &&
        state is NavigationRecommendationState.Actionable or
            NavigationRecommendationState.Locked or
            NavigationRecommendationState.SourceExpectedForced;
}
