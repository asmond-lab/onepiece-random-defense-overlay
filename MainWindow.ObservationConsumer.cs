namespace OrandOverlay;

public partial class MainWindow
{
    // Fixture calls the observation path, never the guarded runtime event body.
    internal void StopControlledObservation()
    {
        if (_runtimeEffects || !_controlledRecognitionAllowed || AutoScanCheck.IsChecked == true)
            throw new InvalidOperationException("Controlled Stop requires an unchecked non-runtime sandbox window.");
        _scanCancellation?.Cancel();
        ApplyScanStopObservation();
    }

    private void ApplyScanStopObservation()
    {
        Dispatcher.VerifyAccess();
        InvalidateDiagnosticInventoryObservation();
        _updateObservationPending = true;
        var stopped = ScanStopTransition.Apply(new CoachFrame
        {
            MatchGeneration = _adaptivePlanning.MatchGeneration,
            Revision = _coachRevision,
            Round = _lastRound,
            CompletedStoryStage = _mapSignals.CompletedStoryStageOrdinal,
            Inventory = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty,
            RecognitionRevision = _recognitionRevision,
            IsCurrent = _coachCurrent,
            NativeNavigation = _mapSignals.NativeNavigation,
            CombatObservations = _combatObservations,
            Gorosei = _goroseiObservation.Current
        }, _automatic.Count);
        _scanGeneration++; // fence any result issued before Stop
        _recognitionRevision = stopped.RecognitionRevision;
        _coachCurrent = stopped.IsCurrent;
        _automaticStale = true;
        _combatObservations = stopped.CombatObservations;
        _goroseiObservation.Accept(stopped.MatchGeneration, stopped.RecognitionRevision,
            stopped.Gorosei.Marker!, stopped.Gorosei.IsCurrent);
        _mapSignals = NavigationAfterObservationLoss(_mapSignals) with { RouteQuests = RouteQuestSnapshot.Unknown };
        _coachSignals = stopped.Signals;
        _guideRuntime = stopped.GuideRuntime;
        _helperState = stopped.HelperState;
        _loadedClearCount = stopped.LoadedClearCount;
        _latestRecognitionObservation = AdaptivePlanningRecognitionObservation.Empty;
        SetOverlayHandAvailability(false);
        RecognitionStatus.Text = "유닛 자동 확인을 껐어요. 마지막으로 읽은 유닛은 현재 보유 상태가 아닐 수 있어요.";
        RecognitionStatus.Foreground = RandyPickTheme.Warning;
        RefreshAll(_automatic.Count > 0
            ? "실시간 인식을 중지했습니다. 마지막 정상 패를 표시 중입니다."
            : "실시간 인식을 중지했습니다.");
    }

    // Pure extraction of the event consumers: no window or external effects required.
    internal static MapSignals NavigationAfterObservationLoss(MapSignals signals) =>
        signals with { NativeNavigation = NativeNavigationSnapshot.Unknown };

    internal static bool IsCurrentRecognition(int issuedScanGeneration, int currentScanGeneration,
        long issuedMatchGeneration, long currentMatchGeneration, bool sameRecognizer = true) =>
        issuedScanGeneration == currentScanGeneration && issuedMatchGeneration == currentMatchGeneration && sameRecognizer;

    internal static string ObservationNavigationHeader(string goalName,
        NativeNavigationSnapshot native, NavigationSessionState manual) =>
        goalName + " · " + NativeNavigationPresentation.Describe(native, manual.ConfirmedOptionId);

    // Guide planning consumes selected identity, not the separately gated current effect.
    internal static GoroseiPlanningSelection ResolveGuidePlanningSelection(GoroseiObservation current,
        long generation, long revision, GoroseiPlanningSelection? userSelection = null) =>
        GoroseiPlanningSelection.Resolve(current, generation, revision, userSelection);

    internal static GoroseiMode ResolveObservationGorosei(PlayMode mode,
        GoroseiMode savedSelection, GoroseiObservation current,
        GoroseiMode? explicitScenario = null, bool liveAutomatic = true) =>
        !liveAutomatic && mode != PlayMode.Guide && explicitScenario is { } scenario ? scenario :
        current.Marker is { Status: GoroseiMarkerStatus.SelectedIdentity } && current.EffectsActiveVerified
            ? current.EffectMode : GoroseiMode.None;
}
