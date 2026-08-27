namespace OrandOverlay;

internal static class NavigationAutomaticRecommendationPolicy
{
    public static bool ShouldApply(bool enabled, ManualLatches latches,
        NavigationRecommendationState state, string? recommendedOptionId) =>
        enabled && !latches.NavigationOverride &&
        !string.IsNullOrWhiteSpace(recommendedOptionId) &&
        state is NavigationRecommendationState.Actionable or
            NavigationRecommendationState.Locked or
            NavigationRecommendationState.SourceExpectedForced;
}
