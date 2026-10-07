using System.Collections.Immutable;
using System.Text.Json;
using OrandOverlay;
using Xunit;
namespace PlannerEvidenceCapture.Tests;

public sealed class BulletSyntheticGoroseiTests
{
    [Theory]
    [InlineData(2, true, RecognitionState.Ready, false)]
    [InlineData(49, true, RecognitionState.Ready, false)]
    [InlineData(50, true, RecognitionState.Ready, true)]
    [InlineData(60, false, RecognitionState.Ready, false)]
    [InlineData(60, true, RecognitionState.TransientReadError, false)]
    public void CaptureEnumAndLabelsCannotPromoteAndEarlyResetOrDisconnectCannotActivate(
        int round, bool explicitSynthetic, RecognitionState state, bool active)
    {
        var c = OverlayExecutionContext.SyntheticGoroseiFixture(new());
        var input = explicitSynthetic ? c.CreateSyntheticGorosei(GoroseiMode.Warcury) : null;
        var result = new RecognitionResult { State = state, Status = "SyntheticFixtureNotNative", SyntheticGorosei = input,
            Diagnostics = BulletSyntheticGoroseiProducer.Diagnostics(round, [], GoroseiMode.Warcury, null, input) };
        var s = new GoroseiObservationSession(); s.Reset(2); s.AcceptRecognition(2, 1, result, c);
        Assert.Equal(active, s.Current.EffectsActiveVerified);
        Assert.Equal(active ? GoroseiMode.Warcury : GoroseiMode.None,
            MainWindow.ResolveObservationGorosei(PlayMode.Guide, GoroseiMode.Warcury, s.Current));
    }

    [Theory]
    [InlineData(GoroseiMarkerStatus.Unknown)]
    [InlineData(GoroseiMarkerStatus.Conflict)]
    [InlineData(GoroseiMarkerStatus.SelectedIdentity)]
    public void CaptureExplicitNativeMarkerWinsOverSyntheticInputWithoutBecomingActive(GoroseiMarkerStatus status)
    {
        var c = OverlayExecutionContext.SyntheticGoroseiFixture(new());
        var input = c.CreateSyntheticGorosei(GoroseiMode.Nasjuro);
        var marker = new GoroseiMarkerSnapshot(status, GoroseiMode.Nasjuro, "SyntheticFixtureNotNative", []);
        var diagnostics = BulletSyntheticGoroseiProducer.Diagnostics(60, [], GoroseiMode.Nasjuro, marker, input);
        Assert.Same(marker, diagnostics.GoroseiMarker);
        var s = new GoroseiObservationSession(); s.Reset(1);
        s.AcceptRecognition(1, 1, new() { Diagnostics = diagnostics, SyntheticGorosei = input }, c);
        Assert.False(s.Current.IsSynthetic); Assert.False(s.Current.EffectsActiveVerified);
    }

    [Theory]
    [InlineData(GoroseiMode.Nasjuro)]
    [InlineData(GoroseiMode.Warcury)]
    [InlineData(GoroseiMode.Saturn)]
    public void ExplicitCaptureProducerReachesDetailedSessionPlanAndRealJson(GoroseiMode mode)
    {
        var context = OverlayExecutionContext.SyntheticGoroseiFixture(new());
        var input = context.CreateSyntheticGorosei(mode);
        var result = new RecognitionResult {
            SyntheticGorosei = input,
            Diagnostics = BulletSyntheticGoroseiProducer.Diagnostics(60, [], mode, null, input)
        };
        var s = new GoroseiObservationSession(); s.Reset(1); s.AcceptRecognition(1, 1, result, context);
        Assert.Equal(mode, MainWindow.ResolveObservationGorosei(PlayMode.Guide, GoroseiMode.None, s.Current));
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var inventory = new[] { "180h", "U30h", "540h" }.ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(60, 13, inventory, "악몽", BulletGuidePolicy.NavigationId,
            MainWindow.ResolveGuidePlanningSelection(s.Current, 1, 1).Mode);
        if (mode == GoroseiMode.Warcury) Assert.Equal(120, plan.Support!.ArmorTarget);
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = 60, CompletedStoryStage = 13,
            MatchGeneration = 1, Revision = 1, RecognitionRevision = 1, Inventory = inventory,
            Gorosei = s.Current, IsCurrent = true, Difficulty = "악몽", GuidePlan = plan };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        var json = JsonSerializer.SerializeToElement(new { Rows = new List<object> { BulletGuideRowProjection.Observe("explicit-synthetic", decision, frame) } });
        var evidence = Environment.GetEnvironmentVariable("BULLET_SYNTHETIC_EVIDENCE");
        if (evidence is not null) File.WriteAllText(Path.Combine(evidence, $"synthetic-row-{mode}.json"),
            JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
        var observed = json.GetProperty("Rows")[0].GetProperty("Gorosei");
        Assert.Equal("SyntheticFixtureNotNative", observed.GetProperty("Source").GetString());
        Assert.True(observed.GetProperty("EffectsActiveVerified").GetBoolean());
        Assert.False(observed.GetProperty("Marker").GetProperty("EffectsActiveVerified").GetBoolean());
        Assert.Equal(s.Current.EffectEvidence, observed.GetProperty("EffectEvidence").GetString());
    }
}
