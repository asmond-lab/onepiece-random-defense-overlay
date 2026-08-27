using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AdaptivePlanningSystemBaselineTests
{
    internal const string ProfilePin =
        "1285441e29434ca6b45f50743caa5708c9afdda79cc690c07a6ad2d1179d4928";
    internal const string DataPin =
        "9d251140732dfba86c5a77dfa7073bb8b41dd1f9f4933abe5c63ea92af1d8721";

    [Fact]
    public void CurrentFactoryCoordinatorAndPresentationPathIsSourcePinned()
    {
        var fixture = Fixture();
        var factory = new AdaptivePlanningCoordinatorInputFactory(fixture.DataDirectory);
        var coordinator = new AdaptivePlanningCoordinator();
        var input = factory.Create(fixture.Source(round: 20, phase: PlannerPhase.SpendRares));
        var work = coordinator.TryBegin(input)!;
        AdaptivePlanningApplied? applied = null;

        coordinator.ScheduleApply(AdaptivePlanningCoordinator.Evaluate(work),
            action => action(), value => applied = value);

        Assert.Equal(ProfilePin, input.CanonicalInput.ProfileHash);
        Assert.Equal(DataPin, input.CanonicalInput.DataHash);
        Assert.Equal(NavigationProfiles.Options.Select(option => option.Id),
            input.NavigationRequest.Options.Select(option => option.OptionId));
        Assert.NotNull(applied);
        var presentation = PlannerEvidenceProjector.Project(20, applied, false, null);
        Assert.True(presentation.IsRecommendationOnly);
        Assert.False(presentation.ClaimsRuntimeSelection);
        Assert.Equal("recommendation-only=true;runtime-selection=false",
            presentation[PlannerEvidenceFieldKind.Restrictions].MachineValue);
    }

    internal static SystemFixture Fixture()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.AllUnits.First(unit => IsTop(unit.Tier) &&
            AdaptiveRouteEvaluator.ClassifyDamage(unit) != DamageLane.Unknown);
        var legend = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "전설");
        var rare = catalog.AllUnits.First(unit => BaseTier(unit.Tier) == "희귀함");
        return new SystemFixture(ProjectDirectory(), catalog, goal, legend, rare);
    }

    private static bool IsTop(string tier) => BaseTier(tier) is
        "신비함" or "초월" or "불멸" or "영원" or "제한됨";
    private static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();

    private static string ProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OrandOverlay.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    internal sealed record SystemFixture(string ProjectDirectory, DataCatalog Catalog,
        UnitDefinition Goal, UnitDefinition Legend, UnitDefinition Rare)
    {
        public string DataDirectory => Path.Combine(ProjectDirectory, "Data");

        public AdaptivePlanningInputSource Source(int round, PlannerPhase phase,
            int stage = 9, int specialWisps = 0, int rareWisps = 0,
            ManualLatches? latches = null, bool transient = false,
            long matchGeneration = 0, IReadOnlyList<InventoryEntry>? inventory = null,
            ImmutableArray<string> previouslyObservedLegendIds = default) => new()
        {
            MatchGeneration = matchGeneration,
            RecognitionRevision = round,
            Round = round,
            Phase = phase,
            ActiveStoryStage = transient ? null : stage,
            CompletedStoryMilestones = Enumerable.Range(1, Math.Max(0, stage - 1))
                .Select(value => $"stage-{value}").ToImmutableArray(),
            Inventory = inventory ??
            [
                new InventoryEntry { UnitId = Legend.Id, Count = 1 },
                new InventoryEntry { UnitId = Rare.Id, Count = 1 }
            ],
            Units = Catalog.AllUnits.ToDictionary(unit => unit.Id,
                StringComparer.OrdinalIgnoreCase),
            GoalUnitId = Goal.Id,
            RouteGoalUnitIds = [Goal.Id],
            NavigationOptionId = "AlliedForces.DoubleBenefit",
            GoroseiMode = GoroseiMode.None,
            RewardWisps = ImmutableDictionary<string, int>.Empty
                .Add("e016", specialWisps).Add("e019", rareWisps),
            PreviouslyObservedLegendIds = previouslyObservedLegendIds.IsDefault
                ? [] : previouslyObservedLegendIds,
            ManualLatches = latches ?? ManualLatches.None,
            IsTransient = transient
        };
    }
}
