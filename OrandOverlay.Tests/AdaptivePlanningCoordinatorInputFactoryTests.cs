using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AdaptivePlanningCoordinatorInputFactoryTests
{
    [Fact]
    public void ManualGoalConstrainsAdaptiveEvaluationWithoutDisablingNavigationSimulations()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var factory = new AdaptivePlanningCoordinatorInputFactory(Path.Combine(ProjectDirectory(), "Data"));
        var goal = catalog.Unit("rawcode:F90H");
        var alternative = catalog.AllUnits.First(unit => TopTier(unit.Tier) && unit.Id != goal.Id &&
            AdaptiveRouteEvaluator.ClassifyDamage(unit) != DamageLane.Unknown);
        var legend = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "전설");
        var rare = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "희귀함");
        var source = Source(catalog, goal, legend, rare, 21) with
        {
            ManualLatches = new ManualLatches(true, false),
            RouteGoalUnitIds = [alternative.Id]
        };

        var input = factory.Create(source);

        Assert.Equal(goal.Id, input.BuildSnapshot.RouteCandidate?.GoalUnitId);
        Assert.Equal(NavigationIntervalScorer.RequiredOptionIds,
            input.NavigationRequest.Options.Select(option => option.OptionId));
        Assert.False(input.NavigationRequest.ManualNavigationOverride);
        var coordinator = new AdaptivePlanningCoordinator(AdaptiveBuildState.Initial(3) with
        {
            Phase = PlannerPhase.CommitRound20,
            ManualLatches = source.ManualLatches
        });
        var first = Apply(coordinator, input);
        var updated = factory.Create(source with
        {
            Round = 22,
            Phase = coordinator.State.Phase,
            Inventory = [new InventoryEntry { UnitId = goal.Id, Count = 1 }]
        });
        var second = Apply(coordinator, updated);
        Assert.NotEqual(first.InputFingerprint, second.InputFingerprint);
        Assert.Equal(goal.Id, second.State.RouteLock?.GoalUnitId);
        Assert.True(second.State.RouteLock!.ProgressBp > first.State.RouteLock!.ProgressBp);
        Assert.Null(second.SuggestedGoalId);
        Assert.True(NavigationAutomaticRecommendationPolicy.ShouldApply(true, source.ManualLatches,
            PlannerPhase.AwaitMarineford, NavigationRecommendationState.Actionable,
            "PathOfKings.BountyHunter"));
        Assert.False(NavigationAutomaticRecommendationPolicy.ShouldApply(true, new ManualLatches(true, true),
            PlannerPhase.Committed, NavigationRecommendationState.Actionable,
            "PathOfKings.BountyHunter"));
    }

    [Fact]
    public void CompleteSnapshotTreatsMissingRewardWispsAsZero()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var factory = new AdaptivePlanningCoordinatorInputFactory(
            Path.Combine(ProjectDirectory(), "Data"));
        var goal = catalog.AllUnits.First(unit => TopTier(unit.Tier) &&
            AdaptiveRouteEvaluator.ClassifyDamage(unit) != DamageLane.Unknown);
        var legend = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "전설");
        var rare = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "희귀함");

        var input = factory.Create(Source(catalog, goal, legend, rare, round: 20) with
        {
            Phase = PlannerPhase.SpendRares,
            RewardWisps = ImmutableDictionary<string, int>.Empty
        });

        Assert.Equal(0, input.BuildSnapshot.SpecialUncommonWispCount);
        Assert.Equal(0, input.BuildSnapshot.RareWispCount);
    }

    [Fact]
    public void RealProductionFactoryFeedsAllSimulatorsAndRoundLifecycle()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var dataDirectory = Path.Combine(ProjectDirectory(), "Data");
        var factory = new AdaptivePlanningCoordinatorInputFactory(dataDirectory);
        var goal = catalog.AllUnits.First(unit => TopTier(unit.Tier) &&
            AdaptiveRouteEvaluator.ClassifyDamage(unit) != DamageLane.Unknown);
        var legend = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "전설");
        var rare = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "희귀함");
        var source = Source(catalog, goal, legend, rare, round: 20);

        var round20Input = factory.Create(source);

        Assert.Equal(NavigationIntervalScorer.RequiredOptionIds,
            round20Input.NavigationRequest.Options.Select(option => option.OptionId));
        Assert.True(round20Input.NavigationRequest.RouteConfidenceBp >= 8_000);
        Assert.True(round20Input.NavigationRequest.MechanicsConfidenceBp >= 8_000);
        Assert.NotEmpty(round20Input.BuildSnapshot.LegendCandidates);
        Assert.Contains(legend.Id, round20Input.BuildSnapshot.NewlyObservedLegendIds);
        Assert.NotNull(round20Input.BuildSnapshot.RouteCandidate);
        Assert.DoesNotContain(round20Input.CanonicalInput.Values,
            value => value.Name.Contains("runtime-selection", StringComparison.Ordinal));

        var committed = AdaptiveBuildState.Initial(3) with
        {
            Phase = PlannerPhase.Committed,
            FirstRareHistory = FirstRareHistory.Observed,
            RouteLock = round20Input.BuildSnapshot.RouteCandidate
        };
        var coordinator = new AdaptivePlanningCoordinator(committed);
        var round20 = Apply(coordinator, round20Input);
        Assert.Equal(NavigationRecommendationState.Provisional, round20.Navigation.State);
        Assert.False(round20.Navigation.ClaimsRuntimeSelection);

        var round21Input = factory.Create(source with
        {
            Round = 21,
            Phase = coordinator.State.Phase,
            PreviouslyObservedLegendIds = [legend.Id]
        });
        var round21 = Apply(coordinator, round21Input);
        Assert.Equal(NavigationRecommendationState.Actionable, round21.Navigation.State);
        Assert.NotNull(round21.State.NavigationLockId);
        Assert.False(round21.Navigation.ClaimsRuntimeSelection);
    }

    [Fact]
    public void TransientProductionInputFreezesLastGoodWithoutMapSelection()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var factory = new AdaptivePlanningCoordinatorInputFactory(
            Path.Combine(ProjectDirectory(), "Data"));
        var goal = catalog.AllUnits.First(unit => TopTier(unit.Tier) &&
            AdaptiveRouteEvaluator.ClassifyDamage(unit) != DamageLane.Unknown);
        var legend = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "전설");
        var rare = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "희귀함");
        var coordinator = new AdaptivePlanningCoordinator(
            AdaptiveBuildState.Initial(3));
        var good = Apply(coordinator, factory.Create(Source(catalog, goal, legend, rare, 20)));
        var transient = factory.Create(Source(catalog, goal, legend, rare, 21) with
        {
            IsTransient = true,
            PreviouslyObservedLegendIds = [legend.Id]
        });
        var writes = 0;

        coordinator.ScheduleApply(AdaptivePlanningCoordinator.Evaluate(
            coordinator.TryBegin(transient)!), action => action(), _ => writes++);

        Assert.Equal(NavigationScoringInputState.Transient,
            transient.NavigationRequest.InputState);
        Assert.Contains(transient.CanonicalInput.Values,
            value => !value.IsKnown);
        Assert.Equal(0, writes);
        Assert.Same(good, coordinator.LastApplied);
    }

    [Fact]
    public void TextLineEndingsDoNotChangeProfileDataOrPlannerFingerprint()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adaptive-hash-{Guid.NewGuid():N}");
        var shipped = Path.Combine(ProjectDirectory(), "Data");
        var lf = WriteProfileSet(root, "lf", shipped, "\n");
        var crlf = WriteProfileSet(root, "crlf", shipped, "\r\n");
        var cr = WriteProfileSet(root, "cr", shipped, "\r");
        var changed = WriteProfileSet(root, "changed", shipped, "\n");
        try
        {
            var catalog = new DataCatalog();
            catalog.Load(loadCarryPolicy: false);
            var goal = catalog.AllUnits.First(unit => TopTier(unit.Tier) &&
                AdaptiveRouteEvaluator.ClassifyDamage(unit) != DamageLane.Unknown);
            var legend = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "전설");
            var rare = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "희귀함");
            var source = Source(catalog, goal, legend, rare, 20);

            var lfInput = new AdaptivePlanningCoordinatorInputFactory(lf).Create(source);
            var crlfInput = new AdaptivePlanningCoordinatorInputFactory(crlf).Create(source);
            var crInput = new AdaptivePlanningCoordinatorInputFactory(cr).Create(source);

            Assert.Equal(lfInput.CanonicalInput.ProfileHash,
                crlfInput.CanonicalInput.ProfileHash);
            Assert.Equal(lfInput.CanonicalInput.ProfileHash,
                crInput.CanonicalInput.ProfileHash);
            Assert.Equal(lfInput.CanonicalInput.DataHash,
                crlfInput.CanonicalInput.DataHash);
            Assert.Equal(lfInput.CanonicalInput.DataHash,
                crInput.CanonicalInput.DataHash);
            Assert.Equal(lfInput.Fingerprint, crlfInput.Fingerprint);
            Assert.Equal(lfInput.Fingerprint, crInput.Fingerprint);

            var gameDataPath = Path.Combine(changed, "game-data.demo.json");
            File.WriteAllText(gameDataPath, File.ReadAllText(gameDataPath)
                .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2",
                    StringComparison.Ordinal));
            var changedInput = new AdaptivePlanningCoordinatorInputFactory(changed)
                .Create(source);
            Assert.NotEqual(lfInput.CanonicalInput.DataHash,
                changedInput.CanonicalInput.DataHash);
            Assert.NotEqual(lfInput.Fingerprint, changedInput.Fingerprint);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AdaptivePlanningInputSource Source(DataCatalog catalog,
        UnitDefinition goal, UnitDefinition legend, UnitDefinition rare, int round) => new()
    {
        MatchGeneration = 3,
        RecognitionRevision = round,
        Round = round,
        Phase = PlannerPhase.Committed,
        ActiveStoryStage = 9,
        CompletedStoryMilestones = ["stage-1", "stage-9"],
        Inventory = [new InventoryEntry { UnitId = legend.Id, Count = 1 },
            new InventoryEntry { UnitId = rare.Id, Count = 1 }],
        Units = catalog.AllUnits.ToDictionary(unit => unit.Id,
            StringComparer.OrdinalIgnoreCase),
        GoalUnitId = goal.Id,
        RouteGoalUnitIds = [goal.Id],
        NavigationOptionId = "AlliedForces.DoubleBenefit",
        GoroseiMode = GoroseiMode.None,
        CompletedTopUnitIds = [],
        GrowthUnitIds = [],
        RewardWisps = ImmutableDictionary<string, int>.Empty
            .Add("e016", 0).Add("e019", 0),
        PreviouslyObservedLegendIds = [],
        ManualLatches = ManualLatches.None
    };

    private static AdaptivePlanningApplied Apply(AdaptivePlanningCoordinator coordinator,
        AdaptivePlanningRefreshInput input)
    {
        AdaptivePlanningApplied? applied = null;
        coordinator.ScheduleApply(AdaptivePlanningCoordinator.Evaluate(
            coordinator.TryBegin(input)!), action => action(), value => applied = value);
        return applied!;
    }

    private static bool TopTier(string tier) => BaseTier(tier) is
        "신비함" or "초월" or "불멸" or "영원" or "제한됨";
    private static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();

    private static string WriteProfileSet(string root, string name,
        string shippedDirectory, string lineEnding)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        foreach (var fileName in new[]
        {
            "navigation-mechanics-2314.json", "game-data.demo.json",
            "map-recipe-overrides-2314.txt", "tmo-unit-catalog.json",
            "tmo-unit-additions-42479.json"
        })
        {
            var text = File.ReadAllText(Path.Combine(shippedDirectory, fileName))
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Replace("\n", lineEnding, StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(directory, fileName), text);
        }
        return directory;
    }

    private static string ProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OrandOverlay.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
