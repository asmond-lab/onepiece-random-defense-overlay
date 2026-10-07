namespace OrandOverlay;

/// <summary>Observation-only Stop transition. No window, timer, scanner, or persistence.</summary>
internal static class ScanStopTransition
{
    internal static CoachFrame Apply(CoachFrame observation, int automaticCount) =>
        observation with
        {
            IsCurrent = false, // Stop freshness is independent of automatic inventory count.
            RecognitionRevision = observation.RecognitionRevision + 1,
            NativeNavigation = NativeNavigationSnapshot.Unknown,
            CombatObservations = [],
            Gorosei = new(observation.MatchGeneration, observation.RecognitionRevision + 1,
                GoroseiMode.None, false) { Marker = GoroseiMarkerSnapshot.Unknown },
            Signals = CoachSignalAdapter.Read(null, observation.MatchGeneration,
                observation.RecognitionRevision + 1, false),
            GuideRuntime = BulletGuideRuntimeState.Unknown,
            HelperState = null,
            LoadedClearCount = null
        };
}
