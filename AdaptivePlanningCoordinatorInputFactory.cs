using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace OrandOverlay;

public sealed class AdaptivePlanningCoordinatorInputFactory
{
    private const int SourceBoundConfidenceBp = 8_000;
    private readonly NavigationMechanicsProfile profile;
    private readonly string profileHash;
    private readonly string dataHash;

    public AdaptivePlanningCoordinatorInputFactory(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new ArgumentException("A data directory is required.", nameof(dataDirectory));
        profile = NavigationMechanicsProfileLoader.LoadFromDirectory(dataDirectory);
        profileHash = HashFiles(dataDirectory, ["navigation-mechanics-2314.json"]);
        dataHash = HashFiles(dataDirectory,
            ["game-data.demo.json", "map-recipe-overrides-2314.txt",
                "tmo-unit-catalog.json", "tmo-unit-additions-42479.json"]);
    }

    public AdaptivePlanningRefreshInput Create(AdaptivePlanningInputSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Units.ContainsKey(source.GoalUnitId))
            throw new ArgumentException("The selected goal must exist in the unit catalog.", nameof(source));
        var inventory = source.Inventory.Where(entry => entry.Count > 0)
            .GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        var observedLegends = inventory.Keys.Where(id => source.Units.TryGetValue(id,
                out var unit) && BaseTier(unit.Tier) == "전설")
            .Order(StringComparer.Ordinal).ToImmutableArray();
        var routeInput = BuildRouteInput(source, inventory, observedLegends);
        var routes = new AdaptiveRouteEvaluator().Evaluate(routeInput);
        var bestRoute = routes.Selected ?? routes.BestPhysical ?? routes.BestMagic;
        var goalBundle = RouteQuestEvaluation.Evaluate(source);
        var beforeBuild = source.PlannedGoalUnitIds.IsDefaultOrEmpty
            ? bestRoute?.FinalLowerBp ?? 0 : goalBundle.PlannedBuildBp;
        var beforeCore = bestRoute?.GoalBp ?? 0;
        var beforeCombat = bestRoute?.PackageBp ?? 0;
        var batch = AdaptivePlanningCoordinatorSimulationFactory.Create(
            profile, routes, source, inventory);
        var options = NavigationIntervalSimulationAdapter.Adapt(batch,
            new AdaptivePlanningCoordinatorOutcomeProjector(
                beforeBuild, beforeCore, beforeCombat));
        options = goalBundle.Apply(options);
        var canonical = new AdaptivePlanningInput(source.MatchGeneration, source.Round,
            source.Phase, new StorySignal(source.ActiveStoryStage ?? 0,
                source.CompletedStoryMilestones, !source.IsTransient),
            CanonicalValues(source, inventory), source.ManualLatches,
            profileHash, dataHash);
        var build = new AdaptiveBuildSnapshot(source.MatchGeneration, source.Round,
            source.IsTransient ? null : source.ActiveStoryStage,
            inventory.Keys.Any(id => source.Units.TryGetValue(id, out var unit) &&
                                     BaseTier(unit.Tier) == "희귀함"),
            Wisp(source, "e016"), Wisp(source, "e019"),
            LegendCandidates(source, inventory, bestRoute),
            observedLegends.Except(source.PreviouslyObservedLegendIds,
                StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            RouteCandidate(bestRoute, routes.Selected is not null), null,
            source.ManualLatches, source.IsTransient, false);
        var request = new NavigationIntervalScoringRequest
        {
            EvaluateContinuously = true,
            Round = source.Round,
            BeforeBuildBp = beforeBuild,
            BeforeCoreBp = beforeCore,
            BeforeCombatBp = beforeCombat,
            RouteConfidenceBp = source.IsTransient ? 0 :
                Math.Min(SourceBoundConfidenceBp, bestRoute?.ConfidenceBp ?? 0),
            MechanicsConfidenceBp = source.IsTransient ? 0 : SourceBoundConfidenceBp,
            InputState = source.IsTransient
                ? NavigationScoringInputState.Transient
                : bestRoute is null
                    ? NavigationScoringInputState.Unknown
                    : NavigationScoringInputState.Ready,
            ManualNavigationOverride = source.ManualLatches.NavigationOverride,
            ManualOverlayOptionId = source.NavigationOptionId,
            CurrentOverlayRecommendationId = source.CurrentOverlayRecommendationId,
            LockedOverlayRecommendationId = source.LockedOverlayRecommendationId,
            Options = options
        };
        return new AdaptivePlanningRefreshInput(canonical, build, request);
    }

    private static AdaptiveRouteEvaluationInput BuildRouteInput(
        AdaptivePlanningInputSource source, IReadOnlyDictionary<string, int> inventory,
        ImmutableArray<string> observedLegends)
    {
        var ids = source.ManualLatches.GoalOverride || source.RouteGoalUnitIds.IsDefaultOrEmpty
            ? [source.GoalUnitId]
            : source.RouteGoalUnitIds;
        var candidates = ids.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(source.Units.ContainsKey).Select(id => new AdaptiveRouteCandidate
            {
                GoalUnitId = id,
                VariantId = "canonical",
                RecognitionConfidenceBp = source.IsTransient ? 0 : SourceBoundConfidenceBp,
                DamageConfidenceBp = 10_000,
                RecipeConfidenceBp = 10_000
            }).ToImmutableArray();
        return new AdaptiveRouteEvaluationInput
        {
            Units = source.Units,
            Inventory = inventory,
            Candidates = candidates,
            FirstObservedLegendIds = observedLegends,
            IsAutomatic = true
        };
    }

    private static ImmutableArray<LegendBuildCandidate> LegendCandidates(
        AdaptivePlanningInputSource source, IReadOnlyDictionary<string, int> inventory,
        AdaptiveRouteScore? bestRoute)
    {
        var calculator = new RecipeCompletionCalculator(id => source.Units[id]);
        var resources = source.IsTransient ? ResourceCompletion.Unknown :
            source.RewardWisps.Values.Any(value => value > 0)
                ? ResourceCompletion.Incomplete : ResourceCompletion.Complete;
        return FirstLegendRecommendationPolicy.Candidates(
                source.Units.Values, source.Units, inventory)
            .Select(unit =>
            {
                var progress = calculator.CalculateAllocation([unit.Id], inventory).Progress;
                var missing = checked((int)Math.Min(int.MaxValue,
                    Math.Max(0, progress.RequiredLeafCount - progress.OwnedLeafCount)));
                var preserved = bestRoute?.PackageUnitIds.Contains(unit.Id,
                    StringComparer.OrdinalIgnoreCase) == true ? 1 : 0;
                return new LegendBuildCandidate(unit.Id, missing == 0,
                    resources, missing, preserved);
            }).OrderBy(candidate => candidate.UnitId, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static AdaptiveRouteLockCandidate? RouteCandidate(
        AdaptiveRouteScore? route, bool robust) => route is null ? null : new(
            route.Lane, route.GoalUnitId,
            $"{route.VariantId}:{string.Join('+', route.PackageUnitIds)}",
            route.FinalLowerBp, robust);

    private static ImmutableArray<PlanningValue> CanonicalValues(
        AdaptivePlanningInputSource source, IReadOnlyDictionary<string, int> inventory)
    {
        var values = new List<PlanningValue>
        {
            PlanningValue.Known("recognition-revision", source.RecognitionRevision),
            PlanningValue.Known($"goal:{source.GoalUnitId}", 1),
            PlanningValue.Known($"navigation:{source.NavigationOptionId}", 1),
            PlanningValue.Known($"gorosei:{source.GoroseiMode}", 1),
            PlanningValue.Known("route-quest:transcendent", (int)source.RouteQuests.Status("Q008")),
            PlanningValue.Known("route-quest:limited", (int)source.RouteQuests.Status("Q011")),
            PlanningValue.Known("route-quest:both", source.PursueBothRouteQuests ? 1 : 0),
            source.IsTransient ? PlanningValue.Unknown("runtime-signals") :
                PlanningValue.Known("runtime-signals", 1)
        };
        values.AddRange(inventory.Select(pair =>
            PlanningValue.Known($"inventory:{pair.Key}", pair.Value)));
        values.AddRange(source.PlannedGoalUnitIds.Select((id, index) =>
            PlanningValue.Known($"planned-goal:{index}:{id}", 1)));
        values.AddRange(RouteQuestCatalog.All.Select(quest =>
            PlanningValue.Known($"route-quest:{quest.Id}", (int)source.RouteQuests.Status(quest.Id))));
        values.AddRange(source.CompletedTopUnitIds.Select(id =>
            PlanningValue.Known($"completed-top:{id}", 1)));
        values.AddRange(source.GrowthUnitIds.Select(id =>
            PlanningValue.Known($"growth:{id}", 1)));
        values.AddRange(source.RewardWisps.Select(pair =>
            PlanningValue.Known($"reward-wisp:{pair.Key}", pair.Value)));
        values.AddRange(source.PreviouslyObservedLegendIds.Select(id =>
            PlanningValue.Known($"observed-legend:{id}", 1)));
        values.AddRange(source.AdditionalValues);
        return values.OrderBy(value => value.Name, StringComparer.Ordinal).ToImmutableArray();
    }

    private static int? Wisp(AdaptivePlanningInputSource source, string id) =>
        source.IsTransient ? null : source.RewardWisps.TryGetValue(id, out var value)
            ? value : 0;

    private static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();

    private static string HashFiles(string directory, ImmutableArray<string> names)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in names.Order(StringComparer.Ordinal))
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            hash.AppendData(nameBytes);
            hash.AppendData(CanonicalizeLineEndings(
                File.ReadAllBytes(Path.Combine(directory, name))));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static byte[] CanonicalizeLineEndings(byte[] bytes)
    {
        var firstCarriageReturn = Array.IndexOf(bytes, (byte)'\r');
        if (firstCarriageReturn < 0) return bytes;
        var canonical = new byte[bytes.Length];
        Buffer.BlockCopy(bytes, 0, canonical, 0, firstCarriageReturn);
        var written = firstCarriageReturn;
        for (var index = firstCarriageReturn; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)'\r')
            {
                canonical[written++] = bytes[index];
                continue;
            }
            canonical[written++] = (byte)'\n';
            if (index + 1 < bytes.Length && bytes[index + 1] == (byte)'\n')
                index++;
        }
        return canonical[..written];
    }
}
