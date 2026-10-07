using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;

namespace OrandOverlay;

public partial class MainWindow
{
    private CoachJournal? _coachJournal;
    private long _coachRevision;
    private long _lastCoachSignalRevision = -1;
    private bool _coachCurrent;
    private ImmutableDictionary<string, long?> _coachSignals =
        CoachSignalAdapter.Read(null, 0, 0, false);
    private CoachFrame? _lastCoachFrame;
    private CoachDecision? _lastCoachDecision;
    private BeginnerGoalPolicy _beginnerGoals = null!;
    private BeginnerCoachSession _coachSession = null!;
    private IntermediateCraftCommitment _craftCommitment = null!;
    private bool _coachPaused;
    private ManualGoalPlan? _manualPlan;
    private BulletGuidePlan? _guidePlan;
    private QueenConversionInput _queenInput;
    private BulletGuideRuntimeState _guideRuntime = BulletGuideRuntimeState.Unknown;
    private HelperUnitState? _helperState;
    private ImmutableArray<CombatUnitState> _combatObservations = [];
    private int? _loadedClearCount;
    private FastUniqueState _guideFastUnique = FastUniqueState.Unknown;
    private readonly HashSet<string> _guideObservedLegendIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _guideObservedLegendCounts = new(StringComparer.OrdinalIgnoreCase);
    private NavigationAdvice? _automaticNavigation;
    private bool _manualDetails;
    private PlayMode CurrentPlayMode => BetaPlayModes.Resolve(PlayModes.Current(_settings));
    private bool UsesAutomaticGoals => CurrentPlayMode == PlayMode.Beginner;
    internal event Action<CoachDecision, CoachFrame>? CoachRendered;

    private void InitializeCoach()
    {
        _beginnerGoals = new BeginnerGoalPolicy(_catalog, _clearStats, _matchDifficulty);
        _coachSession = new BeginnerCoachSession(_catalog);
        _craftCommitment = new IntermediateCraftCommitment(_catalog);
        BetaPlayModes.Normalize(_settings);
        _adaptivePlanning.ClearManualNavigationOverride();
        PlayModeCombo.ItemsSource = PlayModes.Options;
        PlayModeCombo.SelectedItem = PlayModes.Options.Single(option => option.Mode == CurrentPlayMode);
        SecondaryGoalCombo.ItemsSource = GoalUnits();
        SecondaryGoalCombo.SelectedItem = GoalUnits().FirstOrDefault(unit => unit.Id == _settings.SecondaryGoalUnitId);
        ValidateSecondaryGoal();
        if (CurrentPlayMode == PlayMode.Manual) _adaptivePlanning.LatchManualGoalOverride();
        foreach (var view in new[] { MainCoachView, _overlay.BeginnerView })
        {
            view.ConfigurePlanCatalog(_catalog);
            view.AdvancedRequested += () =>
            {
                if (UsesAutomaticGoals)
                    SetPlayMode(CurrentPlayMode == PlayMode.Beginner ? PlayMode.Normal : PlayMode.Beginner);
                else
                {
                    _manualDetails = !_manualDetails;
                    MainCoachView.SetCompact(!_manualDetails);
                    _overlay.BeginnerView.SetCompact(!_manualDetails);
                }
            };
            view.QueenConditionRequested += (input, generation, revision) =>
            {
                if (_coachCurrent && !_coachPaused && CurrentPlayMode == PlayMode.Guide &&
                    _settings.GuideNumber == 1 && Enum.IsDefined(input) &&
                    _lastCoachFrame is { HasKnownDifficulty: true, Outcome: not ("clear" or "fail") } current &&
                    current.MatchGeneration == generation && current.Revision == revision &&
                    generation == _adaptivePlanning.MatchGeneration &&
                    current.Inventory.GetValueOrDefault("rawcode:HA0h") > 0)
                {
                    _queenInput = input; // Session-only user evidence, never a native mission flag.
                    RefreshAll();
                }
            };
            view.ConfirmationRequested += ConfirmCoachNavigation;
            view.GuideSelectionRequested += number =>
            {
                if (_settings.GuideNumber == number) return;
                _settings.GuideNumber = number;
                if (_persistSettings) _execution.SaveSettings(_settings);
                RefreshAll();
            };
            view.PauseRequested += () =>
            {
                _coachPaused = !_coachPaused;
                InvalidateDiagnosticInventoryObservation();
                if (UsesMap2320 && CurrentPlayMode == PlayMode.Normal) RenderDiagnosticInventoryReference();
                else RefreshAll();
            };
            view.ReviewRequested += OpenCoachReview;
        }
        InitializeNormalCandidateBrowser();
        ApplyCoachMode();
    }

    private void PlayMode_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updatingSelections || PlayModeCombo.SelectedItem is not PlayModeOption option) return;
        SetPlayMode(option.Mode);
    }

    private void SetPlayMode(PlayMode mode)
    {
        if (!BetaPlayModes.IsAvailable(mode))
        {
            PlayModeCombo.SelectedItem = PlayModes.Options.Single(option => option.Mode == CurrentPlayMode);
            return;
        }
        var previous = CurrentPlayMode;
        if (previous == mode) return;
        _manualDetails = false;
        if (previous == PlayMode.Manual)
            _settings.ManualGoalUnitId = SelectedGoal?.Id ?? _settings.GoalUnitId;
        if (PlayModes.AutomaticGoals(mode) && !PlayModes.AutomaticGoals(previous))
        {
            _beginnerGoals.Reset();
            _adaptivePlanning.ClearManualGoalOverride();
        }
        if (mode == PlayMode.Guide)
        {
            _adaptivePlanning.ClearManualGoalOverride();
            _pendingAdaptiveFingerprint = null;
            _routeQuestEvaluation = null;
        }
        _settings.Mode = mode;
        BetaPlayModes.Normalize(_settings);
        if (_adaptivePlanning.ManualLatches.NavigationOverride)
            _adaptivePlanning.ClearManualNavigationOverride();
        _updatingSelections = true;
        PlayModeCombo.SelectedItem = PlayModes.Options.Single(option => option.Mode == mode);
        AutoStartCheck.IsChecked = _settings.AutoStartGoal;
        AutoNavigationCheck.IsChecked = true;
        _updatingSelections = false;
        if (mode == PlayMode.Manual)
        {
            ApplyGoalAdvice(_catalog.Unit(_settings.ManualGoalUnitId ?? _settings.GoalUnitId), "");
            ValidateSecondaryGoal();
            _adaptivePlanning.LatchManualGoalOverride();
        }
        ApplyCoachMode();
        if (_persistSettings) _execution.SaveSettings(_settings);
        if (UsesMap2320 && mode == PlayMode.Normal) { RenderDiagnosticInventoryReference(); return; }
        RefreshAll();
    }

    private void ApplyCoachMode()
    {
        var mode = CurrentPlayMode;
        ExpertSettings.IsExpanded = mode == PlayMode.Manual;
        ExpertBoard.Visibility = ExpertSettlementButton.Visibility = ExpertReRecommendButton.Visibility =
            Visibility.Collapsed;
        MainCoachView.Visibility = mode == PlayMode.Normal ? Visibility.Collapsed : Visibility.Visible;
        NormalBrowserView.Visibility = mode == PlayMode.Normal ? Visibility.Visible : Visibility.Collapsed;
        UpdateMainModeTabs();
        ManualGoalsPanel.Visibility = mode == PlayMode.Manual ? Visibility.Visible : Visibility.Collapsed;
        MainCoachView.SetCompact(mode != PlayMode.Beginner);
        _overlay.BeginnerView.SetCompact(mode != PlayMode.Beginner);
        UpdateNavigationSelectionVisibility();
        BoardHeadingText.Text = PlayModes.Options.Single(option => option.Mode == mode).Name + " 모드";
        BoardSubheadingText.Text = "다음 행동을 따라가세요";
        DataVersionText.Visibility = Visibility.Collapsed;
        BeginnerModeHint.Text = mode switch
        {
            PlayMode.Beginner => "추천할 유닛과 항법을 고르고, 다음 행동과 이유를 차례로 알려드려요.",
            PlayMode.Normal => "인식한 유닛으로 만들 수 있는 조합 후보를 살펴보세요.",
            PlayMode.Manual => "목표 유닛을 두 개까지 고를 수 있어요. 필요한 재료와 항법을 함께 살펴봅니다.",
            _ => "공략 1 · 더글라스 불릿 · 솔로 악몽 기준"
        };
        if (UsesMap2320)
        {
            var recipeSourceVersion = _catalog.MapBundle?.Source.MapVersion ?? _catalog.OfflineBundle!.Source.MapVersion;
            DataVersionText.Visibility = Visibility.Visible;
            BeginnerModeHint.Text = mode == PlayMode.Normal
                ? $"인식한 유닛을 {recipeSourceVersion} 조합 자료와 비교해요. 실제 보유와 조합 가능 여부는 게임에서 확인해 주세요."
                : $"{recipeSourceVersion} 조합 자료를 참고해요. 현재 라운드와 게임 상태에 맞는 행동인지 게임에서 확인해 주세요.";
        }
    }

    private ImmutableArray<string> SelectedManualGoals() =>
        CurrentPlayMode != PlayMode.Manual || SelectedGoal is null ? [] :
        SecondaryGoalCombo.SelectedItem is UnitDefinition second && second.Id != SelectedGoal.Id
            ? [SelectedGoal.Id, second.Id] : [SelectedGoal.Id];

    private void SecondaryGoal_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updatingSelections) return;
        ValidateSecondaryGoal();
        if (_persistSettings) _execution.SaveSettings(_settings);
        RefreshAll();
    }

    private void ValidateSecondaryGoal()
    {
        var second = SecondaryGoalCombo.SelectedItem as UnitDefinition;
        if (second is not null && SelectedGoal is { } first)
        {
            try { ManualGoalPlan.Create(_catalog, [first.Id, second.Id], [], null); }
            catch (ArgumentException)
            {
                _updatingSelections = true;
                SecondaryGoalCombo.SelectedItem = null;
                _updatingSelections = false;
                second = null;
                FooterStatus.Text = "같은 유닛이나 서로 형태를 바꾸는 유닛은 두 목표로 지정할 수 없습니다.";
            }
        }
        _settings.SecondaryGoalUnitId = second?.Id;
    }

    private void ClearSecondaryGoal_OnClick(object sender, RoutedEventArgs e) =>
        SecondaryGoalCombo.SelectedItem = null;

    private void SelectBeginnerGoal(IReadOnlyList<InventoryEntry> inventory)
    {
        if (!UsesAutomaticGoals || !_coachCurrent || _coachPaused) return;
        var hasOwnedTop = inventory.Any(entry => entry.Count > 0 &&
            TopGradePolicy.IsTopGrade(_catalog.Unit(entry.UnitId).Tier));
        // Story/reward gates delay choosing a new build, not recognizing an existing one.
        if (!hasOwnedTop && (_mapSignals.CompletedStoryStageOrdinal < 9 && _lastRound < 20 ||
            _beginnerGoals.CommittedGoalId is null && _mapSignals.RewardWisps.GetValueOrDefault("e019") > 0)) return;
        var goal = _beginnerGoals.Select(inventory);
        if (goal is null || goal.Id == SelectedGoal?.Id) return;
        ApplyGoalAdvice(goal, "");
    }

    private void RenderBeginnerCoach(IReadOnlyList<Recommendation> recommendations,
        IReadOnlyList<AutoCombineStep> steps, IReadOnlyList<RareRerollAdvice> rerolls,
        IReadOnlyList<SpecialDismantleAdvice> dismantles, IReadOnlyList<GreenBloodAdvice> greenBlood,
        bool greenBloodAvailable, IReadOnlyList<EmergencySummonAdvice> wisps)
    {
        var automaticGoal = UsesAutomaticGoals && _beginnerGoals.CommittedGoalId is null;
        var goalIds = _manualPlan?.GoalIds ?? [];
        var goal = CurrentPlayMode == PlayMode.Guide
            ? _guidePlan is null ? null : _catalog.Unit(BulletGuidePolicy.GoalId) :
            _manualPlan is { } manual ? _catalog.Unit(manual.ActiveGoalId) : SelectedGoal;
        _automaticNavigation = UsesMap2320 ? null : _coachCurrent && _guidePlan is not null
            ? new NavigationAdvice(BulletGuidePolicy.NavigationId, "바운티헌터",
                "공략 1 계획 항법: 바운티헌터 고정 · 게임의 실제 선택은 별도 확인", [])
                { CanSelectNow = _lastRound is >= 21 and <= 23 }
            : _coachCurrent ? AutomaticNavigationAdvisor.Select(_catalog, CombinedInventory(),
            goalIds.Length > 0 ? goalIds.Select(_catalog.Unit).ToArray() : goal is null ? [] : [goal],
            _lastRound, Math.Max(1, _routeQuestEvaluation?.PlannedTopCount ?? goalIds.Length),
            _adaptivePlanningApplied?.InputFingerprint == _pendingAdaptiveFingerprint
                ? _adaptivePlanningApplied?.Navigation : null) : null;
        if (_automaticNavigation is { } automatic)
            SelectNavigation(NavigationProfiles.Find(automatic.OptionId));
        var frame = new CoachFrame
        {
            Mode = CurrentPlayMode, SelectedGoalIds = goalIds,
            GuideNumber = _settings.GuideNumber, GuidePlan = _guidePlan,
            GuideRuntime = _guideRuntime,
            HelperState = _helperState,
            CombatObservations = _combatObservations,
            Gorosei = _goroseiObservation.Current,
            UserGoroseiPlan = _userGoroseiPlan,
            RecognitionRevision = _recognitionRevision,
            LoadedClearCount = _loadedClearCount,
            ManualGoalSummary = _manualPlan is null ? "" : string.Join("\n", _manualPlan.Progress.Select((item, index) =>
                $"{index + 1}. {_catalog.Unit(item.GoalId).Name} · 유닛 재료 {item.Progress.CompletionRatio:P0}" +
                (item.Owned ? " · 보유/남겨두기" : ""))),
            NavigationConstraint = _manualPlan?.NavigationConflict == true
                ? "현재 확정 항법의 상위 수 제한과 선택 목표가 충돌합니다. 목표는 유지하고 다음 상위 제작은 보류합니다." : "",
            MatchGeneration = _adaptivePlanning.MatchGeneration, Revision = ++_coachRevision,
            Round = _lastRound, CompletedStoryStage = _mapSignals.CompletedStoryStageOrdinal,
            IsCurrent = _coachCurrent && !_automaticStale && !_automaticDisconnected,
            Paused = _coachPaused, Difficulty = _matchDifficulty, Outcome = _outcome.Outcome,
            GuideVisible = true,
            Inventory = CombinedInventory().ToImmutableDictionary(entry => entry.UnitId, entry => entry.Count),
            Signals = _coachSignals, GoalId = automaticGoal ? null : goal?.Id,
            NativeNavigation = _mapSignals.NativeNavigation,
            ConfirmedNavigation = EffectiveNavigation,
            SuggestedNavigation = _automaticNavigation?.OptionId,
            Story = _storySequence, RewardWisps = _mapSignals.RewardWisps,
            Recommendations = recommendations, CraftSteps = steps, Rerolls = rerolls,
            CommittedCraftUnitId = _craftCommitment.TargetUnitId,
            Dismantles = dismantles, GreenBlood = greenBlood, GreenBloodAvailable = greenBloodAvailable,
            Wisps = wisps
        };
        if (frame.Mode == PlayMode.Guide && frame.GuidePlan is not null)
            frame = frame with { ShipReservations = ShipReservationPolicy.Evaluate(frame, _catalog) };
        var decision = _coachSession.Update(frame);
        _craftCommitment.Record(frame, decision);
        BoardSubheadingText.Text = decision.Kind == CoachActionKind.Finished
            ? "이번 판을 돌아보세요" : "다음 행동을 따라가세요";
        var unit = decision.TargetUnitId is { } target ? _catalog.Unit(target) : null;
        MainCoachView.Render(decision, frame, unit);
        _overlay.RenderCoach(true, decision, frame, unit);
        RenderNormalCandidateBrowser(frame);
        RenderNavigationContext();
        _ = RecordCoachAsync(frame, decision);
        CoachRendered?.Invoke(decision, frame);
    }

    private void ConfirmCoachNavigation(string optionId)
    {
        if (UsesMap2320 || !_coachCurrent || _lastCoachDecision is not { RequiresUserConfirmation: true } decision ||
            !NavigationProfiles.Categories.SelectMany(category => NavigationProfiles.ForCategory(category.Id))
                .Any(option => option.Id == optionId)) return;
        if (decision.RequiresNavigationChoice ? _lastRound < 24 :
            decision.NavigationOptionId != optionId || _lastRound is < 21 or > 23) return;
        _navigationSession.Confirm(optionId);
        SelectNavigation(NavigationProfiles.Find(optionId));
        RefreshAll("게임 내 항법 선택을 사용자 확인으로 기록했습니다.");
    }

    private void OpenCoachReview()
    {
        string text;
        try
        {
            text = _coachJournal is not { HasLatest: true }
                ? "아직 저장된 이번 판 기록이 없어요. 게임을 진행한 뒤 다시 확인해 주세요."
                : CoachReview.Read(_coachJournal).Describe();
        }
        catch (IOException) { text = "이번 판 기록을 읽지 못했어요. 저장이 끝난 뒤 다시 열어 주세요."; }
        var window = new Window
        {
            Title = "이번 판 기록", Owner = this, Width = 640, Height = 620,
            Background = OverlayTheme.RowBrush, Foreground = OverlayTheme.WhiteBrush,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = text, TextWrapping = TextWrapping.Wrap,
                    Margin = OverlayTheme.CoachPanelPadding, FontSize = OverlayTheme.CoachBodyTypeSize
                }
            }
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(window, "coach-review-window");
        window.Show();
    }

    private void CaptureCoachObservation(RecognitionResult result)
    {
        CaptureGameplayTelemetry(result);
        _coachRevision++;
        _coachCurrent = !UsesMap2320 && result.State == RecognitionState.Ready;
        if (_coachCurrent)
        {
            _guideFastUnique = new BulletGuidePolicy(_catalog).ObserveFastUnique(_guideFastUnique, _lastRound,
                result.Entries.GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count)), _coachCurrent);
            var legends = result.Entries.Where(entry => entry.Count > 0 &&
                    _catalog.Unit(entry.UnitId).Id != "rawcode:S80h" &&
                    TopGradePolicy.BaseTier(_catalog.Unit(entry.UnitId).Tier) is "전설" or "히든" or "해적선")
                .GroupBy(entry => _catalog.Unit(entry.UnitId).Id, StringComparer.OrdinalIgnoreCase);
            foreach (var group in legends)
            {
                _guideObservedLegendIds.Add(group.Key);
                _guideObservedLegendCounts[group.Key] =
                    Math.Max(_guideObservedLegendCounts.GetValueOrDefault(group.Key), group.Sum(entry => entry.Count));
            }
        }
        _guideRuntime = _coachCurrent ? result.GuideRuntime : BulletGuideRuntimeState.Unknown;
        _helperState = _coachCurrent && !result.ConfirmsSessionBoundary ? result.HelperState : null;
        _combatObservations = AcceptCombatObservations(result);
        _loadedClearCount = _coachCurrent && !result.ConfirmsSessionBoundary ? result.LoadedClearCount : null;
        var snapshot = result.RecommendationInputs;
        var newSignals = snapshot is not null && snapshot.MatchGeneration == _adaptivePlanning.MatchGeneration &&
                         snapshot.RecognitionRevision > _lastCoachSignalRevision;
        _coachSignals = CoachSignalAdapter.Read(snapshot, _adaptivePlanning.MatchGeneration,
            snapshot?.RecognitionRevision ?? _coachRevision, _coachCurrent && newSignals);
        _coachSignals = CoachSignalAdapter.WithPlayerResources(_coachSignals, result.PlayerResources,
            _coachCurrent && !result.ConfirmsSessionBoundary);
        if (newSignals) _lastCoachSignalRevision = snapshot!.RecognitionRevision;
    }

    private async Task RecordCoachAsync(CoachFrame frame, CoachDecision decision)
    {
        _lastCoachFrame = frame;
        _lastCoachDecision = decision;
        RecordGameplayRecommendation(frame, decision);
        if (_coachJournal is null || !_liveSessionActive && frame.Outcome == "unknown") return;
        await _coachJournal.RecordAsync(frame, decision);
        if (_coachJournal.LastError is not null)
            FooterStatus.Text = "이번 판 기록을 저장하지 못했어요. 폴더 권한과 남은 저장 공간을 확인해 주세요.";
    }

    private void RecordCoachOutcome(string outcome)
    {
        if (_lastCoachFrame is not { } previous || _lastCoachDecision is not { } decision ||
            outcome is not ("clear" or "fail")) return;
        var frame = previous with
        {
            Revision = ++_coachRevision, Round = _lastRound,
            CompletedStoryStage = _mapSignals.CompletedStoryStageOrdinal, Outcome = outcome,
            IsCurrent = _coachCurrent,
            Inventory = CombinedInventory().ToImmutableDictionary(entry => entry.UnitId, entry => entry.Count),
            Signals = _coachSignals
        };
        _ = RecordCoachAsync(frame, decision with
        {
            Kind = CoachActionKind.Finished, Id = "outcome:" + outcome,
            Title = outcome == "clear" ? "클리어 확인" : "이번 판 종료 확인",
            Controls = "이번 판 기록에서 마지막 패와 추천을 확인하세요.",
            Confirmation = "게임이 끝난 것을 자동으로 확인했습니다.", TargetUnitId = null,
            NavigationOptionId = null, RequiresUserConfirmation = false,
            Reason = $"{_lastRound}라 · 스토리 {_mapSignals.CompletedStoryStageOrdinal}단계. 마지막 안내: {decision.Title}"
        });
    }
}
