using Xunit;

namespace OrandOverlay.Tests;

public sealed class ScanStopTransitionTests
{
    [Fact]
    public void AutomaticZeroManualInventoryOldCombatStopsActualPlanner()
    {
        var before = NasjuroWispAdvicePolicyTests.Frame() with { Mode = PlayMode.Manual };
        Assert.NotEmpty(before.Inventory); // manual inventory survives automatic0
        Assert.NotEmpty(before.CombatObservations);
        var stopped = ScanStopTransition.Apply(before, automaticCount: 0);
        Assert.Equal(CoachActionKind.Recognition,
            new BeginnerCoachPlanner(NasjuroWispAdvicePolicyTests.Catalog()).Decide(stopped).Kind);
        Assert.False(stopped.IsCurrent);
        Assert.Empty(stopped.CombatObservations);
        Assert.False(stopped.Gorosei.IsCurrent);
        Assert.Equal(GoroseiMode.None, stopped.Gorosei.EffectMode);
        Assert.Same(before.Inventory, stopped.Inventory);
        Assert.Equal(before.ConfirmedNavigation, stopped.ConfirmedNavigation);
    }

    [Theory]
    [InlineData(PlayMode.Guide, 0)]
    [InlineData(PlayMode.Beginner, 0)]
    [InlineData(PlayMode.Normal, 0)]
    [InlineData(PlayMode.Manual, 0)]
    [InlineData(PlayMode.Guide, 2)]
    [InlineData(PlayMode.Beginner, 2)]
    [InlineData(PlayMode.Normal, 2)]
    [InlineData(PlayMode.Manual, 2)]
    public void StopDetailedMarkerAndResumeRevisionFenceAcrossModes(PlayMode mode, int automaticCount)
    {
        // Characterization of the fixed transition and real pure consumers, not WPF.
        var before = NasjuroWispAdvicePolicyTests.Frame() with { Mode = mode };
        var session = new GoroseiObservationSession(); session.Reset(before.MatchGeneration);
        var marker = new GoroseiMarkerSnapshot(GoroseiMarkerStatus.SelectedIdentity,
            GoroseiMode.Nasjuro, "fixture identity; effects not verified", []);
        session.Accept(before.MatchGeneration, before.RecognitionRevision, marker, true);
        var manual = new NavigationSessionState(); manual.Confirm("AlliedForces.EmergencyCall");
        before = before with { Gorosei = session.Current,
            NativeNavigation = new(NativeNavigationStatus.Selected, BulletGuidePolicy.NavigationId, "fixture native"),
            ConfirmedNavigation = manual.ConfirmedOptionId };
        Assert.True(before.Gorosei.IsCurrent);
        var stopped = ScanStopTransition.Apply(before, automaticCount);
        Assert.False(stopped.IsCurrent);
        Assert.Empty(stopped.CombatObservations);
        Assert.Equal(NativeNavigationStatus.Unknown, stopped.NativeNavigation.Status);
        Assert.False(stopped.Gorosei.IsCurrent);
        Assert.Equal(GoroseiMarkerStatus.Unknown, stopped.Gorosei.Marker!.Status);
        Assert.Equal(GoroseiMode.None, MainWindow.ResolveObservationGorosei(mode, GoroseiMode.Nasjuro, stopped.Gorosei));
        Assert.Equal(manual.ConfirmedOptionId, stopped.NativeNavigation.Resolve(manual.ConfirmedOptionId));
        Assert.Equal(NativeNavigationPresentation.Describe(NativeNavigationSnapshot.Unknown, manual.ConfirmedOptionId),
            NativeNavigationPresentation.Describe(stopped.NativeNavigation, manual.ConfirmedOptionId));
        Assert.Same(before.Inventory, stopped.Inventory);
        Assert.Same(before.GuidePlan, stopped.GuidePlan);
        Assert.Equal(CoachActionKind.Recognition, new BeginnerCoachPlanner(NasjuroWispAdvicePolicyTests.Catalog()).Decide(stopped).Kind);
        Assert.NotEqual(NasjuroApplicability.Applicable, NasjuroWispAdvicePolicy.Evaluate(stopped).Applicability);
        session.Accept(stopped.MatchGeneration, stopped.RecognitionRevision, stopped.Gorosei.Marker, false);
        session.Accept(before.MatchGeneration, before.RecognitionRevision, marker, true);
        Assert.False(session.Current.IsCurrent); // result predating Stop rejected
        session.Accept(stopped.MatchGeneration, stopped.RecognitionRevision + 1, marker, true);
        Assert.True(session.Current.IsCurrent); // fresh scan resumes without deleting user evidence
        var resumed = before with { Gorosei = session.Current, RecognitionRevision = session.Current.RecognitionRevision };
        Assert.Equal(manual.ConfirmedOptionId, resumed.ConfirmedNavigation);
        var twice = ScanStopTransition.Apply(stopped, automaticCount);
        Assert.True(twice.RecognitionRevision > stopped.RecognitionRevision);
        session.Reset(stopped.MatchGeneration + 1);
        session.Accept(stopped.MatchGeneration, twice.RecognitionRevision + 1, marker, true);
        Assert.False(session.Current.IsCurrent); // previous match cannot resurrect detail
        Assert.Equal(GoroseiMode.None, session.LastKnown);
    }

    [Fact]
    public void SharedStopTransitionExistsWithoutConstructingAWindow()
    {
        // API/extraction contract, not the behavioral RED or a WPF event test.
        Assert.NotNull(typeof(MainWindow).Assembly.GetType("OrandOverlay.ScanStopTransition"));
    }
}
