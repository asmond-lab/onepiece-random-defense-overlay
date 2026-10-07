using System.Collections.Immutable;
using System.Text.Json;
using OrandOverlay;
using Xunit;
namespace PlannerEvidenceCapture.Tests;
public sealed class BulletQueenExactFixtureTests
{
    [Theory]
    [InlineData(QueenConversionInput.Unknown)]
    [InlineData(QueenConversionInput.UserConfirmedMissionsComplete)]
    [InlineData(QueenConversionInput.UserConfirmedStoryTooSlow)]
    public void ExactRound60QueenFixtureKeepsExplicitInputGate(QueenConversionInput input)
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var inventory = new[] { "180h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Q30h", "K50h", "HA0h", "I20h", "L00h" }
            .ToImmutableDictionary(c => "rawcode:" + c, _ => 1).Add("luffy_common", 20);
        var plan = new BulletGuidePolicy(catalog).Plan(60, 13, inventory, "악몽", BulletGuidePolicy.NavigationId, queenInput: input);
        var entries = inventory.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }).ToArray();
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys),
            Goal = catalog.Unit(BulletGuidePolicy.GoalId), Inventory = entries,
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = BulletGuidePolicy.NavigationId,
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 60, CompletedStoryStage = 13 });
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(candidates.Recommendations, entries, protectedUnitIds: plan.ProtectedUnitIds);
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = 60, CompletedStoryStage = 13,
            MatchGeneration = 0, RecognitionRevision = 1, Revision = 1, IsCurrent = true, Inventory = inventory,
            Difficulty = "악몽", GuidePlan = plan, GoalId = BulletGuidePolicy.GoalId,
            Signals = ImmutableDictionary<string,long?>.Empty.Add("lumber", 0),
            ConfirmedNavigation = BulletGuidePolicy.NavigationId, CraftSteps = steps, Recommendations = candidates.Recommendations };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        var path = Environment.GetEnvironmentVariable("BULLET_FINAL_EVIDENCE");
        if (path is not null) File.WriteAllText(Path.Combine(path, $"queen-{input}.json"), JsonSerializer.Serialize(new {
            input, inventory, plan, steps, decision }, new JsonSerializerOptions { WriteIndented = true }));
        if (input == QueenConversionInput.Unknown)
        {
            Assert.False(plan.QueenConversionConfirmed);
            Assert.DoesNotContain(steps, s => s.TargetUnitId == "rawcode:IC0h");
            Assert.False(decision.Kind == CoachActionKind.Craft && decision.TargetUnitId == "rawcode:IC0h");
        }
        else
        {
            Assert.True(plan.QueenConversionConfirmed);
            Assert.Equal(CoachActionKind.Craft, decision.Kind);
            Assert.Equal("rawcode:IC0h", decision.TargetUnitId);
        }
    }
}
