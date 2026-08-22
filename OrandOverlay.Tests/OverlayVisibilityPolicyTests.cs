using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

/// <summary>
/// 신세계 라운드 진입 시 오버레이가 꺼졌다 켜졌다 반복하던 깜빡임 버그의 회귀 방지.
/// 가시성 결정은 OverlayVisibilityPolicy.Decide 하나로 이뤄진다:
/// - 추천 가능(Ready+보유 패) → 즉시 표시
/// - 세션 경계 확정(Waiting) 또는 치명 상태(미검증 프로필 등) → 즉시 숨김
/// - 그 외 일시적 상태(TransientReadError 등) → 연속 HiddenStreakThreshold회 쌓였을 때만 숨김
/// </summary>
public sealed class OverlayVisibilityPolicyTests
{
    private static RecognitionResult Result(RecognitionState state, bool sessionBoundary = false,
        string unitId = "mobydick", int count = 1) => new()
    {
        State = state,
        ConfirmsSessionBoundary = sessionBoundary,
        Entries = count > 0 ? [new InventoryEntry { UnitId = unitId, Count = count }] : []
    };

    [Fact]
    public void Ready_WithEntries_AlwaysShows()
    {
        var decision = OverlayVisibilityPolicy.Decide(false,
            Result(RecognitionState.Ready), hiddenStreakCount: 2);
        Assert.Equal(OverlayVisibilityDecision.Show, decision);
    }

    [Fact]
    public void TransientBlip_WhileShown_KeepsOverlayVisible()
    {
        var decision = OverlayVisibilityPolicy.Decide(true,
            Result(RecognitionState.TransientReadError), hiddenStreakCount: 1);
        Assert.Equal(OverlayVisibilityDecision.KeepShown, decision);
    }

    [Fact]
    public void TransientBlip_BelowThreshold_WhileHidden_StaysHidden()
    {
        var decision = OverlayVisibilityPolicy.Decide(false,
            Result(RecognitionState.TransientReadError), hiddenStreakCount: 2);
        Assert.Equal(OverlayVisibilityDecision.KeepHidden, decision);
    }

    [Fact]
    public void TransientStreak_AtThreshold_Hides()
    {
        var decision = OverlayVisibilityPolicy.Decide(true,
            Result(RecognitionState.TransientReadError),
            hiddenStreakCount: OverlayVisibilityPolicy.HiddenStreakThreshold);
        Assert.Equal(OverlayVisibilityDecision.Hide, decision);
    }

    [Fact]
    public void SessionBoundaryWaiting_HidesImmediately_EvenOnFirstTick()
    {
        var decision = OverlayVisibilityPolicy.Decide(true,
            Result(RecognitionState.Waiting, sessionBoundary: true, count: 0),
            hiddenStreakCount: 1);
        Assert.Equal(OverlayVisibilityDecision.Hide, decision);
    }

    [Theory]
    [InlineData(RecognitionState.UnverifiedProfile)]
    [InlineData(RecognitionState.Unsupported)]
    [InlineData(RecognitionState.ConfigurationError)]
    public void FatalStates_HideImmediately(RecognitionState state)
    {
        var decision = OverlayVisibilityPolicy.Decide(true, Result(state),
            hiddenStreakCount: 1);
        Assert.Equal(OverlayVisibilityDecision.Hide, decision);
    }

    [Fact]
    public void NewWorldFlickerSequence_NeverHides()
    {
        // 버그 재현 시퀀스: 신세계 라운드 진입 구간에서 Ready와 TransientReadError가
        // 교차할 때(스캔 0.8초 주기), 단발 블립으로는 절대 숨기지 않아야 한다.
        var sequence = new[]
        {
            Result(RecognitionState.Ready),
            Result(RecognitionState.TransientReadError),
            Result(RecognitionState.Ready),
            Result(RecognitionState.TransientReadError),
            Result(RecognitionState.Ready),
            Result(RecognitionState.TransientReadError),
            Result(RecognitionState.TransientReadError),
            Result(RecognitionState.Ready)
        };
        var shown = true;
        var streak = 0;
        foreach (var result in sequence)
        {
            if (!OverlayVisibilityPolicy.ShouldShow(result)) streak++; else streak = 0;
            var decision = OverlayVisibilityPolicy.Decide(shown, result, streak);
            Assert.NotEqual(OverlayVisibilityDecision.Hide, decision);
            switch (decision)
            {
                case OverlayVisibilityDecision.Show: shown = true; break;
                case OverlayVisibilityDecision.Hide: shown = false; break;
                case OverlayVisibilityDecision.KeepShown: shown = true; break;
                case OverlayVisibilityDecision.KeepHidden: shown = false; break;
            }
        }
        Assert.True(shown, "깜빡임 시퀀스가 끝난 뒤에는 오버레이가 살아 있어야 한다");
    }
}
