using System.Text.Json;
using Xunit;
namespace OrandOverlay.Tests;

public sealed class SyntheticGoroseiEffectTests
{
    [Fact]
    public void ExplicitSyntheticCurrentDetailedInputReachesEffectConsumerAtRound50()
    {
        var context = OverlayExecutionContext.SyntheticGoroseiFixture(new AppSettings());
        var input = context.CreateSyntheticGorosei(GoroseiMode.Nasjuro);
        var session = new GoroseiObservationSession(); session.Reset(1);
        session.AcceptRecognition(1, 1, Result(input, 50), context);
        var effective = MainWindow.ResolveObservationGorosei(PlayMode.Guide, GoroseiMode.None, session.Current);
        Assert.Equal(GoroseiMode.Nasjuro, effective);
        Assert.False(session.Current.Marker!.EffectsActiveVerified);
        var frame = NasjuroWispAdvicePolicyTests.Frame() with {
            Round = 50, Gorosei = session.Current, GoalId = BulletGuidePolicy.GoalId };
        frame = frame with { ShipReservations = ShipReservationPolicy.Evaluate(frame, NasjuroWispAdvicePolicyTests.Catalog()) };
        Assert.Equal(GoroseiMode.Nasjuro, frame.CurrentGoroseiEffect);
        Assert.Equal(BulletGuideAdvice.Operation(frame),
            new BeginnerCoachPlanner(NasjuroWispAdvicePolicyTests.Catalog()).Decide(frame).OperationGuide);
        Assert.Equal("SyntheticFixtureNotNative", session.Current.Source);
    }

    [Theory]
    [InlineData(0, 50, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 49, false)]
    [InlineData(1, 50, true)]
    [InlineData(1, 51, true)]
    [InlineData(1, null, false)]
    public void ActivationRequiresKnownCurrentMatchAndRound50Start(long generation, int? round, bool active)
    {
        var context = OverlayExecutionContext.SyntheticGoroseiFixture(new());
        var input = context.CreateSyntheticGorosei(GoroseiMode.Warcury);
        var s = new GoroseiObservationSession(); s.Reset(generation);
        s.AcceptRecognition(generation, 1, Result(input, round), context);
        Assert.Equal(active, s.Current.EffectsActiveVerified);
        var effective = MainWindow.ResolveObservationGorosei(PlayMode.Guide, GoroseiMode.Warcury, s.Current);
        var strategy = new GoalStrategyProfile(1, 1, ArmorReductionTarget: 100, MagicArmorReductionTarget: 30);
        Assert.Equal(active ? GoalStrategyCalculator.ApplyGorosei(strategy, GoroseiMode.Warcury) : strategy,
            GoalStrategyCalculator.ApplyGorosei(strategy, effective));
    }

    [Fact]
    public void ContextIdentityNotNamesSettingsOrPublicJsonControlsAuthority()
    {
        var issuer = OverlayExecutionContext.SyntheticGoroseiFixture(new());
        var input = issuer.CreateSyntheticGorosei(GoroseiMode.Nasjuro);
        var result = Result(input, 60);
        foreach (var other in new[] { OverlayExecutionContext.Production(Path.GetTempPath()),
            OverlayExecutionContext.Fixture(new() { GoroseiMode = "Nasjuro" }),
            OverlayExecutionContext.FixtureWithMemoryJournal(new()),
            OverlayExecutionContext.SyntheticGoroseiFixture(new()) })
        {
            var s = new GoroseiObservationSession(); s.Reset(1); s.AcceptRecognition(1, 1, result, other);
            Assert.False(s.Current.EffectsActiveVerified);
        }
        Assert.Throws<InvalidOperationException>(() => OverlayExecutionContext.Production(Path.GetTempPath()).CreateSyntheticGorosei(GoroseiMode.Nasjuro));
        Assert.Throws<InvalidOperationException>(() => OverlayExecutionContext.Fixture(new()).CreateSyntheticGorosei(GoroseiMode.Nasjuro));
        Assert.Throws<InvalidOperationException>(() => OverlayExecutionContext.FixtureWithMemoryJournal(new()).CreateSyntheticGorosei(GoroseiMode.Nasjuro));
        Assert.Throws<ArgumentOutOfRangeException>(() => issuer.CreateSyntheticGorosei(GoroseiMode.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => issuer.CreateSyntheticGorosei((GoroseiMode)999));
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("\"SyntheticGorosei\"", json);
        var replay = JsonSerializer.Deserialize<RecognitionResult>(json)!;
        var session = new GoroseiObservationSession(); session.Reset(1);
        session.AcceptRecognition(1, 1, replay, issuer);
        Assert.False(session.Current.EffectsActiveVerified);
        var forged = JsonSerializer.Deserialize<RecognitionResult>("{\"SyntheticGorosei\":{\"Mode\":3},\"EffectsActiveVerified\":true,\"Source\":\"SyntheticFixtureNotNative\"}")!;
        session.AcceptRecognition(1, 2, forged, issuer);
        Assert.False(session.Current.EffectsActiveVerified);
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"GoroseiMode\":\"Nasjuro\",\"SyntheticGorosei\":true,\"EffectsActiveVerified\":true}")!;
        issuer.SaveSettings(settings);
        Assert.Throws<InvalidOperationException>(() => OverlayExecutionContext.Fixture(issuer.LoadSettings()).CreateSyntheticGorosei(GoroseiMode.Nasjuro));
    }

    [Theory]
    [InlineData(GoroseiMarkerStatus.Unknown)]
    [InlineData(GoroseiMarkerStatus.Conflict)]
    [InlineData(GoroseiMarkerStatus.SelectedIdentity)]
    public void CapabilityCannotOverwriteNativeUnknownConflictOrSeparateIdentity(GoroseiMarkerStatus status)
    {
        var c = OverlayExecutionContext.SyntheticGoroseiFixture(new());
        var i = c.CreateSyntheticGorosei(GoroseiMode.Nasjuro);
        var marker = i.Marker with { Status = status };
        var s = new GoroseiObservationSession(); s.Reset(1);
        s.AcceptRecognition(1, 1, Result(i, 60, marker: marker), c);
        Assert.Same(marker, s.Current.Marker);
        Assert.False(s.Current.IsSynthetic);
        Assert.False(s.Current.EffectsActiveVerified);
        foreach (var mode in Enum.GetValues<PlayMode>())
            Assert.Equal(GoroseiMode.None, MainWindow.ResolveObservationGorosei(mode, GoroseiMode.Nasjuro, s.Current));
    }

    [Fact]
    public void IdentityOnlyDisconnectResetAndRevisionFenceNeverRetainSyntheticApproval()
    {
        var c = OverlayExecutionContext.SyntheticGoroseiFixture(new());
        var i = c.CreateSyntheticGorosei(GoroseiMode.Nasjuro);
        var s = new GoroseiObservationSession(); s.Reset(1);
        s.AcceptRecognition(1, 1, Result(i, 50), c); Assert.True(s.Current.EffectsActiveVerified);
        s.Accept(1, 2, i.Marker, true); Assert.False(s.Current.EffectsActiveVerified);
        s.AcceptRecognition(1, 2, Result(i, 60), c); Assert.False(s.Current.EffectsActiveVerified);
        s.AcceptRecognition(1, 3, Result(i, 60), c); Assert.True(s.Current.EffectsActiveVerified);
        s.AcceptRecognition(1, 4, Result(i, 60, RecognitionState.TransientReadError), c);
        Assert.False(s.Current.EffectsActiveVerified); Assert.False(s.Current.IsCurrent);
        s.AcceptRecognition(1, 5, Result(i, 60), c);
        s.Reset(2); Assert.False(s.Current.EffectsActiveVerified);
        s.AcceptRecognition(1, 99, Result(i, 60), c); Assert.False(s.Current.EffectsActiveVerified);
        s.AcceptRecognition(2, 1, Result(i, 2), c); Assert.False(s.Current.EffectsActiveVerified);
        s.AcceptRecognition(2, 2, Result(i, 50), c); Assert.True(s.Current.EffectsActiveVerified);
        var frame = NasjuroWispAdvicePolicyTests.Frame() with { MatchGeneration = 2, RecognitionRevision = 3, Round = 50, Gorosei = s.Current };
        Assert.NotEqual(NasjuroApplicability.Applicable, NasjuroWispAdvicePolicy.Evaluate(frame).Applicability);
        var replay = JsonSerializer.Deserialize<GoroseiObservation>(JsonSerializer.Serialize(s.Current))!;
        Assert.False(replay.IsSynthetic); Assert.False(replay.EffectsActiveVerified);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(49)]
    [InlineData(51)]
    public void SyntheticCurrentEffectDisplayRejectsDifferentFrameRound(int round)
    {
        var c = OverlayExecutionContext.SyntheticGoroseiFixture(new());
        var i = c.CreateSyntheticGorosei(GoroseiMode.Nasjuro);
        var s = new GoroseiObservationSession(); s.Reset(1); s.AcceptRecognition(1, 1, Result(i, 50), c);
        var f = NasjuroWispAdvicePolicyTests.Frame() with { Round = round, Gorosei = s.Current };
        // Latest correction: the selection remains usable for planning; only current effect is fenced.
        Assert.Equal(GoroseiMode.None, f.CurrentGoroseiEffect);
        Assert.Equal(GoroseiMode.Nasjuro, f.PlanningGorosei.Mode);
        Assert.NotEqual(BulletGuideAdvice.Operation(f with { Round = 50 }), BulletGuideAdvice.Operation(f));
    }

    [Fact]
    public void RealMemoryReaderIdentityPlansWithoutInventingActiveEffectBridge()
    {
        var memory = new BulletGoroseiNativeTests.MarkerMemory();
        var marker = memory.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
            [(BulletGoroseiNativeTests.MarkerMemory.Unit, 0x6f303332)]);
        Assert.Equal(GoroseiMarkerStatus.SelectedIdentity, marker.Status);
        var c = OverlayExecutionContext.SyntheticGoroseiFixture(new());
        var i = c.CreateSyntheticGorosei(GoroseiMode.Nasjuro);
        var s = new GoroseiObservationSession(); s.Reset(1);
        s.AcceptRecognition(1, 1, Result(i, 50, marker: marker), c);
        Assert.Same(marker, s.Current.Marker); Assert.False(s.Current.EffectsActiveVerified);
        var f = NasjuroWispAdvicePolicyTests.Frame() with { Round = 50, Gorosei = s.Current };
        Assert.Equal(GoroseiMode.None, f.CurrentGoroseiEffect);
        Assert.Equal(GoroseiMode.Nasjuro, f.PlanningGorosei.Mode);
    }

    internal static RecognitionResult Result(OverlayExecutionContext.SyntheticGoroseiInput input, int? round,
        RecognitionState state = RecognitionState.Ready, GoroseiMarkerSnapshot? marker = null) => new()
    {
        State = state, SyntheticGorosei = input,
        Diagnostics = new() { GoroseiMarker = marker ?? input.Marker,
            MapState = round is { } r ? new MapStateSample(r, 0, "악몽") : null }
    };
}
