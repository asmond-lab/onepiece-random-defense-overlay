namespace OrandOverlay;

/// <summary>가시성 결정 — 스캔 결과를 히스테리시스로 해석한 결과.</summary>
public enum OverlayVisibilityDecision
{
    /// <summary>조건 충족 — 즉시 표시(사용자 숨김도 다시 연다).</summary>
    Show,
    /// <summary>일시적 블립 — 표시 중이면 그대로 둔다.</summary>
    KeepShown,
    /// <summary>아직 숨김 조건 미충족 — 숨겨진 상태를 유지한다.</summary>
    KeepHidden,
    /// <summary>즉시 숨김 — 세션 경계 확정 또는 치명적 인식 불가 상태.</summary>
    Hide
}

public static class OverlayVisibilityPolicy
{
    /// <summary>연속 비표시 판정 이 횟수만큼 쌓여야 숨긴다(0.8초 스캔 × 3 ≈ 2.4초).
    /// 신세계 라운드 진입처럼 Ready와 일시 읽기 오류가 교차하는 구간의 깜빡임 방지용.</summary>
    public const int HiddenStreakThreshold = 2;

    public static bool ShouldShow(RecognitionResult result) =>
        result.State == RecognitionState.Ready &&
        result.ShouldReplaceInventory &&
        result.Entries.Any(entry => entry.Count > 0);

    /// <summary>판 종료 확정(Waiting 세션 경계) 또는 치명적 인식 불가 상태면 즉시 숨긴다.</summary>
    public static bool ShouldForceHide(RecognitionResult result) =>
        result.State is RecognitionState.UnverifiedProfile
                     or RecognitionState.Unsupported
                     or RecognitionState.ConfigurationError;

    public static bool ShouldCountTowardHide(RecognitionResult result) =>
        result.State == RecognitionState.Waiting &&
        result.ConfirmsSessionBoundary;

    /// <summary>스캔 결과 하나를 현재 가시성·연속 비표시 횟수와 함께 해석한다.
    /// 순수 함수 — WPF 의존 없이 단위테스트 가능.</summary>
    public static OverlayVisibilityDecision Decide(bool shownNow,
        RecognitionResult result, int hiddenStreakCount)
    {
        if (ShouldShow(result)) return OverlayVisibilityDecision.Show;
        if (ShouldForceHide(result)) return OverlayVisibilityDecision.Hide;
        // 대전 중 transient는 몇 번 이어져도 마지막 정상 패·가시성을 유지한다.
        if (result.State == RecognitionState.TransientReadError)
            return shownNow
                ? OverlayVisibilityDecision.KeepShown
                : OverlayVisibilityDecision.KeepHidden;
        // 세션 경계 Waiting은 2회 연속 확인한 뒤에만 숨긴다.
        return hiddenStreakCount >= HiddenStreakThreshold
            ? OverlayVisibilityDecision.Hide
            : shownNow ? OverlayVisibilityDecision.KeepShown : OverlayVisibilityDecision.KeepHidden;
    }
}

public readonly record struct OverlayVisibilityState(bool HandAvailable, bool HiddenByUser)
{
    public bool ShouldShow => HandAvailable && !HiddenByUser;

    public OverlayVisibilityState WithHandAvailability(bool available) =>
        this with { HandAvailable = available };

    public OverlayVisibilityState HideByUser() =>
        this with { HiddenByUser = true };

    public OverlayVisibilityState ShowByUser() =>
        this with { HiddenByUser = false };
}
