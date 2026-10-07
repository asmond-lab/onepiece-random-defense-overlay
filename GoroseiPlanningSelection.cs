namespace OrandOverlay;

/// <summary>Completed-deck selection, never permission to apply current combat effects.</summary>
public sealed record GoroseiPlanningSelection(GoroseiMode Mode, string Source)
{
    public static GoroseiPlanningSelection Unknown { get; } = new(GoroseiMode.None, "Unknown");
    public bool IsKnown => Mode != GoroseiMode.None && Enum.IsDefined(Mode);
    public string Describe() => IsKnown
        ? $"준비할 오로성: {GoroseiEffects.Options.First(option => option.Mode == Mode).Name} · " +
            (Source switch
            {
                "CurrentSelectedIdentity" => "게임에서 확인한 선택",
                "UserPlan" => "직접 선택한 계획",
                "SavedUserPlan" => "저장해 둔 계획",
                "SyntheticIdentityNotNative" => "검수용 예시",
                _ => "선택한 계획"
            }) + " · 50라 이후를 위한 조합 목표이며, 현재 적용 중인 효과는 아닙니다."
        : "준비할 오로성을 아직 확인하지 못했습니다. 오로성 선택 메뉴에서 직접 고를 수 있습니다.";

    public static GoroseiPlanningSelection Resolve(GoroseiObservation current, long generation, long revision,
        GoroseiPlanningSelection? userSelection = null)
    {
        if (generation >= 0 && revision > 0 && current.IsCurrent &&
            current.MatchGeneration == generation && current.RecognitionRevision == revision &&
            current.Mode != GoroseiMode.None &&
            (current.Marker is null || current.Marker.Status == GoroseiMarkerStatus.SelectedIdentity))
            return new(current.Mode, current.IsSynthetic ? "SyntheticIdentityNotNative" : "CurrentSelectedIdentity");
        return userSelection is { IsKnown: true, Source: "UserPlan" or "SavedUserPlan" }
            ? userSelection : Unknown;
    }
}
