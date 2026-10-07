namespace OrandOverlay;

/// <summary>Current accepted scan provenance, never settings or last-known evidence.</summary>
public sealed record GoroseiObservation(long MatchGeneration, long RecognitionRevision,
    GoroseiMode Mode, bool IsCurrent)
{
    public static GoroseiObservation Unknown { get; } = new(0, 0, GoroseiMode.None, false);
    public string Source => IsSynthetic ? "SyntheticFixtureNotNative" : "CurrentScan";
    [System.Text.Json.Serialization.JsonIgnore]
    internal OverlayExecutionContext.SyntheticGoroseiInput? SyntheticInput { get; init; }
    public int? EffectObservationRound { get; internal init; }
    public bool IsSynthetic => SyntheticInput is { } input && ReferenceEquals(input.Marker, Marker) && Mode == Marker.Mode;
    public string EffectEvidence => IsSynthetic ? "검수용 예시 · 실제 게임에서 확인한 효과가 아닙니다." : "실제 전투 효과는 아직 확인하지 못했습니다.";
    public GoroseiMarkerSnapshot? Marker { get; init; }
    public bool EffectsActiveVerified => IsCurrent && (IsSynthetic
        ? MatchGeneration > 0 && RecognitionRevision > 0 && EffectObservationRound >= 50 : (Marker?.EffectsActiveVerified ?? true));
    public GoroseiMode EffectMode => EffectsActiveVerified ? Mode : GoroseiMode.None;
}

public sealed class GoroseiObservationSession
{
    public GoroseiObservation Current { get; private set; } = GoroseiObservation.Unknown;
    public GoroseiMode LastKnown { get; private set; }
    internal void AcceptRecognition(long generation, long revision, RecognitionResult result, OverlayExecutionContext context)
    {
        if (generation != Current.MatchGeneration || revision <= Current.RecognitionRevision) return;
        var ready = result.State == RecognitionState.Ready && result.ShouldReplaceInventory;
        var marker = result.Diagnostics.GoroseiMarker;
        Accept(generation, revision, marker, ready);
        if (ready && result.SyntheticGorosei is { } input && input.Matches(context, marker))
            Current = Current with { SyntheticInput = input, EffectObservationRound = result.Diagnostics.MapState?.MaxRound };
    }
    public void Reset(long generation)
    {
        Current = new(generation, 0, GoroseiMode.None, false);
        LastKnown = GoroseiMode.None;
    }
    public void Accept(long generation, long revision, GoroseiMode mode, bool ready)
    {
        if (generation != Current.MatchGeneration || revision <= Current.RecognitionRevision) return;
        var current = ready && mode != GoroseiMode.None;
        Current = new(generation, revision, current ? mode : GoroseiMode.None, current);
        if (current) LastKnown = mode;
    }
    public void Accept(long generation, long revision, GoroseiMarkerSnapshot marker, bool ready)
    {
        if (generation != Current.MatchGeneration || revision <= Current.RecognitionRevision) return;
        var current = ready && marker.Status == GoroseiMarkerStatus.SelectedIdentity;
        Current = new(generation, revision, current ? marker.Mode : GoroseiMode.None, current)
            { Marker = ready ? marker : GoroseiMarkerSnapshot.Unknown };
        if (current) LastKnown = marker.Mode;
    }
}
