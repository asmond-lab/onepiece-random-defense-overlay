using System.Collections.Immutable;
using System.Text.Json;
using OrandOverlay;
using Xunit;
namespace PlannerEvidenceCapture.Tests;

public sealed class BulletInitialSessionTests
{
    [Theory]
    [InlineData(0, 1)] [InlineData(0, 2)] [InlineData(0, 49)] [InlineData(0, 50)] [InlineData(0, 60)]
    [InlineData(1, 1)] [InlineData(1, 2)] [InlineData(1, 49)] [InlineData(1, 50)] [InlineData(1, 60)]
    public void FreshIdentityPlansCompletedDeckAtAllRoundsWithoutActivatingEffect(long generation, int round)
    {
        var session = new GoroseiObservationSession();
        if (generation != 0) session.Reset(generation);
        var marker = new GoroseiMarkerSnapshot(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Warcury, "identity only", []);
        session.AcceptRecognition(generation, 1, new RecognitionResult { State = RecognitionState.Ready,
            Diagnostics = BulletSyntheticGoroseiProducer.Diagnostics(round, [], GoroseiMode.Warcury, marker, null) },
            OverlayExecutionContext.Fixture(new()));
        var selection = MainWindow.ResolveGuidePlanningSelection(session.Current, generation, 1);
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var inventory = new[] { "180h", "U30h", "540h" }.ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(round, 13, inventory, "악몽", BulletGuidePolicy.NavigationId, selection.Mode);
        Assert.Equal(GoroseiMode.Warcury, selection.Mode);
        Assert.Equal(120, plan.Support!.ArmorTarget);
        Assert.False(session.Current.EffectsActiveVerified);
        Assert.Equal(GoroseiMode.None, session.Current.EffectMode);
        foreach (var fault in new[] { session.Current with { IsCurrent = false },
            session.Current with { MatchGeneration = generation + 1 }, session.Current with { RecognitionRevision = 2 },
            session.Current with { Marker = marker with { Status = GoroseiMarkerStatus.Conflict } },
            session.Current with { Marker = GoroseiMarkerSnapshot.Unknown }, GoroseiObservation.Unknown })
        {
            Assert.False(MainWindow.ResolveGuidePlanningSelection(fault, generation, 1).IsKnown);
            foreach (var source in new[] { "UserPlan", "SavedUserPlan" })
                Assert.Equal(GoroseiMode.Saturn, MainWindow.ResolveGuidePlanningSelection(fault, generation, 1,
                    new(GoroseiMode.Saturn, source)).Mode);
        }
        Assert.False(MainWindow.ResolveGuidePlanningSelection(session.Current, generation, 0).IsKnown);
        Assert.False(MainWindow.ResolveGuidePlanningSelection(session.Current with { MatchGeneration = -1 }, -1, 1).IsKnown);
        var path = Environment.GetEnvironmentVariable("BULLET_FINAL_EVIDENCE");
        if (path is not null) File.WriteAllText(Path.Combine(path, $"matrix-{generation}-{round}.json"),
            JsonSerializer.Serialize(new { generation, round, selection, plan, observation = session.Current }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void InitialProducerSaturnExactInventoryReachesRealConsumerPolicyPipelineWithoutInventingCraft()
    {
        var generation = new AdaptivePlanningCoordinator().MatchGeneration;
        Assert.Equal(0, generation);
        var session = new GoroseiObservationSession();
        var marker = new GoroseiMarkerSnapshot(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Saturn,
            "Controlled planning identity; current effects unverified", []);
        session.AcceptRecognition(generation, 1, new RecognitionResult { State = RecognitionState.Ready,
            Diagnostics = BulletSyntheticGoroseiProducer.Diagnostics(60, [], GoroseiMode.Saturn, marker, null) },
            OverlayExecutionContext.Fixture(new()));
        var selection = MainWindow.ResolveGuidePlanningSelection(session.Current, generation, 1);
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var inventory = new[] { "180h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Y30h", "Q30h", "K50h" }
            .ToImmutableDictionary(c => "rawcode:" + c, _ => 1).Add("luffy_common", 20);
        var plan = new BulletGuidePolicy(catalog).Plan(60, 13, inventory, "악몽", BulletGuidePolicy.NavigationId, selection.Mode);
        var entries = inventory.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }).ToArray();
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys),
            Goal = catalog.Unit(BulletGuidePolicy.GoalId), Inventory = entries,
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = BulletGuidePolicy.NavigationId,
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 60, CompletedStoryStage = 13 });
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(candidates.Recommendations, entries, protectedUnitIds: plan.ProtectedUnitIds);
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = 60, CompletedStoryStage = 13,
            MatchGeneration = generation, RecognitionRevision = 1, Revision = 1, IsCurrent = true, Inventory = inventory,
            Difficulty = "악몽", Gorosei = session.Current, GuidePlan = plan, GoalId = BulletGuidePolicy.GoalId,
            ConfirmedNavigation = BulletGuidePolicy.NavigationId, CraftSteps = steps, Recommendations = candidates.Recommendations };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        var path = Environment.GetEnvironmentVariable("BULLET_FINAL_EVIDENCE");
        if (path is not null) File.WriteAllText(Path.Combine(path, "initial-saturn.json"), JsonSerializer.Serialize(new {
            generation, selection, inventory, plan, candidates, steps, decision, row = BulletGuideRowProjection.Observe("saturn-chopper", decision, frame)
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(GoroseiMode.Saturn, selection.Mode);
        Assert.Equal("rawcode:K20h", plan.TargetUnitId);
        Assert.Equal(BulletGuideStage.ControlSupport, plan.Stage);
        Assert.False(session.Current.EffectsActiveVerified);
        Assert.Equal(GoroseiMode.None, frame.CurrentGoroseiEffect);
        Assert.NotEqual(CoachActionKind.Craft, decision.Kind);
        Assert.DoesNotContain(steps, s => s.TargetUnitId == "rawcode:K20h");
    }
}
