using Xunit;

namespace OrandOverlay.Tests;

public sealed class GoroseiObservationSessionTests
{
    [Theory]
    [InlineData(GoroseiMode.Warcury)][InlineData(GoroseiMode.Nasjuro)][InlineData(GoroseiMode.None)]
    public void FirstReadyBoundaryKeepsOnlyNewMatchObservation(GoroseiMode fresh)
    {
        var session = new GoroseiObservationSession(); session.Reset(1);
        session.Accept(1,1,GoroseiMode.Nasjuro,true);
        var result = new RecognitionResult { State = RecognitionState.Ready, ConfirmsSessionBoundary = true };
        Assert.True(RecognitionPolicy.ShouldResetBeforeReadyInventory(result));
        session.Reset(2);
        session.Accept(2,2,fresh,result.ShouldReplaceInventory);
        Assert.Equal(fresh,session.Current.Mode);
        Assert.Equal(fresh != GoroseiMode.None,session.Current.IsCurrent);
        session.Accept(1,99,GoroseiMode.Saturn,true);
        Assert.Equal(fresh,session.Current.Mode);
    }
    [Theory]
    [InlineData(RecognitionState.Waiting)][InlineData(RecognitionState.TransientReadError)]
    public void WaitingAndErrorClearBeforeConfirmedReset(RecognitionState state)
    {
        var session = new GoroseiObservationSession(); session.Reset(1);
        session.Accept(1,1,GoroseiMode.Nasjuro,true);
        var result = new RecognitionResult { State = state, ConfirmsSessionBoundary = true };
        session.Accept(1,2,GoroseiMode.Nasjuro,state == RecognitionState.Ready && result.ShouldReplaceInventory);
        Assert.False(session.Current.IsCurrent);
        Assert.False(RecognitionPolicy.ShouldResetMatch(result,1));
        if (state == RecognitionState.Waiting) Assert.True(RecognitionPolicy.ShouldResetMatch(result,2));
        session.Accept(1,2,GoroseiMode.Nasjuro,true);
        Assert.False(session.Current.IsCurrent);
    }
    [Fact]
    public void CurrentObservationIsSeparateFromLastKnownAndClearsOnNone()
    {
        var type = typeof(CoachFrame).Assembly.GetType("OrandOverlay.GoroseiObservationSession");
        Assert.NotNull(type);
        dynamic session = Activator.CreateInstance(type)!;
        session.Reset(1L);
        session.Accept(1L, 1L, GoroseiMode.Nasjuro, true);
        Assert.Equal(GoroseiMode.Nasjuro, (GoroseiMode)session.Current.Mode);
        session.Accept(1L, 2L, GoroseiMode.None, true);
        Assert.False((bool)session.Current.IsCurrent);
        Assert.Equal(GoroseiMode.None, (GoroseiMode)session.Current.Mode);
        Assert.Equal(GoroseiMode.Nasjuro, (GoroseiMode)session.LastKnown);
        session.Accept(1L, 1L, GoroseiMode.Nasjuro, true);
        Assert.False((bool)session.Current.IsCurrent);
        session.Reset(2L);
        session.Accept(1L, 9L, GoroseiMode.Nasjuro, true);
        Assert.False((bool)session.Current.IsCurrent);
        session.Accept(2L, 3L, GoroseiMode.Warcury, true);
        Assert.True((bool)session.Current.IsCurrent);
        session.Accept(2L, 4L, GoroseiMode.Warcury, false);
        Assert.False((bool)session.Current.IsCurrent);
    }
}
