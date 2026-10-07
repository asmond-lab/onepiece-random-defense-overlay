using System.Collections.Immutable;
using System.Text.Json;
using OrandOverlay;
using Xunit;
using Xunit.Abstractions;
namespace PlannerEvidenceCapture.Tests;

// Headless pure production-policy replay, NOT MainWindow accepted-frame or WPF evidence.
public sealed class FastUniqueUiPolicyReplayTests(ITestOutputHelper output)
{
    [Fact]
    public void SourceSelectedSaboHandDoesNotOverrideTarget()
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var hand = ImmutableDictionary<string, int>.Empty.Add("rawcode:E10h", 1);
        var plan = new BulletGuidePolicy(catalog).Plan(9, 0, hand, "악몽");
        Assert.Equal("rawcode:M30h", plan.TargetUnitId);
    }

    [Fact]
    public void ReplaySerializesEverySyntheticInputAndActualPolicyDecision()
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var rows = new List<object>();
        var history = FastUniqueState.Unknown;
        long generation = 0, revision = 0;
        foreach (var original in FastUniqueUiFixture.Build(catalog))
        {
            var item = original;
            if (item.Boundary) { generation++; history = FastUniqueState.Unknown; }
            var request = FastUniqueUiFixture.Request(item, generation, ++revision);
            var inventory = request.Entries.ToImmutableDictionary(e => e.UnitId, e => e.Count);
            var policy = new BulletGuidePolicy(catalog);
            history = policy.ObserveFastUnique(history, item.Round, inventory, true);
            var frame = ModelFrame(catalog, item, request, history, generation, revision);
            // Match MainWindow.Coach's pre-decision/render enrichment; Decide only enriches its local record copy.
            if (frame.Mode == PlayMode.Guide && frame.GuidePlan is not null)
                frame = frame with { ShipReservations = ShipReservationPolicy.Evaluate(frame, catalog) };
            var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
            var row = new { item.Name, Kind = "synthetic-headless-policy-model-not-accepted-WPF-frame",
                item.Round, item.Boundary, request.Status, Inventory = inventory, item.Wisps,
                frame.Story, frame.NativeNavigation, Projection = BulletGuideRowProjection.Observe(item.Name, decision, frame) };
            rows.Add(row);
            output.WriteLine(JsonSerializer.Serialize(row));
            Assert.NotEqual(FastUniqueState.CompletedVerified, frame.GuidePlan!.FastUnique);
            Assert.True(BulletGuideRowProjection.MatchesObservation(frame, item.Round, generation, revision,
                inventory, GoroseiMarkerSnapshot.Unknown));
            Assert.False(BulletGuideRowProjection.MatchesObservation(frame, item.Round, generation + 1, revision,
                inventory, GoroseiMarkerSnapshot.Unknown));
            Assert.False(BulletGuideRowProjection.MatchesObservation(frame, item.Round, generation, revision + 1,
                inventory, GoroseiMarkerSnapshot.Unknown));
            Assert.False(BulletGuideRowProjection.MatchesObservation(frame, item.Round, generation, revision,
                inventory.SetItem("luffy_common", inventory.GetValueOrDefault("luffy_common") + 1), GoroseiMarkerSnapshot.Unknown));
            if (item.Name == "zombie-nonselectable")
            {
                Assert.DoesNotContain("H00h", BulletGuideSupportPolicy.CommonCodes);
                Assert.NotEqual("rawcode:H00h", decision.TargetUnitId);
                var missing = new RecipeCompletionCalculator(catalog.Unit).Calculate([frame.GuidePlan.TargetUnitId!], inventory).MissingLeaves;
                var selectable = missing.Where(l => catalog.Unit(l.UnitId).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains)).Sum(l => l.MissingCount);
                Assert.Equal(selectable, frame.GuidePlan.RareCommonDeficit);
                Assert.Equal(missing.Sum(l => l.MissingCount) - selectable, frame.GuidePlan.RareOtherDeficit);
            }
            if (item.Name.StartsWith("first-rare-"))
            {
                Assert.Equal(item.Round == 0 ? BulletGuideStage.RoundUnknown : BulletGuideStage.FastUniqueRare,
                    frame.GuidePlan.Stage);
                Assert.Equal(item.Round == 0 ? CoachActionKind.Recognition : CoachActionKind.Craft, decision.Kind);
                if (item.Round == 0) Assert.Null(decision.TargetUnitId);
                else Assert.NotNull(decision.CraftRecipe);
            }
            if (item.Name == "deadline-8")
            {
                Assert.Equal("rawcode:M30h", frame.GuidePlan.TargetUnitId);
                Assert.Contains(decision.RecipePreview, p => p.UnitId == "rawcode:M30h" && p.Tier == "히든 [물딜]");
            }
            if (item.Name == "rare-consumed-7") Assert.Equal(FastUniqueState.RarePreviouslyObserved, frame.GuidePlan.FastUnique);
            if (item.Name == "new-session-7") Assert.Equal(BulletGuideStage.FastUniqueRare, frame.GuidePlan.Stage);
            if (item.Name is "selection-1" or "selection-3" or "selection-after-one")
            {
                Assert.NotEqual("e018", decision.RewardWispId);
                Assert.Null(decision.SelectionBatch);
                Assert.Equal(item.Name == "selection-1" ? 1 : item.Name == "selection-3" ? 3 : 2,
                    frame.RewardWisps.GetValueOrDefault("e018"));
            }
            if (item.Name == "selection-after-one")
                Assert.Equal(1, frame.Inventory.GetValueOrDefault(catalog.Unit("rawcode:300h").Id));
            if (item.Name.StartsWith("actual-"))
            {
                Assert.Equal(BulletGuidePolicy.NavigationId, frame.GuidePlan.PlannedNavigation);
                Assert.Equal(BulletGuideAdvice.Operation(frame), decision.OperationGuide);
                Assert.Equal(item.Name == "actual-unknown" ? NativeNavigationStatus.Unknown : NativeNavigationStatus.Selected,
                    frame.NativeNavigation.Status);
            }
            if (item.Name == "bullet-zero-to-one")
            {
                Assert.Equal(BulletGuidePolicy.GoalId, frame.GuidePlan.TargetUnitId);
                Assert.Equal(CoachActionKind.Craft, decision.Kind);
                Assert.Equal(BulletGuidePolicy.GoalId, decision.TargetUnitId);
            }
            if (item.Name == "bullet-owned-no-recraft")
                Assert.DoesNotContain(frame.CraftSteps, s => s.TargetUnitId == BulletGuidePolicy.GoalId);
            if (item.Name.EndsWith("-received") || item.Name.EndsWith("-repeat"))
            {
                Assert.Equal(StorySequenceAction.FindFirstRare, frame.Story!.Action);
                Assert.Equal(CoachActionKind.Reward, decision.Kind);
                Assert.Equal(item.Name.Split('-')[0], decision.RewardWispId);
                Assert.Contains("한 번", decision.Controls);
                Assert.Equal(0, frame.RewardWisps.GetValueOrDefault("e018"));
            }
            if (item.Name.EndsWith("-ready-rare")) Assert.Equal(CoachActionKind.Craft, decision.Kind);
            if (item.Name.EndsWith("-spent") || item.Name.EndsWith("-unreceived")) Assert.NotEqual(CoachActionKind.Reward, decision.Kind);
        }
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(rows));
        Assert.Equal(FastUniqueUiFixture.Build(catalog).Count, serialized.RootElement.GetArrayLength());
        foreach (var row in serialized.RootElement.EnumerateArray())
        {
            var projection = row.GetProperty("Projection");
            var reservations = projection.GetProperty("ShipReservations");
            Assert.Equal(JsonValueKind.Object, reservations.ValueKind);
            using var key = JsonDocument.Parse(reservations.GetProperty("InputKey").GetString()!);
            Assert.Equal(projection.GetProperty("MatchGeneration").GetInt64(), key.RootElement.GetProperty("MatchGeneration").GetInt64());
            Assert.Equal(projection.GetProperty("RecognitionRevision").GetInt64(), key.RootElement.GetProperty("RecognitionRevision").GetInt64());
            Assert.True(key.RootElement.GetProperty("IsCurrent").GetBoolean());
            Assert.True(key.RootElement.GetProperty("HasPlan").GetBoolean());
            Assert.Equal(row.GetProperty("Inventory").EnumerateObject().OrderBy(p => p.Name).Select(p => (p.Name, p.Value.GetInt32())),
                key.RootElement.GetProperty("Inventory").EnumerateArray().OrderBy(p => p.GetProperty("Key").GetString()).Select(p => (p.GetProperty("Key").GetString()!, p.GetProperty("Value").GetInt32())));
            Assert.Equal(JsonValueKind.Object, row.GetProperty("Projection").GetProperty("Decision").ValueKind);
            Assert.Equal(JsonValueKind.Array, row.GetProperty("Projection").GetProperty("Candidates").ValueKind);
            Assert.Equal(JsonValueKind.Number, row.GetProperty("Projection").GetProperty("RecognitionRevision").ValueKind);
        }
    }

    [Theory]
    [InlineData("PathOfKings.BountyHunter", false)]
    [InlineData("PathOfKings.RoyalLoader", false)]
    [InlineData("AlliedForces.EmergencyCall", true)]
    [InlineData(null, false)]
    public void ActualNavigationRevalidatesRealPreviouslyProducedBulletStep(string? actual, bool allows)
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var item = FastUniqueUiFixture.Build(catalog).Single(c => c.Name == "bullet-zero-to-one");
        var frame = ModelFrame(catalog, item, FastUniqueUiFixture.Request(item, 0, 1), FastUniqueState.Unknown, 0, 1);
        var planner = new BeginnerCoachPlanner(catalog);
        Assert.Equal(CoachActionKind.Craft, planner.Decide(frame).Kind);
        Assert.Contains(frame.CraftSteps, s => s.TargetUnitId == BulletGuidePolicy.GoalId);
        // Explicit stale-final-consumer seam, NOT a fresh producer or fabricated action.
        var current = frame with { Inventory = frame.Inventory.Add(BulletGuidePolicy.GoalId, 1),
            ConfirmedNavigation = null,
            NativeNavigation = actual is null ? NativeNavigationSnapshot.Unknown :
                new(NativeNavigationStatus.Selected, actual, "synthetic current actual") };
        var decision = planner.Decide(current);
        Assert.Equal(allows, decision.Kind == CoachActionKind.Craft);
        if (actual is "PathOfKings.BountyHunter" or "PathOfKings.RoyalLoader")
            Assert.Contains("조합 후 2기", decision.Reason);
        output.WriteLine(JsonSerializer.Serialize(new { Kind = "stale-final-consumer-not-fresh-WPF",
            actual, allows, Decision = decision, OriginalSteps = frame.CraftSteps, CurrentInventory = current.Inventory }));
    }

    [Theory]
    [InlineData("e016")] [InlineData("e017")] [InlineData("e019")]
    public void ObservedRewardBeforeFirstRareMustReachDecisionWithoutInventingStoryAction(string reward)
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var item = FastUniqueUiFixture.Build(catalog).Single(c => c.Name == reward + "-received");
        var initial = new AdaptivePlanningCompositionRoot(Path.Combine(AppContext.BaseDirectory, "Data"));
        Assert.Equal(PlannerPhase.AwaitFirstRare, initial.State.Phase);
        var request = FastUniqueUiFixture.Request(item, 0, 1);
        var frame = ModelFrame(catalog, item, request, FastUniqueState.Unknown, 0, 1);
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        output.WriteLine($"phase=AwaitFirstRare; story={frame.Story?.Action}; actual={decision.Kind}; reward={decision.RewardWispId}");
        Assert.Equal(CoachActionKind.Reward, decision.Kind);
        Assert.Equal(reward, decision.RewardWispId);
    }

    [Theory]
    [InlineData("e016")] [InlineData("e017")] [InlineData("e019")]
    public void ProducedFirstRareRewardUsesCommonConsumerInEveryRegisteredModeAndKeepsGuards(string reward)
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var item = FastUniqueUiFixture.Build(catalog).Single(c => c.Name == reward + "-received");
        var produced = ModelFrame(catalog, item, FastUniqueUiFixture.Request(item, 0, 1), FastUniqueState.Unknown, 0, 1);
        var planner = new BeginnerCoachPlanner(catalog);
        // Consumer-mode characterization with real produced Story, not an accepted UI mode transition.
        foreach (var mode in Enum.GetValues<PlayMode>())
        {
            var frame = produced with { Mode = mode, GuidePlan = mode == PlayMode.Guide ? produced.GuidePlan : null };
            var decision = planner.Decide(frame);
            Assert.Equal(CoachActionKind.Reward, decision.Kind);
            Assert.Equal(reward, decision.RewardWispId);
            Assert.NotEqual(CoachActionKind.Reward, planner.Decide(frame with { IsCurrent = false }).Kind);
            Assert.NotEqual(CoachActionKind.Reward, planner.Decide(frame with { Paused = true }).Kind);
            Assert.NotEqual(CoachActionKind.Reward, planner.Decide(frame with { Outcome = "fail" }).Kind);
            Assert.NotEqual(CoachActionKind.Reward, planner.Decide(frame with
            {
                NativeNavigation = new(NativeNavigationStatus.Conflict, null, "synthetic conflict")
            }).Kind);
            Assert.NotEqual(CoachActionKind.Reward, planner.Decide(frame with
            {
                RewardWisps = ImmutableDictionary<string, int>.Empty
            }).Kind);
        }
    }

    private static CoachFrame ModelFrame(DataCatalog catalog, FastUniqueUiCase item, RecognitionResult request,
        FastUniqueState history, long generation, long revision)
    {
        var inventory = request.Entries.ToImmutableDictionary(e => e.UnitId, e => e.Count);
        var actual = item.Navigation.Resolve(null);
        var plan = new BulletGuidePolicy(catalog).Plan(item.Round, item.Story, inventory, "악몽", actual,
            fastUnique: history, selectionWisps: item.Wisps.GetValueOrDefault("e018"));
        // Initial Guide mode keeps the adaptive phase at AwaitFirstRare. Do not synthesize SpendStoryWisps.
        var story = StoryRewardSequencePlanner.Evaluate(new StoryRewardSequenceInput
        {
            Phase = PlannerPhase.AwaitFirstRare, Round = item.Round, ActiveStoryStage = item.Story + 1,
            CompletedStoryStage = item.Story, RewardWisps = item.Wisps, Inventory = request.Entries,
            Units = catalog.AllUnits.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First()),
            StoryStages = MapStoryProfileLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")).Stages
        });
        var candidates = RecommendationPipeline.ComputeCandidates(new()
        {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(catalog),
            Goal = catalog.Unit(BulletGuidePolicy.GoalId), Inventory = request.Entries,
            InitialSurface = RecommendationSurface.FastRare, StorySequence = story,
            NativeNavigation = item.Navigation, NavigationMode = actual ?? "Unselected",
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽",
            Round = item.Round, CompletedStoryStage = item.Story
        });
        var final = RecommendationPipeline.Finalize(candidates, catalog, catalog.Unit(BulletGuidePolicy.GoalId),
            request.Entries, new FirstRareTargetPolicy(), item.Round, item.Story, "악몽");
        var steps = new AutoCombinePlanner(catalog, CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory,
            "Data", "tmo-combine-hotkeys.json"))).Plan(final.Recommendations, request.Entries);
        return new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, GoalId = BulletGuidePolicy.GoalId,
            GuidePlan = plan, Round = item.Round, CompletedStoryStage = item.Story,
            MatchGeneration = generation, RecognitionRevision = revision, Revision = revision, IsCurrent = true,
            Gorosei = GoroseiObservation.Unknown with { Marker = GoroseiMarkerSnapshot.Unknown },
            Difficulty = "악몽", Inventory = inventory, RewardWisps = item.Wisps,
            NativeNavigation = item.Navigation, ConfirmedNavigation = actual, Story = final.StorySequence,
            Recommendations = final.Recommendations, CraftSteps = steps,
            Signals = item.Lumber is { } lumber ? ImmutableDictionary<string, long?>.Empty.Add("lumber", lumber) : ImmutableDictionary<string, long?>.Empty
        };
    }
}
