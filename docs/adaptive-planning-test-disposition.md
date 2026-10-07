# Adaptive planning test disposition

Task 19 treats `NavigationIntervalScorer` and the production
`AdaptivePlanningCompositionRoot` replay as the authority for adaptive navigation
selection. The remaining `NavigationAdvisor` assertions in `SmokeTests/Program.cs`
and `ViviEternalRecommendationPolicyTests.cs` are retained only as
legacy-compatibility coverage for the older display/ranking contract. They do not
prove all-15 selection, source pins, fail-closed behavior, or timing; those claims
belong exclusively to `AdaptivePlanningSystemTests` and the explicit adaptive
planning/navigation Smoke PASS markers.
