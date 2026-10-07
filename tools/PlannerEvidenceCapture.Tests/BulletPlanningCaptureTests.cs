using System.Collections.Immutable;
using System.Text.Json;
using OrandOverlay;
using Xunit;
namespace PlannerEvidenceCapture.Tests;

public sealed class BulletPlanningCaptureTests
{
    [Theory]
    [InlineData(2, GoroseiMode.Warcury)] [InlineData(49, GoroseiMode.Warcury)] [InlineData(50, GoroseiMode.Warcury)]
    [InlineData(2, GoroseiMode.Nasjuro)] [InlineData(49, GoroseiMode.Nasjuro)] [InlineData(50, GoroseiMode.Nasjuro)]
    public void IdentityOnlyProducerPlansAndSerializesSeparateCurrentEffect(int round, GoroseiMode mode)
    {
        var c = OverlayExecutionContext.Fixture(new());
        var marker = new GoroseiMarkerSnapshot(GoroseiMarkerStatus.SelectedIdentity, mode,
            "Controlled identity only · native activation unverified", []);
        var result = new RecognitionResult { State = RecognitionState.Ready,
            Diagnostics = BulletSyntheticGoroseiProducer.Diagnostics(round, [], mode, marker, null) };
        var s = new GoroseiObservationSession(); s.Reset(1); s.AcceptRecognition(1, 1, result, c);
        Assert.False(s.Current.IsSynthetic); Assert.False(s.Current.EffectsActiveVerified);
        var selection = MainWindow.ResolveGuidePlanningSelection(s.Current, 1, 1);
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var inventory = new[] { "180h", "U30h", "540h", "3A0h" }.ToImmutableDictionary(code => "rawcode:" + code, _ => 1);
        var f = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = round, CompletedStoryStage = 13,
            MatchGeneration = 1, Revision = 1, RecognitionRevision = 1, IsCurrent = true, Inventory = inventory,
            Difficulty = "악몽", Gorosei = s.Current, GoalId = BulletGuidePolicy.GoalId,
            GuidePlan = new BulletGuidePolicy(catalog).Plan(round, 13, inventory, "악몽", null, selection.Mode),
            CombatObservations = [new(1,"h0A3",0,null,CombatUnitKind.LocalUnit,null,100,100,null,false,false)],
            RewardWisps = ImmutableDictionary<string,int>.Empty.Add("e01A",1) };
        f = f with { ShipReservations = ShipReservationPolicy.Evaluate(f, catalog) };
        var d = new BeginnerCoachPlanner(catalog).Decide(f);
        Assert.Equal(mode == GoroseiMode.Warcury ? 120 : 100, f.GuidePlan.Support!.ArmorTarget);
        if (mode == GoroseiMode.Nasjuro) Assert.Contains("8:2", d.OperationGuide);
        var json = JsonSerializer.SerializeToElement(new { Rows = new List<object> { BulletGuideRowProjection.Observe("planning", d, f) } });
        var row = json.GetProperty("Rows")[0];
        Assert.True(row.TryGetProperty("PlanningGorosei", out var planning), "Missing separate planning selection in capture row");
        Assert.Equal((int)mode, planning.GetProperty("Mode").GetInt32());
        Assert.Equal("CurrentSelectedIdentity", planning.GetProperty("Source").GetString());
        Assert.Equal((int)GoroseiMode.None, row.GetProperty("CurrentGoroseiEffect").GetInt32());
        Assert.False(row.GetProperty("Gorosei").GetProperty("EffectsActiveVerified").GetBoolean());
        var path = Environment.GetEnvironmentVariable("BULLET_PLANNING_EVIDENCE");
        if (path is not null) File.WriteAllText(Path.Combine(path,$"planning-{round}-{mode}.json"),
            JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
    }
}
