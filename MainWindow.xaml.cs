using System.Globalization;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrandOverlay;

public partial class MainWindow : Window
{
    internal static readonly TimeSpan RecognitionInterval = TimeSpan.FromMilliseconds(250);
    internal AdaptivePlanningPerformanceSample? LastAdaptivePlanningPerformance =>
        _lastAdaptivePlanningPerformance;
    private readonly DataCatalog _catalog;
    private readonly OverlayExecutionContext _execution;
    private readonly AppSettings _settings;
    private readonly bool _persistSettings;
    // Startup suppression alone is not isolation: also gate event-driven external effects.
    private readonly bool _runtimeEffects;
    private readonly Dictionary<string, InventoryEntry> _automatic = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _growthUnitIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly LatestRefreshVersion _refreshVersion = new();
    private AdaptivePlanningCompositionRoot _adaptivePlanning = null!;
    private StoryProgressionProfile _storyProfile = null!;
    private readonly AdaptiveDecisionTraceBuffer _adaptiveDecisionTrace = new();
    private readonly LatestBackgroundWorkCoordinator _recommendationWork = new();
    private readonly DispatcherTimer _timer = new();
    // Same-channel signed Cloudflare manifest checks, every fifteen minutes after startup.
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    // 자동 업데이트 정책·중복 억제는 AppUpdateCoordinator가 소유한다.
    private AppUpdateCoordinator? _updateCoordinator;
    private RecommendationEngine _engine = null!;
    private InventoryStatsCalculator _statsCalculator = null!;
    private RareRerollAdvisor _rareRerollAdvisor = null!;
    private GreenBloodAdvisor _greenBloodAdvisor = null!;
    private GreenBloodAdvisor.UsageTracker _greenBloodUsage = null!;
    private SpecialDismantleAdvisor _specialAdvisor = null!;
    private AlchemyDismantleAdvisor _alchemyAdvisor = null!;
    private CombineHotkeyCatalog _combineHotkeys = null!;
    private AutoCombinePlanner _combinePlanner = null!;
    private ClearBuildStats _clearStats = ClearBuildStats.Empty;
    private LiveStats _liveStats = new();
    private CompletedTopUnitTracker _completedTopUnits = null!;
    private readonly FirstRareRecommendationGate _firstRareRecommendationGate = new();
    private readonly FirstRareTargetPolicy _firstRareTargetPolicy = new();
    private readonly TelemetryUploader _telemetry;
    private readonly MatchOutcomeDetector _outcome = new();
    // 텔레메트리의 coarse count·인식 상태·추천 범주는 전용 세션이 소유한다.
    private readonly MatchTelemetrySession _telemetrySession;
    private string _matchDifficulty = "unknown";
    private IInventoryRecognizer _recognizer = null!;
    private readonly bool _controlledRecognitionAllowed;
    private OverlayWindow _overlay = null!;
    private bool _initialized;
    internal bool StartupAborted { get; private set; }
    private bool _scanInProgress;
    private readonly DiagnosticScanContinuation _diagnosticScanContinuation = new();
    private bool _automaticStale;
    private bool _automaticDisconnected;
    private GoroseiMode _detectedGorosei = GoroseiMode.None;
    // Only an explicit selection during this UI session can supply offline scenario effects.
    // Persisted/auto-detected combo values are never current observation evidence.
    private GoroseiMode? _explicitGoroseiScenario;
    private GoroseiPlanningSelection _userGoroseiPlan = GoroseiPlanningSelection.Unknown;
    private string? EffectiveNavigation => _mapSignals.NativeNavigation.Resolve(_navigationSession.ConfirmedOptionId);
    private readonly GoroseiObservationSession _goroseiObservation = new();
    private long _recognitionRevision;
    private GoroseiMode CurrentGorosei => _goroseiObservation.Current.EffectMode;
    private bool _liveSessionActive;
    private bool _autoStartApplied;
    private MapSignals _mapSignals = MapSignals.Empty;
    private AdaptivePlanningApplied? _adaptivePlanningApplied;
    private StoryRewardSequenceDecision? _storySequence;
    private AdaptivePlanningRecognitionObservation _latestRecognitionObservation =
        AdaptivePlanningRecognitionObservation.Empty;
    private AdaptivePlanningPerformanceSample? _lastAdaptivePlanningPerformance;
    private readonly HashSet<string> _observedLegendIds =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _pendingAdaptiveFingerprint;
    private ImmutableArray<string> _pendingAdaptiveLegendIds = [];
    private bool _relockAfterMove;
    private bool _updatingSelections;
    private string? _selectedRouteId;
    private string? _clusterHeadRouteId;
    private IReadOnlyList<Recommendation> _boardRecs = [];
    private IReadOnlyList<AutoCombineStep> _boardPlan = [];
    private string? _boardBanner;
    private bool _boardShowsClusterChildren = true;
    private RecommendationSurface _recommendationSurface = RecommendationSurface.TopAndNavigation;
    private CancellationTokenSource? _scanCancellation;
    private int _scanGeneration;
    private string? _lastScanSignature;
    private int _lastRound;
    private int _confirmedWaitingScans;
    private bool _waitingBoundaryConsumed;
    // 현재 패 인식과 사용자 숨김 선택을 함께 보존하는 오버레이 표시 상태.
    private OverlayVisibilityState _overlayVisibility;
    // 연속 비표시 판정 횟수 — 히스테리시스 임계(OverlayVisibilityPolicy.HiddenStreakThreshold)와 비교.
    private int _overlayHiddenStreak;

    private sealed record RefreshComputation(
        RecommendationEngine Engine,
        IReadOnlyList<Recommendation> Recommendations,
        AdaptivePlanningEvaluation? AdaptivePlanning,
        StoryRewardSequenceDecision? StorySequence,
        RecommendationSurface Surface);

    public MainWindow() : this((Application.Current as App)?.Execution ?? OverlayExecutionContext.Production())
    {
    }

    internal MainWindow(AppSettings? settingsOverride, bool startRuntime,
        string? telemetryQueueDirectory = null)
        : this(startRuntime ? OverlayExecutionContext.Production()
            : OverlayExecutionContext.Fixture(settingsOverride ?? throw new ArgumentNullException(nameof(settingsOverride))),
            settingsOverride, telemetryQueueDirectory) { }

    internal MainWindow(OverlayExecutionContext execution, AppSettings? settingsOverride = null,
        string? telemetryQueueDirectory = null, string? fixtureMapVersion = null)
    {
        ArgumentNullException.ThrowIfNull(execution);
        execution.RequireConsent();
        if (fixtureMapVersion is not null && (execution.RuntimeEnabled || fixtureMapVersion is not ("2.314" or "2.320" or "2.321" or "2.322" or "2.323")))
            throw new ArgumentException("Map overrides are restricted to non-runtime fixtures.", nameof(fixtureMapVersion));
        _execution = execution;
        var startRuntime = execution.RuntimeEnabled;
        _runtimeEffects = execution.RuntimeEnabled;
        _controlledRecognitionAllowed = !execution.LiveMemoryEnabled;
        _catalog = execution.CreateCatalog();
        InitializeComponent();
        Loaded += (_, _) => { ApplyResolutionScale(); RestoreMainWindowGeometry(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(ApplyResolutionScale));
        Closing += (_, _) => SaveMainWindowGeometry();
        if (_runtimeEffects)
        {
            Loaded += (_, _) => ForegroundGameWatcher.Start();
            Closed += (_, _) => ForegroundGameWatcher.Stop();
        }
        _persistSettings = execution.RuntimeEnabled && settingsOverride is null;
        _settings = execution.RuntimeEnabled && settingsOverride is not null ? settingsOverride : execution.LoadSettings();
        BetaPlayModes.Normalize(_settings);
        _coachJournal = execution.CreateCoachJournal();
        _telemetry = execution.CreateTelemetry(_settings.TelemetryEnabled, telemetryQueueDirectory);
        _telemetrySession = new MatchTelemetrySession(_telemetry);
        try
        {
            _catalog.Load(mapVersion: fixtureMapVersion ?? (execution.LiveMemoryEnabled ? Map2323SourceContract.MapVersion : "2.314"));
            _adaptivePlanning = new AdaptivePlanningCompositionRoot(
                Path.Combine(AppContext.BaseDirectory, "Data"));
            _storyProfile = LoadApplicationStoryProfile(_catalog);
            _clearStats = ClearBuildStats.Load(ClearSamplePaths());
            _liveStats = LiveStats.Load(Path.Combine(AppContext.BaseDirectory, "Data", "orand-live-stats.json"));
            _combineHotkeys = CombineHotkeyCatalog.Load(
                Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
            _engine = new RecommendationEngine(_catalog, RankingClearStats(_catalog, _clearStats),
                _combineHotkeys);
            if (!UsesMap2320) _engine.SetLiveStats(_liveStats);
            _statsCalculator = new InventoryStatsCalculator(_catalog);
            _rareRerollAdvisor = new RareRerollAdvisor(_catalog);
            _greenBloodAdvisor = new GreenBloodAdvisor(_catalog);
            _greenBloodUsage = new GreenBloodAdvisor.UsageTracker(_catalog);
            _specialAdvisor = new SpecialDismantleAdvisor(_catalog);
            _alchemyAdvisor = new AlchemyDismantleAdvisor(_catalog);
            _combinePlanner = new AutoCombinePlanner(_catalog, _combineHotkeys);
            _completedTopUnits = new CompletedTopUnitTracker(_catalog);
            _recognizer = execution.CreateRecognizer(_catalog);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("{0}", exception);
            StartupAborted = true;
            MessageBox.Show("앱에 필요한 게임 정보를 불러오지 못했습니다. 앱 파일이 모두 있는지 확인한 뒤 다시 실행해 주세요.",
                "게임 정보 읽기 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        RepopulateGoalChoices(_settings.GoalUnitId);
        RepopulateNavigationChoices();
        RepopulateBuildVariants();
        GoroseiCombo.ItemsSource = GoroseiEffects.Options;
        _userGoroseiPlan = new(GoroseiEffects.Parse(_settings.BulletPlanningGoroseiMode), "SavedUserPlan");
        var selectedGorosei = GoroseiEffects.Parse(_settings.GoroseiMode);
        if (_userGoroseiPlan.IsKnown) selectedGorosei = _userGoroseiPlan.Mode;
        GoroseiCombo.SelectedItem = GoroseiEffects.Options.First(option => option.Mode == selectedGorosei);
        GoroseiSummaryText.Text = GoroseiEffects.Options.First(option => option.Mode == selectedGorosei).Summary;
        ClickThroughCheck.IsChecked = _settings.ClickThroughOverlay;
        OverlayModeCombo.SelectedIndex = _settings.OverlayDisplayMode switch
        {
            OverlayDisplayMode.StatsOnly => 1,
            OverlayDisplayMode.Hidden => 2,
            _ => 0
        };
        AutoScanCheck.IsChecked = _settings.AutoScanEnabled;
        ClearDataRefreshCheck.IsChecked = _settings.ClearDataAutoRefresh;
        ApplicationUpdateCheck.IsChecked = _settings.AutoUpdateEnabled;
        if (UsesMap2320) _telemetry.SetEnabled(false, deletePending: false);
        if (startRuntime && !UsesMap2320 && execution.HasCurrentConsent)
            _ = _telemetry.FlushPendingAsync();
        AutoStartCheck.IsChecked = _settings.AutoStartGoal;
        AutoNavigationCheck.IsChecked = _settings.AutoRecommendNavigation;
        ConfigureMap2320Navigation();
        UpdateNavigationSelectionVisibility();
        DataVersionText.Text = ApplicationDataVersionLabel();
        VersionText.Text = RandyPickBrand.BetaLabel;

        _overlay = new OverlayWindow();
        if (execution.ReplayDirectory is not null)
        {
            Title = "REPLAY VERIFICATION - " + Title;
            _overlay.Title = "REPLAY VERIFICATION - " + _overlay.Title;
            _overlay.Stats.Title = "REPLAY VERIFICATION - " + _overlay.Stats.Title;
        }
        InitializeCoach();
        if (UsesMap2320)
        {
            DataVersionText.Visibility = Visibility.Visible;
            RenderMap2320NavigationContext();
        }
        _overlay.RestorePosition(_settings.OverlayLeft, _settings.OverlayTop);
        _overlay.Stats.RestorePosition(_settings.StatsOverlayLeft, _settings.StatsOverlayTop);
        _overlay.PositionCommitted += Overlay_OnPositionCommitted;
        _overlay.Stats.PositionCommitted += StatsOverlay_OnPositionCommitted;
        _overlay.HiddenByUser += () =>
        {
            HideOverlayByUser();
        };
        _overlay.Stats.HiddenByUser += HideOverlayByUser;
        _overlay.ReRecommendRequested += ShowReRecommendMenu;
        // 인게임 패가 잡히기 전에는 오버레이를 띄우지 않는다.
        // 배율이 적용된 뒤라야 창 크기가 확정되므로 기본 배치는 로드 후에 잡는다.
        _overlay.Dispatcher.BeginInvoke(new Action(ApplyDefaultOverlayLayout),
            System.Windows.Threading.DispatcherPriority.Loaded);
        _overlay.SetClickThrough(_settings.ClickThroughOverlay);
        OverlayButton.Content = "패 인식 대기 중";
        _timer.Interval = RecognitionInterval;
        _timer.Tick += async (_, _) => await ScanAsync();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _updateLifecycleClosed = true;
            _updateTimer.Stop();
            _updateCoordinator?.Stop();
            CloseDiagnosticInventoryObservation();
            _scanCancellation?.Cancel();
            _scanCancellation?.Dispose();
            SendMatchTelemetry();
            SaveOverlayPosition();
            _overlay.Stats.CloseForApplication();
            _overlay.CloseForApplication();
            if (_persistSettings) _execution.SaveSettings(_settings);
        };
        _initialized = true;
        InitializeActivityRecording();
        InitializePendingUpdateNotices();
        InitializeApplicationUpdates(startRuntime);
        if (!execution.LiveMemoryEnabled) return;
        if (startRuntime) InitializeGameplayServices();
        RefreshAll("게임의 유닛을 읽을 준비를 하고 있어요.");
        if (_settings.AutoScanEnabled)
        {
            _timer.Start();
            _ = ScanAsync();
        }
        if (!startRuntime) return;
        _ = RefreshClearDataAsync();

    }

    private void LogUnknownRawcodes(RecognitionResult result) => _execution.LogUnknownRawcodes(result);

    private string[] ClearSamplePaths() => _execution.ClearSamplePaths();

    private string ClearStatsSummary() => _clearStats.HasData
        ? $" · 신+ 클리어 {_clearStats.TotalGodPlusSamples:#,0}판 학습" +
          $"({_clearStats.OldestSampleAt:MM.dd}~{_clearStats.NewestSampleAt:MM.dd})" +
          (_liveStats.TotalRecords > 0 ? $" · 실사용 {_liveStats.TotalRecords:#,0}판" : "")
        : " · 이전 클리어 기록이 없어 기본 추천 순서를 사용해요";

    /// <summary>
    /// 앱 시작 후 한 번, 저장소 스냅샷이 번들보다 새로우면 증분 수신한다.
    /// 티모지지 서버에는 접속하지 않는다. 실패해도 기존 번들+캐시로 계속 동작한다.
    /// </summary>
    private async Task RefreshClearDataAsync()
    {
        if (!_runtimeEffects) return;
        if (!_settings.ClearDataAutoRefresh) return;
        var newerThan = _clearStats.NewestSampleAt ?? DateTimeOffset.UtcNow.AddDays(-14);
        var service = _execution.CreateClearRefreshService();
        if (service is null) return;
        var fresh = await service.FetchNewSamplesAsync(newerThan);
        if (fresh.Count == 0) return;
        ClearSnapshotRefreshService.MergeIntoCache(_execution.ClearCacheFile!, fresh,
            DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        var reloaded = await Task.Run(() => ClearBuildStats.Load(ClearSamplePaths()));
        if (!reloaded.HasData) return;
        await Dispatcher.InvokeAsync(() =>
        {
            _clearStats = reloaded;
            _engine = new RecommendationEngine(_catalog, RankingClearStats(_catalog, _clearStats), _combineHotkeys);
            DataVersionText.Text = ApplicationDataVersionLabel();
            RepopulateGoalChoices(SelectedGoal?.Id ?? _settings.GoalUnitId);
            RepopulateNavigationChoices();
            RefreshAll($"신+ 클리어 데이터 {fresh.Count}판을 새로 반영했습니다.");
        });
    }

    private List<UnitDefinition> GoalUnits()
    {
        var tops = _catalog.AllUnits
            .Where(unit => IsTopUnitTier(unit.Tier) && unit.Recipe.Count > 0)
            .DistinctBy(x => x.Id)
            .ToList();
        return tops
            .OrderBy(unit => unit.Tier, StringComparer.CurrentCulture)
            .ThenBy(unit => unit.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private UnitDefinition? SelectedGoal => GoalCombo.SelectedItem as UnitDefinition;

    private sealed record GoalCategory(string Id, string Name, bool IsMagic, string BaseTier);

    // 유저가 부르는 순서(초월→불멸→영원→제한) 우선, 그 외 티어는 뒤에 이름순.
    private static readonly string[] GoalTierOrder = ["초월", "불멸", "영원", "제한됨", "신비함"];

    private static GoalCategory CategoryFor(UnitDefinition unit)
    {
        var isMagic = DamageTiers.IsMagic(unit.Tier);
        var baseTier = unit.Tier.Split('[', 2)[0].Trim();
        var kind = isMagic ? "마딜" : "물딜";
        return new GoalCategory($"{kind}:{baseTier}", $"{kind} - {baseTier}", isMagic, baseTier);
    }

    // 상위 선택을 "딜 유형 - 티어" 카테고리와 유닛의 2단 콤보로 나눈다(유저 요청).
    // 카테고리 안 유닛 순서는 기존과 같이 학습 표본 많은 순을 유지한다.
    private void RepopulateGoalChoices(string? preferredGoalId = null)
    {
        _updatingSelections = true;
        try
        {
            var goalUnits = GoalUnits();
            if (goalUnits.Count == 0) return;
            var currentId = preferredGoalId ?? SelectedGoal?.Id ?? _settings.GoalUnitId;
            var currentUnit = goalUnits.FirstOrDefault(x => x.Id == currentId) ?? goalUnits[0];
            var categories = goalUnits
                .Select(CategoryFor)
                .DistinctBy(category => category.Id)
                .OrderBy(category => category.IsMagic ? 1 : 0)
                .ThenBy(category => Array.IndexOf(GoalTierOrder, category.BaseTier) is >= 0 and var rank
                    ? rank
                    : int.MaxValue)
                .ThenBy(category => category.BaseTier, StringComparer.CurrentCulture)
                .ToList();
            GoalCategoryCombo.ItemsSource = categories;
            var category = categories.First(item => item.Id == CategoryFor(currentUnit).Id);
            GoalCategoryCombo.SelectedItem = category;
            var units = goalUnits.Where(unit => CategoryFor(unit).Id == category.Id).ToList();
            GoalCombo.ItemsSource = units;
            GoalCombo.SelectedItem = units.FirstOrDefault(x => x.Id == currentUnit.Id) ?? units[0];
        }
        finally
        {
            _updatingSelections = false;
        }
    }

    private void Settlement_OnClick(object sender, RoutedEventArgs e)
    {
        // 버튼은 레이아웃에 남기고 동작만 임시 비활성화한다(정산 판정 안정화 대기).
    }

    // 클리어 정산: 인식된 유닛(클리어 화면에선 필드 배치 유닛도 로컬 소유로 잡힘)을
    // 티어별로 세어 작은 창으로 보여준다.
    private void ShowSettlement()
    {
        var report = SettlementReport.Build(_catalog, CombinedInventory());
        var window = new Window
        {
            Title = "클리어 정산",
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = RandyPickTheme.Canvas,
            Topmost = true,
            MaxWidth = 560,
            ResizeMode = ResizeMode.NoResize,
            Content = new TextBlock
            {
                Text = report,
                Foreground = RandyPickTheme.Text,
                FontSize = 14,
                LineHeight = 24,
                Margin = new Thickness(20, 16, 20, 16),
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Malgun Gothic")
            }
        };
        window.Show();
    }

    private void AutoStart_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _updatingSelections) return;
        _settings.AutoStartGoal = UsesAutomaticGoals;
        RefreshAll();
    }

    private void AutoNavigation_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _updatingSelections) return;
        EnsureAutomaticNavigation();
        RefreshAll();
    }

    private string ApplyGoalAdvice(UnitDefinition goal, string prefix)
    {
        _autoStartApplied = true;
        _adaptivePlanning.NoteProgrammaticSelection();
        RepopulateGoalChoices(goal.Id);
        RepopulateNavigationChoices();
        RepopulateBuildVariants();
        return prefix;
    }

    // 다시 추천: 지금 패 기준으로 조합이 가장 가까운(완성률 순) 학습 상위 순위를
    // 목록으로 보여주고, 골라서 전환한다(유저 요청).
    private void ReRecommend_OnClick(object sender, RoutedEventArgs e) =>
        ShowReRecommendMenu(sender as FrameworkElement ?? this);

    private void ShowReRecommendMenu(FrameworkElement anchor)
    {
        if (!_initialized) return;
        var difficultyLabel = MatchOutcomeDetector.IsKnownDifficulty(_matchDifficulty)
            ? _matchDifficulty : "난이도를 아직 확인하지 못했어요";
        var counts = CombinedInventory()
            .Where(entry => entry.Count > 0)
            .GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        var calculator = new RecipeCompletionCalculator(_catalog.Unit);
        var ranked = GoalUnits()
            .Select(unit => (Unit: unit,
                Ratio: calculator.Calculate([unit.Id], counts).CompletionRatio,
                Samples: LearnedSelection.GoalSampleCount(_beginnerGoals.Statistics, unit)))
            .OrderByDescending(pair => pair.Ratio)
            .ThenByDescending(pair => pair.Samples)
            .Take(6)
            .ToList();
        if (ranked.Count == 0)
        {
            RefreshAll("조합 정보가 있는 상위 유닛이 없습니다.");
            return;
        }
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        var rank = 1;
        foreach (var entry in ranked)
        {
            var unit = entry.Unit;
            var percent = Math.Round(entry.Ratio * 100, MidpointRounding.AwayFromZero);
            var samples = entry.Samples;
            var isCurrent = unit.Id.Equals(SelectedGoal?.Id, StringComparison.OrdinalIgnoreCase);
            var item = new MenuItem
            {
                Header = $"{rank++}. {unit.Name} · 완성률 {percent:0}% · {difficultyLabel} {samples:#,0}판" +
                         (isCurrent ? " (현재 목표)" : ""),
                IsEnabled = !isCurrent
            };
            item.Click += (_, _) =>
            {
                _autoStartApplied = true;
                _adaptivePlanning.LatchManualGoalOverride();
                RefreshAll(ApplyGoalAdvice(unit,
                    $"다시 추천: {unit.Name} 선택 (완성률 {percent:0}퍼센트 · {difficultyLabel} {samples:#,0}판)."));
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private bool SelectNavigation(NavigationOption option)
    {
        if (UsesMap2320) option = ResolveApplicationNavigation(option.Id);
        var previousId = (NavigationCombo.SelectedItem as NavigationOption)?.Id ??
                         _settings.NavigationMode;
        _updatingSelections = true;
        try
        {
            var categories = NavigationProfiles.Categories
                .Where(category => VisibleNavigations(category.Id).Count > 0)
                .ToList();
            if (categories.Count == 0) categories = NavigationProfiles.Categories.ToList();
            if (categories.All(category => !category.Id.Equals(option.CategoryId,
                    StringComparison.OrdinalIgnoreCase)))
                categories.Add(NavigationProfiles.FindCategory(option.CategoryId));
            categories = NavigationProfiles.Categories
                .Where(category => categories.Any(item => item.Id.Equals(category.Id,
                    StringComparison.OrdinalIgnoreCase)))
                .ToList();
            NavigationCategoryCombo.ItemsSource = categories;
            NavigationCategoryCombo.SelectedItem = categories.FirstOrDefault(category =>
                category.Id.Equals(option.CategoryId, StringComparison.OrdinalIgnoreCase)) ?? categories[0];
            var options = VisibleNavigations(option.CategoryId);
            if (options.All(item => !item.Id.Equals(option.Id, StringComparison.OrdinalIgnoreCase)))
                options = ApplicationNavigationsForCategory(option.CategoryId).ToList();
            NavigationCombo.ItemsSource = options;
            NavigationCombo.SelectedItem = options.FirstOrDefault(item => item.Id == option.Id)
                                           ?? options[0];
            _settings.NavigationMode = option.Id;
            return !string.Equals(previousId, option.Id, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _updatingSelections = false;
        }
    }

    private void GoalCategoryCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelections) return;
        if (GoalCategoryCombo.SelectedItem is not GoalCategory category) return;
        var units = GoalUnits().Where(unit => CategoryFor(unit).Id == category.Id).ToList();
        if (units.Count == 0) return;
        var current = SelectedGoal;
        GoalCombo.ItemsSource = units;
        // 선택 변경이 GoalCombo_OnSelectionChanged를 태워 항법·빌드 방향 재구성과
        // RefreshAll까지 이어진다.
        GoalCombo.SelectedItem = current is not null && units.Any(x => x.Id == current.Id)
            ? units.First(x => x.Id == current.Id)
            : units[0];
    }

    private List<NavigationOption> VisibleNavigations(string categoryId) =>
        ApplicationNavigationsForCategory(categoryId).ToList();

    // 채우는 동안의 SelectionChanged 연쇄는 _updatingSelections로 차단한다.
    private void RepopulateNavigationChoices()
    {
        _updatingSelections = true;
        try
        {
            var currentOption = NavigationCombo.SelectedItem as NavigationOption ??
                                ResolveApplicationNavigation(_settings.NavigationMode);
            var categories = NavigationProfiles.Categories
                .Where(category => VisibleNavigations(category.Id).Count > 0)
                .ToList();
            if (categories.Count == 0) categories = NavigationProfiles.Categories.ToList();
            NavigationCategoryCombo.ItemsSource = categories;
            var category = categories.FirstOrDefault(item =>
                               item.Id.Equals(currentOption.CategoryId, StringComparison.OrdinalIgnoreCase))
                           ?? categories[0];
            NavigationCategoryCombo.SelectedItem = category;
            var options = VisibleNavigations(category.Id);
            if (options.Count == 0) options = ApplicationNavigationsForCategory(category.Id).ToList();
            NavigationCombo.ItemsSource = options;
            NavigationCombo.SelectedItem =
                options.FirstOrDefault(option => option.Id == currentOption.Id) ?? options[0];
        }
        finally
        {
            _updatingSelections = false;
        }
    }

    private static bool IsTopUnitTier(string tier)
    {
        var baseTier = tier.Split('[', 2)[0].Trim();
        return baseTier is "신비함" or "초월" or "불멸" or "영원" or "제한됨";
    }

    private IReadOnlyList<InventoryEntry> CombinedInventory(bool includeAutomatic = true)
    {
        var automatic = includeAutomatic ? _automatic.Values : Enumerable.Empty<InventoryEntry>();
        return automatic.Where(x => x.Count > 0)
            .OrderBy(x => _catalog.Unit(x.UnitId).Name)
            .ToList();
    }

    private IReadOnlyList<InventoryEntry> RecommendationInventory()
    {
        var current = _automaticDisconnected
            ? CombinedInventory(includeAutomatic: false)
            : CombinedInventory();
        return RecommendationInventoryPolicy.Build(
            current, !_automaticDisconnected, _completedTopUnits, _greenBloodUsage.UsedOnUnit);
    }

    private AdaptivePlanningRefreshInput BuildAdaptivePlanningInput(
        IReadOnlyList<InventoryEntry> inventory, UnitDefinition goal,
        NavigationOption navigation, GoroseiMode gorosei,
        IReadOnlyDictionary<string, UnitDefinition> allUnits)
    {
        var transient = _automaticStale || _automaticDisconnected || !_liveSessionActive;
        var latches = _adaptivePlanning.ManualLatches;
        var values = new List<PlanningValue>
        {
            PlanningValue.Known("recognition-stale", _automaticStale ? 1 : 0),
            PlanningValue.Known("recognition-disconnected", _automaticDisconnected ? 1 : 0),
            PlanningValue.Known("green-blood-used", _greenBloodUsage.Used ? 1 : 0)
        };
        if (_mapSignals.ActiveObjectiveRawcode is { } objective)
            values.Add(PlanningValue.Known($"story-objective:{objective}", 1));
        var activeStage = Math.Clamp(_mapSignals.ActiveObjectiveOrdinal ??
                                     _mapSignals.CompletedStoryStageOrdinal + 1, 0, 14);
        var milestones = Enumerable.Range(1, _mapSignals.CompletedStoryStageOrdinal)
            .Select(stage => $"stage-{stage}").ToImmutableArray();
        var source = new AdaptivePlanningInputSource
        {
            MatchGeneration = _adaptivePlanning.MatchGeneration,
            RecognitionRevision = _scanGeneration,
            Round = _lastRound,
            Phase = _adaptivePlanning.State.Phase,
            ActiveStoryStage = transient ? null : activeStage,
            CompletedStoryMilestones = milestones,
            Inventory = inventory,
            Units = allUnits,
            GoalUnitId = goal.Id,
            RouteGoalUnitIds = (UsesAutomaticGoals
                ? _beginnerGoals.RouteGoalIds
                : CurrentPlayMode == PlayMode.Manual ? new[] { goal.Id }
                : GoalUnits().Select(unit => unit.Id)).ToImmutableArray(),
            PlannedGoalUnitIds = _manualPlan?.GoalIds ?? [],
            NavigationOptionId = EffectiveNavigation ?? "Unselected",
            RouteQuests = _mapSignals.RouteQuests,
            PursueBothRouteQuests = BothRouteQuestsCheck.IsChecked == true,
            GoroseiMode = gorosei,
            CompletedTopUnitIds = _completedTopUnits.CompletedUnitIds.ToImmutableArray(),
            GrowthUnitIds = _growthUnitIds.Order(StringComparer.Ordinal).ToImmutableArray(),
            RewardWisps = _mapSignals.RewardWisps.ToImmutableDictionary(
                StringComparer.OrdinalIgnoreCase),
            PreviouslyObservedLegendIds = _observedLegendIds.Order(StringComparer.Ordinal)
                .ToImmutableArray(),
            AdditionalValues = values.ToImmutableArray(),
            ManualLatches = latches,
            IsTransient = transient,
            CurrentOverlayRecommendationId = _adaptivePlanningApplied?.Navigation.RecommendedOptionId,
            LockedOverlayRecommendationId = null
        };
        _routeQuestEvaluation = RouteQuestEvaluation.Evaluate(source);
        var input = _adaptivePlanning.CreateInput(source);
        _pendingAdaptiveFingerprint = input.Fingerprint;
        _pendingAdaptiveLegendIds = inventory.Where(entry => entry.Count > 0 &&
                allUnits.TryGetValue(entry.UnitId, out var unit) &&
                unit.Tier.Split('[', 2)[0].Trim() == "전설")
            .Select(entry => entry.UnitId).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal).ToImmutableArray();
        return input;
    }

    private StoryRewardSequenceInput BuildStoryRewardSequenceInput(
        IReadOnlyList<InventoryEntry> inventory,
        IReadOnlyDictionary<string, UnitDefinition> allUnits,
        AdaptiveBuildState state)
    {
        var transient = _automaticStale || _automaticDisconnected || !_liveSessionActive;
        var activeStage = Math.Clamp(_mapSignals.ActiveObjectiveOrdinal ??
                                     _mapSignals.CompletedStoryStageOrdinal + 1, 0, 14);
        return new StoryRewardSequenceInput
        {
            Phase = state.Phase,
            Round = _lastRound,
            ActiveStoryStage = transient ? null : activeStage,
            CompletedStoryStage = Math.Clamp(
                _mapSignals.CompletedStoryStageOrdinal, 0, 14),
            RewardWisps = _mapSignals.RewardWisps.ToImmutableDictionary(
                StringComparer.OrdinalIgnoreCase),
            Inventory = inventory,
            Units = allUnits,
            StoryStages = _storyProfile.Stages,
            PendingLegendId = state.PendingLegendId
        };
    }

    private void ApplyAdaptivePlanning(AdaptivePlanningApplied applied)
    {
        if (UsesMap2320) return;
        var previousPhase = _adaptivePlanningApplied?.State.Phase;
        var previousLegend = _adaptivePlanningApplied?.SuggestedLegendId;
        _adaptivePlanningApplied = applied;
        _overlay.RenderPlannerEvidence(_lastRound, applied,
            _automaticStale || _automaticDisconnected, null, _storySequence,
            _mapSignals.CompletedStoryStageOrdinal, _adaptivePlanning.ManualLatches);
        if (_pendingAdaptiveFingerprint == applied.InputFingerprint)
            _observedLegendIds.UnionWith(_pendingAdaptiveLegendIds);
        _adaptiveDecisionTrace.TryAppend(applied.Trace, applied.InputFingerprint, false);
        RenderNavigationContext();
        if (!NavigationAutomaticRecommendationPolicy.ShouldApply(
                true,
                _adaptivePlanning.ManualLatches,
                applied.State.Phase, applied.Navigation.State,
                applied.Navigation.RecommendedOptionId))
        {
            if (previousPhase != applied.State.Phase ||
                !string.Equals(previousLegend, applied.SuggestedLegendId,
                    StringComparison.OrdinalIgnoreCase))
                RefreshAll();
            return;
        }
        var option = ResolveApplicationNavigation(applied.Navigation.RecommendedOptionId!);
        _adaptivePlanning.NoteProgrammaticSelection();
        if (SelectNavigation(option))
        {
            RefreshAll(_lastRound <= 23
                ? $"항법 자동 추천: {option.Name}. 게임 안에서는 직접 선택하세요."
                : $"항법 자동 평가: {option.Name} · 선택 구간 종료, 참고용입니다.");
            return;
        }
        if (previousPhase != applied.State.Phase ||
            !string.Equals(previousLegend, applied.SuggestedLegendId,
                StringComparison.OrdinalIgnoreCase))
            RefreshAll();
    }

    internal static void DispatchPlannerEvidence(
        Action<int, AdaptivePlanningApplied?, bool, string?,
            StoryRewardSequenceDecision?> render,
        int round,
        AdaptivePlanningApplied applied,
        StoryRewardSequenceDecision? storySequence = null) =>
        render(round, applied, false, null, storySequence);

    private async void RefreshAll(string? message = null)
    {
        if (!_initialized) return;
        InvalidateNormalCandidateObservation(); // Synchronous fence before recommendation work can await.
        if (UsesMap2320)
        {
            ConfigureMap2320Navigation();
            DataVersionText.Visibility = Visibility.Visible;
            DataVersionText.Text = ApplicationDataVersionLabel();
        }
        var refreshVersion = _refreshVersion.Next();
        _beginnerGoals.UpdateContext(UsesMap2320 ? ClearBuildStats.Empty : _clearStats, _matchDifficulty);
        var difficultyStats = _beginnerGoals.Statistics;
        var recommendationDifficulty = _matchDifficulty;
        var playMode = CurrentPlayMode;
        var inventory = CombinedInventory();
        var recommendationInventory = RecommendationInventory();
        _guidePlan = playMode == PlayMode.Guide && GuideCatalog.Find(_settings.GuideNumber) is not null
            ? new BulletGuidePolicy(_catalog).Plan(_lastRound, _mapSignals.CompletedStoryStageOrdinal,
                inventory.ToDictionary(entry => entry.UnitId, entry => entry.Count), _matchDifficulty,
                EffectiveNavigation, ResolveGuidePlanningSelection(_goroseiObservation.Current,
                    _adaptivePlanning.MatchGeneration, _recognitionRevision, _userGoroseiPlan).Mode, _mapSignals.RouteQuests,
                _guideObservedLegendIds, _coachCurrent ? _mapSignals.DestructionKingAvailable : null,
                queenInput: _coachCurrent ? _queenInput : QueenConversionInput.Unknown,
                observedLegendCounts: _guideObservedLegendCounts, fastUnique: _guideFastUnique,
                selectionWisps: _coachCurrent ? _mapSignals.RewardWisps.GetValueOrDefault("e018") : 0,
                learning: CurrentBulletGuideLearning) : null;
        _guidePlan = PrepareFirstLegend(_guidePlan, inventory);
        var guidePlan = _guidePlan;
        SelectBeginnerGoal(inventory);
        var goal = GoalCombo.SelectedItem as UnitDefinition;
        if (goal is null) return;
        _settings.GoalUnitId = goal.Id;
        _manualPlan = CurrentPlayMode == PlayMode.Manual
            ? ManualGoalPlan.Create(_catalog, SelectedManualGoals(), recommendationInventory,
                EffectiveNavigation, _matchDifficulty)
            : null;
        var manualPlan = _manualPlan;
        if (manualPlan is not null) goal = _catalog.Unit(manualPlan.ActiveGoalId);
        if (guidePlan is not null) goal = _catalog.Unit(BulletGuidePolicy.GoalId);
        var committedCraftUnitId = _craftCommitment.TargetFor(goal.Id,
            _adaptivePlanning.MatchGeneration, playMode, recommendationInventory);
        var navigation = NavigationCombo.SelectedItem as NavigationOption ??
                         ResolveApplicationNavigation(_settings.NavigationMode);
        _settings.NavigationMode = navigation.Id;
        var gorosei = (GoroseiCombo.SelectedItem as GoroseiOption)?.Mode
                      ?? GoroseiEffects.Parse(_settings.GoroseiMode);
        _settings.GoroseiMode = gorosei.ToString();
        var liveGoroseiInput = AutoScanCheck.IsChecked == true || _liveSessionActive;
        gorosei = ResolveObservationGorosei(playMode, gorosei, _goroseiObservation.Current,
            _explicitGoroseiScenario, liveGoroseiInput);
        GoroseiSummaryText.Text = !liveGoroseiInput && playMode != PlayMode.Guide && _explicitGoroseiScenario is not null
            ? "직접 고른 계획 · 실제 효과 확인 아님 · " + GoroseiEffects.Options.First(x => x.Mode == gorosei).Summary
            : "게임에서 확인한 현재 효과 · " + GoroseiEffects.Options.First(x => x.Mode == gorosei).Summary +
              " · 저장한 설정이나 직접 고른 계획만으로는 실제 효과를 확인할 수 없어요.";
        if (playMode == PlayMode.Guide)
            GoroseiSummaryText.Text = ResolveGuidePlanningSelection(_goroseiObservation.Current,
                _adaptivePlanning.MatchGeneration, _recognitionRevision, _userGoroseiPlan).Describe() +
                " · 게임에서 확인한 효과: " + (CurrentGorosei == GoroseiMode.None ? "아직 확인하지 못했습니다" :
                    GoroseiEffects.Options.First(option => option.Mode == CurrentGorosei).Name) +
                " · " + _goroseiObservation.Current.EffectEvidence;
        // Recommendation engine current-combat input remains fail-closed; GuidePlan uses selection separately.
        if (playMode == PlayMode.Guide) gorosei = CurrentGorosei;
        // 니카 이감/노이감은 별도 토글 없이 현재 패의 스턴을 기준으로 자동 판정한다.
        // 지옥 이하는 그린블러드가 제공되지 않으므로 세라핌 조합 후보도 함께 뺀다.
        var firstRareQuestWindow = FirstRareRecommendationGate.IsQuestWindow(
            recommendationInventory, _lastRound);
        var prioritizeTargetRare = _firstRareRecommendationGate.ShouldPrioritize(
            goal.Id, recommendationInventory, _engine.RecipeRareUnitIds(goal.Id), _lastRound,
            _liveSessionActive);
        var suppressSeraphim = _greenBloodUsage.Used ||
                               recommendationDifficulty is not ("신" or "악몽");
        var nextEngine = new RecommendationEngine(
            _catalog, RankingClearStats(_catalog, difficultyStats), _combineHotkeys);
        if (!UsesMap2320) ConfigureGameplayEngine(nextEngine, recommendationDifficulty);
        var planningState = _adaptivePlanning.State;
        var latches = _adaptivePlanning.ManualLatches;
        var allUnits = _catalog.AllUnits.GroupBy(unit => unit.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var surface = RecommendationSequencePolicy.Surface(
            _settings.AutoStartGoal, planningState.Phase, latches);
        var storyInput = (_settings.AutoStartGoal && !latches.GoalOverride) || guidePlan is not null
            ? BuildStoryRewardSequenceInput(
                recommendationInventory, allUnits, planningState)
            : null;
        var adaptiveWork = UsesMap2320 || playMode == PlayMode.Guide ? null :
            _adaptivePlanning.TryBegin(BuildAdaptivePlanningInput(
                recommendationInventory, goal, navigation, gorosei, allUnits));
        var recognitionObservation = _latestRecognitionObservation;
        var recommendationRound = _lastRound;
        var recommendationStoryStage = _mapSignals.CompletedStoryStageOrdinal;
        var nativeNavigation = _mapSignals.NativeNavigation;
        var effectiveNavigation = EffectiveNavigation;
        var computation = await _recommendationWork.RunAsync(() =>
        {
            var storySequence = storyInput is null
                ? null
                : StoryRewardSequencePlanner.Evaluate(storyInput);
            var pipeline = RecommendationPipeline.ComputeCandidates(
                new RecommendationPipelineRequest
                {
                    Engine = nextEngine,
                    Mode = playMode,
                    GuidePlan = guidePlan,
                    Goal = goal,
                    ManualPlan = manualPlan,
                    CommittedCraftUnitId = committedCraftUnitId,
                    Inventory = recommendationInventory,
                    InitialSurface = surface,
                    StorySequence = storySequence,
                    NativeNavigation = nativeNavigation,
                    NavigationMode = effectiveNavigation ?? "Unselected",
                    Gorosei = gorosei,
                    BuildVariant = BuildVariants.AutoId,
                    Difficulty = recommendationDifficulty,
                    Round = recommendationRound,
                    CompletedStoryStage = recommendationStoryStage,
                    SuppressSeraphim = suppressSeraphim,
                    PrioritizeTargetRare = prioritizeTargetRare,
                    SuppressFirstRareShip = !firstRareQuestWindow
                });
            return new RefreshComputation(nextEngine, pipeline.Recommendations,
                adaptiveWork is null
                    ? null
                    : AdaptivePlanningCompositionRoot.EvaluateObserved(
                        adaptiveWork, recognitionObservation),
                pipeline.StorySequence, pipeline.Surface);
        });
        if (computation is null) return;
        if (!_refreshVersion.IsCurrent(refreshVersion) || Dispatcher.HasShutdownStarted) return;
        _engine = computation.Engine;
        var recommendations = computation.Recommendations;
        _storySequence = computation.StorySequence;
        surface = computation.Surface;
        var pipelineResult = RecommendationPipeline.Finalize(
            new RecommendationPipelineCandidates(surface, recommendations, _storySequence),
            _catalog,
            goal,
            recommendationInventory,
            _firstRareTargetPolicy,
            _lastRound,
            _mapSignals.CompletedStoryStageOrdinal,
            _matchDifficulty);
        recommendations = pipelineResult.Recommendations;
        var recommendationUrgency = pipelineResult.Urgency;
        _recommendationSurface = surface;
        if (computation.AdaptivePlanning is { } adaptive)
        {
            _lastAdaptivePlanningPerformance = adaptive.Performance;
            _adaptivePlanning.ScheduleApply(adaptive.Computed,
                action => Dispatcher.BeginInvoke(new Action(action)),
                ApplyAdaptivePlanning);
        }
        GoalSelectLabel.Text = "목표 1";
        var displayGoal = _storySequence?.RecommendedLegendId is { } storyLegendId &&
                          allUnits.TryGetValue(storyLegendId, out var storyLegend)
            ? storyLegend
            : goal;
        _telemetrySession.ObserveRecommendation(
            playMode == PlayMode.Guide ? DamageLane.Unknown : GoalStrategyCalculator.IsMagicDamageTier(displayGoal.Tier)
                ? DamageLane.Magic
                : DamageLane.Physical,
            playMode == PlayMode.Guide ? "unknown" : displayGoal.Tier,
            surface,
            recommendationUrgency.Urgency);
        CaptureMatchTelemetry();
        var inventoryStats = _statsCalculator.Calculate(recommendationInventory);
        var rareRerolls = playMode == PlayMode.Guide ? [] : _rareRerollAdvisor.Evaluate(recommendationInventory, recommendations,
            displayGoal, difficultyStats.HasData ? difficultyStats : null, _lastRound);
        IReadOnlyList<GreenBloodAdvice> greenBloodAdvice = _greenBloodUsage.Used ||
            recommendationDifficulty is not ("신" or "악몽")
            ? Array.Empty<GreenBloodAdvice>()
            : playMode == PlayMode.Guide
                ? guidePlan is null ? [] : _greenBloodAdvisor.EvaluateBulletGuide(
                    recommendationInventory, _mapSignals.CompletedStoryStageOrdinal,
                    CurrentGorosei, guidePlan.Support?.StunPairReady == true, recommendationDifficulty)
            : _greenBloodAdvisor.Evaluate(displayGoal, recommendationInventory,
                recommendations, difficultyStats.HasData ? difficultyStats : null, recommendationDifficulty);

        if (!UsesMap2320)
        {
            InventoryList.Items.Clear();
            foreach (var item in inventory)
                InventoryList.Items.Add($"{_catalog.Unit(item.UnitId).Name}  ×{item.Count}" +
                                        (_automaticStale ? "   마지막으로 확인한 패" : ""));
            if (inventory.Count == 0) InventoryList.Items.Add("아직 인식한 유닛이 없어요");
        }

        GoalSelectLabel.Visibility = GoalSelectRow.Visibility =
            surface == RecommendationSurface.TopAndNavigation
                ? Visibility.Visible
                : Visibility.Collapsed;
        IReadOnlyList<Recommendation> visibleRecommendations =
            surface == RecommendationSurface.TopAndNavigation
                ? RecommendationResultPolicy.ForEmptyInventory(
                recommendations, recommendationInventory.Count,
                recommendation => _catalog.Unit(recommendation.Route.GoalUnitId)
                    .Tier.Split('[', 2)[0].Trim() == "희귀함")
                : recommendations;
        var phaseHint = recommendationUrgency.Reason ?? surface switch
        {
            RecommendationSurface.FastRare =>
                "7라운드까지 희귀함이 안 나오면 선택 위습 1~2개 사용 권장",
            RecommendationSurface.StoryLegend => _storySequence?.ActionSummary,
            _ => null
        };

        var headId = BoardSelection.ClusterHeadId(
            visibleRecommendations, [], _selectedRouteId, _clusterHeadRouteId);
        var head = BoardSelection.Find(visibleRecommendations, headId)
                   ?? visibleRecommendations.FirstOrDefault();
        var showClusterChildren = surface != RecommendationSurface.StoryLegend;
        var previewChildren = !showClusterChildren || head is null
            ? []
            : _engine.StoryClusterChildren(head.Route.GoalUnitId, recommendationInventory);
        if (_selectedRouteId is null ||
            !BoardSelection.IsKnown(visibleRecommendations, previewChildren, _selectedRouteId))
        {
            _selectedRouteId = head?.Route.Id;
            _clusterHeadRouteId = head?.Route.Id;
        }
        var combinePlan = _combinePlanner.Plan(visibleRecommendations.Take(1).ToList(),
            recommendationInventory, _completedTopUnits.CompletedUnitIds,
            GrowthMaterialInventory.ProtectForCraft(
                guidePlan is not null ? guidePlan.ProtectedUnitIds : manualPlan?.ProtectedGoalIds, _growthUnitIds),
            EffectiveNavigation is { } confirmed
                ? ResolveApplicationNavigation(confirmed).TopUnitLimit : null);
        _boardRecs = visibleRecommendations;
        _boardPlan = combinePlan;
        _boardShowsClusterChildren = showClusterChildren;
        _boardBanner = surface switch
        {
            RecommendationSurface.FastRare =>
                $"첫 희귀함 찾기 · 빠른 완성 순 — {phaseHint}",
            RecommendationSurface.StoryLegend =>
                $"{_storySequence?.CurrentStoryLabel} · {_storySequence?.ActionSummary}",
            _ => null
        };
        FillMainBoard();
        var emergencySummons = surface == RecommendationSurface.TopAndNavigation &&
                               EffectiveNavigation == "AlliedForces.EmergencyCall" &&
                               navigation.Id.Equals("AlliedForces.EmergencyCall",
            StringComparison.OrdinalIgnoreCase)
            ? _engine.RecommendEmergencySummons(recommendations, recommendationInventory)
            : Array.Empty<EmergencySummonAdvice>();
        var header = surface switch
        {
            RecommendationSurface.FastRare => "첫 희귀함 찾기 · 빠른 완성 순",
            RecommendationSurface.StoryLegend =>
                $"{_storySequence?.CurrentStoryLabel} · " +
                $"{_storySequence?.RecommendedLegendName ?? "첫 전설 계산"}",
            _ => ObservationNavigationHeader(goal.Name, _mapSignals.NativeNavigation, _navigationSession)
        };
        Func<Recommendation, IReadOnlyList<Recommendation>>? storyChildren =
            showClusterChildren
                ? rec => _engine.StoryClusterChildren(
                    rec.Route.GoalUnitId, recommendationInventory)
                : null;
        var specialAdvice = playMode == PlayMode.Guide ? [] : _specialAdvisor.Evaluate(recommendationInventory, recommendations, displayGoal,
                difficultyStats.HasData ? difficultyStats : null)
            .Concat(_alchemyAdvisor.Evaluate(recommendationInventory, recommendations, displayGoal,
                EffectiveNavigation ?? "Unselected", _growthUnitIds)).ToList();
        var greenBloodAvailable = recommendationDifficulty is "신" or "악몽" && !_greenBloodUsage.Used &&
            GreenBloodAdvisor.HasUnusedGreenBlood(_catalog, recommendationInventory);
        _overlay.Render(
            header,
            visibleRecommendations, inventoryStats, rareRerolls,
            greenBloodAdvice,
            greenBloodAvailable,
            combinePlan, RecognitionStatus.Text, DamageTiers.IsMagic(displayGoal.Tier),
            emergencySummons,
            gorosei, _greenBloodUsage.Used,
            specialAdvice,
            _engine.ActiveStunTarget, _engine.ActiveStunCap,
            phaseHint, storyChildren,
            (recs, selectedId) => _engine.Recascade(
                recs, RecommendationInventory(), selectedId),
            inventory: recommendationInventory,
            showRouteRootAsCurrentCraft: surface == RecommendationSurface.FastRare,
            onRouteSelected: SelectOverlayRoute);
        var evidenceUnknown = _automaticStale || _automaticDisconnected;
        _overlay.RenderPlannerEvidence(_lastRound, _adaptivePlanningApplied,
            evidenceUnknown,
            evidenceUnknown ? "현재 게임 정보를 다시 확인하는 중입니다." : null,
            _storySequence, _mapSignals.CompletedStoryStageOrdinal, _adaptivePlanning.ManualLatches);
        RenderNavigationContext();
        if (message is not null) FooterStatus.Text = message;
        RenderBeginnerCoach(visibleRecommendations, combinePlan, rareRerolls,
            specialAdvice, greenBloodAdvice, greenBloodAvailable, emergencySummons);
        if (UsesMap2320) RenderMap2320NavigationContext();
    }

    // 자동 스캔 틱에서만 쓰는 얕은 갱신: 패·상태가 직전 틱과 같으면 추천 재계산과
    // 전체 UI 재구성을 건너뛰어 게임과의 CPU 경쟁(끊김)을 줄인다. 상태줄만 갱신한다.
    // The recognizer's Status and diagnostics remain untouched for logs and compatibility reports.
    private static string RecognitionDisplayStatus(RecognitionResult result) => result.State switch
    {
        RecognitionState.Ready => $"유닛 {result.Entries.Sum(entry => entry.Count)}개 인식" +
            (result.Diagnostics.UnknownObjects > 0 ? $" · 종류를 확인하지 못한 유닛 {result.Diagnostics.UnknownObjects}개 제외" : "") +
            (result.Status.Contains("시험 중인 인식 기능", StringComparison.Ordinal) ? " · 인식 기능 시험 중, 게임에서 확인해 주세요" : ""),
        RecognitionState.Waiting => "게임에서 유닛을 확인할 때까지 기다리고 있어요.",
        RecognitionState.TransientReadError => "유닛을 읽지 못했어요. 다시 확인하고 있어요.",
        RecognitionState.Unsupported => "지원하지 않는 워크래프트 버전이에요. 업데이트를 확인해 주세요.",
        RecognitionState.UnverifiedProfile => "이 워크래프트 버전의 인식 기능은 아직 준비 중이에요.",
        RecognitionState.ConfigurationError => "게임 정보를 불러오지 못했어요. 앱을 다시 실행해 주세요.",
        _ => "게임 정보를 확인하고 있어요."
    };

    private static string RecognitionDisplayDetail(RecognitionResult result) =>
        $"유닛 {result.Entries.Sum(entry => entry.Count)}개를 인식했어요." +
        (result.Diagnostics.UnknownObjects > 0
            ? $" 종류를 확인하지 못한 유닛 {result.Diagnostics.UnknownObjects}개는 제외했어요." : "") +
        (result.Diagnostics.Gorosei != GoroseiMode.None
            ? " 오로성 " + GoroseiEffects.Options.First(option => option.Mode == result.Diagnostics.Gorosei).Name + " 효과를 확인했어요." : "") +
        " 실제 보유와 조합 가능 여부는 게임에서 확인해 주세요.";

    private void RefreshIfScanStateChanged(string? message)
    {
        var signature = BuildScanSignature();
        if (signature == _lastScanSignature)
        {
            if (!string.IsNullOrWhiteSpace(message)) FooterStatus.Text = message;
            _overlay.UpdateStatus(RecognitionStatus.Text);
            return;
        }
        _lastScanSignature = signature;
        RefreshAll(message);
    }

    private string BuildScanSignature()
    {
        var builder = new StringBuilder();
        foreach (var entry in CombinedInventory().OrderBy(x => x.UnitId, StringComparer.Ordinal))
            builder.Append(entry.UnitId).Append(':').Append(entry.Count).Append('|');
        foreach (var growthId in _growthUnitIds.OrderBy(id => id, StringComparer.Ordinal))
            builder.Append("growth:").Append(growthId).Append('|');
        builder.Append(_automaticStale).Append('|').Append(_automaticDisconnected).Append('|')
            .Append(_liveSessionActive).Append('|').Append(_autoStartApplied).Append('|')
            .Append(_greenBloodUsage.Used).Append('|').Append(_greenBloodUsage.UsedOnUnit)
            .Append('|').Append(_lastRound).Append('|').Append(_goroseiObservation.Current)
            .Append('|').Append(_matchDifficulty)
            .Append('|').Append(_loadedClearCount)
            .Append('|').Append(_mapSignals.DestructionKingAvailable)
            .Append('|').Append(_mapSignals.ActiveObjectiveOrdinal)
            .Append('|').Append(_mapSignals.CompletedStoryStageOrdinal)
            .Append('|').Append(_adaptivePlanning.State.Phase)
            .Append('|').Append(_adaptivePlanning.ManualLatches.GoalOverride)
            .Append('|').Append(_adaptivePlanning.ManualLatches.NavigationOverride);
        foreach (var reward in _mapSignals.RewardWisps.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            builder.Append('|').Append(reward.Key).Append(':').Append(reward.Value);
        builder.Append('|').Append(_mapSignals.RouteQuests.Describe());
        builder.Append('|').Append(_mapSignals.NativeNavigation).Append('|').Append(_mapSignals.RouteQuests.HighGamble);
        if (_goroseiObservation.Current.Marker is { } marker)
            foreach (var candidate in marker.Candidates) builder.Append('|').Append(candidate);
        builder.Append('|').Append(_outcome.Outcome).Append('|').Append(_coachCurrent);
        if (CurrentPlayMode == PlayMode.Guide)
        {
            builder.Append('|').Append(_guideRuntime);
            builder.Append('|').Append(_helperState?.Mana).Append(':').Append(_helperState?.MaximumMana);
            if (_helperState is { } helper)
                foreach (var ability in helper.Abilities)
                    builder.Append('|').Append(ability);
            AppendCombatObservationFingerprint(builder, _combatObservations);
        }
        foreach (var signal in _coachSignals.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            builder.Append('|').Append(signal.Key).Append(':').Append(signal.Value?.ToString() ?? "unknown");
        return builder.ToString();
    }

    private void ApplyDetectedGorosei(GoroseiMode detected)
    {
        if (detected == GoroseiMode.None) return;
        var current = GoroseiEffects.Parse(_settings.GoroseiMode);
        var resolved = GoroseiMemoryDetector.Resolve(current, detected);
        var firstDetection = _detectedGorosei != resolved;
        _detectedGorosei = resolved;
        if (current != resolved)
        {
            _settings.GoroseiMode = resolved.ToString();
            if (_persistSettings) _execution.SaveSettings(_settings);
        }

        var option = GoroseiEffects.Options.First(item => item.Mode == resolved);
        if ((GoroseiCombo.SelectedItem as GoroseiOption)?.Mode != resolved)
        {
            var wasUpdating = _updatingSelections;
            _updatingSelections = true;
            try { GoroseiCombo.SelectedItem = option; }
            finally { _updatingSelections = wasUpdating; }
        }
        if (firstDetection)
            GoroseiSummaryText.Text = "자동 감지 · " + option.Summary;
    }

    private void FillMainBoard()
    {
        var headId = BoardSelection.ClusterHeadId(
            _boardRecs, [], _selectedRouteId, _clusterHeadRouteId);
        if (_selectedRouteId is not null && _boardRecs.Count > 0)
            _boardRecs = _engine.Recascade(_boardRecs, RecommendationInventory(), headId);
        var head = BoardSelection.Find(_boardRecs, headId) ?? _boardRecs.FirstOrDefault();
        _clusterHeadRouteId = head?.Route.Id;
        var children = !_boardShowsClusterChildren || head is null
            ? []
            : _engine.StoryClusterChildren(head.Route.GoalUnitId, RecommendationInventory());
        if (!BoardSelection.IsKnown(_boardRecs, children, _selectedRouteId))
            _selectedRouteId = head?.Route.Id;
        var evidenceUnknown = _automaticStale || _automaticDisconnected;
        var plannerEvidence = RecommendationPresentation.PlannerEvidence(
            _lastRound, _adaptivePlanningApplied, evidenceUnknown,
            evidenceUnknown ? "현재 게임 정보를 다시 확인하는 중입니다." : null,
            _storySequence, _adaptivePlanning.ManualLatches);
        RecommendationBoard.Fill(NowPanel, FlowPanel, BoardPanel, _boardRecs, _boardPlan,
            _selectedRouteId, SelectMainRoute, _boardBanner, children, head?.Route.Id,
            plannerEvidence,
            showRouteRootAsCurrentCraft:
                _recommendationSurface == RecommendationSurface.FastRare);
    }

    private void SelectMainRoute(string routeId)
    {
        _selectedRouteId = routeId;
        if (_recommendationSurface == RecommendationSurface.FastRare)
        {
            _firstRareTargetPolicy.Select(routeId, _boardRecs);
            RefreshAll();
            return;
        }
        var headId = BoardSelection.ClusterHeadId(
            _boardRecs, [], _selectedRouteId, _clusterHeadRouteId);
        var head = BoardSelection.Find(_boardRecs, headId);
        var currentChildren = !_boardShowsClusterChildren || head is null
            ? []
            : _engine.StoryClusterChildren(head.Route.GoalUnitId, RecommendationInventory());
        if (BoardSelection.Contains(_boardRecs, routeId) &&
            !BoardSelection.Contains(currentChildren, routeId))
            _clusterHeadRouteId = BoardSelection.Find(_boardRecs, routeId)!.Route.Id;
        FillMainBoard();
    }

    private void SelectOverlayRoute(string routeId)
    {
        if (_recommendationSurface != RecommendationSurface.FastRare) return;
        _firstRareTargetPolicy.Select(routeId, _boardRecs);
        _selectedRouteId = routeId;
        _clusterHeadRouteId = routeId;
        RefreshAll();
    }

    private async Task ScanAsync()
    {
        if (!_execution.LiveMemoryEnabled) return;
        await ScanCoreAsync(null, scheduled: true);
    }

    // Explicit result-only seam: never invokes an injected or runtime scanner.
    internal Task ScanControlledAsync(RecognitionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (_runtimeEffects || !_controlledRecognitionAllowed)
            throw new InvalidOperationException("Controlled recognition requires a non-runtime window and an existing absolute sandbox directory.");
        return ScanCoreAsync(result);
    }

    private async Task ScanCoreAsync(RecognitionResult? controlledResult,
        IAsyncEnumerable<DiagnosticRecognitionFrame>? controlledFrames = null, bool scheduled = false)
    {
        if (!_execution.LiveMemoryEnabled && controlledResult is null && controlledFrames is null && !scheduled) return;
        if (_scanInProgress || AutoScanCheck.IsChecked != true) return;
        var independentCadence = scheduled && UsesMap2320 && _recognizer is IModernBasicInventorySource;
        var lane = independentCadence ? BeginDiagnosticCadence(_recognizer) : DiagnosticReadLane.Full;
        if (lane is null)
        {
            _timer.Interval = _diagnosticCadence.NextDelay;
            return;
        }
        if (scheduled && !independentCadence) _timer.Interval = RecognitionInterval;
        _scanInProgress = true;
        _activityScanFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_diagnosticInventory is null)
            _observedCapture?.PresentationState("scanning", "none", DateTimeOffset.UtcNow);
        var generation = _scanGeneration;
        var matchGeneration = _adaptivePlanning.MatchGeneration;
        var recognizer = _recognizer;
        var startedTick = System.Diagnostics.Stopwatch.GetTimestamp();
        RecordActivity("scan.started", new { ScanGeneration = generation, Controlled = controlledResult is not null || controlledFrames is not null });
        RecognitionResult? diagnosticResult = null;
        var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        try
        {
            // 첫 인식은 메모리 구조 탐색이 필요해 몇 초 걸린다. 그동안 초기 문구가 남아 있지 않게 한다.
            if (RecognitionStatus.Text == "수동 모드") RecognitionStatus.Text = "게임 정보 찾는 중…";
            if (lane == DiagnosticReadLane.Basic)
            {
                await ReadBasicInventoryAsync(recognizer, generation, matchGeneration, cancellation.Token);
                return;
            }
            var result = controlledResult ?? await ReadRecognitionFramesAsync(recognizer, controlledFrames,
                generation, matchGeneration, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            // Keep the explicit match fence before reset-before-Ready handling.
            if (matchGeneration != _adaptivePlanning.MatchGeneration) return;
            if (!IsCurrentRecognition(generation, _scanGeneration, matchGeneration,
                _adaptivePlanning.MatchGeneration, ReferenceEquals(recognizer, _recognizer))) return;
            ObserveApplicationUpdateSafety(result);
            UpdateCompatibilityInfo(result);
            if (TryHandleModernInventoryObservation(result))
            {
                diagnosticResult = result;
                return;
            }
            _latestRecognitionObservation = result.Diagnostics.AdaptivePlanningObservation;
            if (!result.ShouldReplaceInventory)
                _mapSignals = NavigationAfterObservationLoss(_mapSignals) with {
                    RouteQuests = _liveSessionActive ? RouteQuestSnapshot.Unavailable(
                    "현재 항로개척 상태를 다시 확인하고 있습니다.") : RouteQuestSnapshot.Unknown };
            LogUnknownRawcodes(result);

            if (RecognitionPolicy.ShouldResetBeforeReadyInventory(result))
            {
                ResetMatchSession();
                _lastRound = 0;
            }
            if (_goroseiObservation.Current.MatchGeneration != _adaptivePlanning.MatchGeneration)
                _goroseiObservation.Reset(_adaptivePlanning.MatchGeneration);
            // The detailed consumer preserves native markers and checks execution-context authority.
            _goroseiObservation.AcceptRecognition(_adaptivePlanning.MatchGeneration, ++_recognitionRevision, result, _execution);
            ApplyDetectedGorosei(CurrentGorosei);
            _confirmedWaitingScans = result.State == RecognitionState.Waiting &&
                                     result.ConfirmsSessionBoundary
                ? _confirmedWaitingScans + 1
                : 0;
            if (result.ShouldReplaceInventory)
            {
                _waitingBoundaryConsumed = false;
                if (!_liveSessionActive)
                {
                    _telemetrySession.MarkSessionStart();
                    _matchDifficulty = "unknown";
                }
                _completedTopUnits.Observe(result.Entries);
                _completedTopUnits.ObserveGoalCraft(_settings.GoalUnitId, result.Entries);
                _greenBloodUsage.Observe(result.Entries);
                _automatic.Clear();
                foreach (var entry in result.Entries) _automatic[entry.UnitId] = entry;
                _mapSignals = result.MapSignals;
                _growthUnitIds.Clear();
                _growthUnitIds.UnionWith(result.Diagnostics.GrowthUnitIds);
                _automaticStale = false;
                _automaticDisconnected = false;
                _liveSessionActive = true;
                CaptureMatchTelemetry();
            }
            else if (RecognitionPolicy.ShouldClearAutomaticInventory(
                         result, _confirmedWaitingScans))
            {
                _automatic.Clear();
                _growthUnitIds.Clear();
                _automaticStale = false;
                _automaticDisconnected = true;
            }
            else
            {
                _automaticStale = _automatic.Count > 0;
                _automaticDisconnected = !RecognitionPolicy.ShouldUseLastGood(
                    result, _confirmedWaitingScans);
            }
            _telemetrySession.ObserveRecognition(result.State);
            if (result.ShouldReplaceInventory || result.State == RecognitionState.Waiting)
            {
                _outcome.Observe(result.Diagnostics.ObservedObjects, result.Diagnostics.ForeignObjects,
                    DateTimeOffset.UtcNow, result.ConfirmsSessionBoundary);
                if (result.Diagnostics.MapState is { } mapState)
                {
                    _outcome.ObserveRound(mapState.MaxRound);
                    _outcome.ObserveSettlement(mapState.SettlementCopies);
                    if (mapState.Difficulty is { Length: > 0 } difficulty &&
                        difficulty != "unknown")
                    {
                        _matchDifficulty = difficulty;
                        _outcome.ObserveDifficulty(difficulty);
                    }
                    _lastRound = mapState.MaxRound > 0 ? Math.Max(_lastRound, mapState.MaxRound) : 0;
                }
            }
            CaptureCoachObservation(result);
            if (!_coachCurrent || _automaticStale || _automaticDisconnected) InvalidateNormalCandidateObservation();
            RequestGameplayStatsForCurrentDifficulty();
            if (_outcome.Outcome is "fail" or "clear")
                SendMatchTelemetry();
            if (RecognitionPolicy.ShouldResetMatch(result, _confirmedWaitingScans, _waitingBoundaryConsumed))
            {
                ResetMatchSession();
                _lastRound = 0;
                _waitingBoundaryConsumed = true;
            }
            ApplyOverlayVisibility(result);
            RecognitionStatus.Text = RecognitionDisplayStatus(result);
            RecognitionStatus.Foreground = result.State switch
            {
                RecognitionState.Ready => RandyPickTheme.Success,
                RecognitionState.Waiting => RandyPickTheme.Warning,
                RecognitionState.TransientReadError => RandyPickTheme.Warning,
                _ => RandyPickTheme.Danger
            };
            var detail = result.ShouldReplaceInventory ? RecognitionDisplayDetail(result) : RecognitionStatus.Text;
            if (!result.ShouldReplaceInventory && _automatic.Count > 0)
                detail += " · 마지막으로 확인한 패를 표시합니다. 현재 패는 다시 확인해 주세요.";
            RefreshIfScanStateChanged(detail);
        }
        catch (OperationCanceledException)
        {
            RecordActivity("scan.cancelled", new { ScanGeneration = generation });
            if (IsCurrentRecognition(generation, _scanGeneration, matchGeneration,
                    _adaptivePlanning.MatchGeneration, ReferenceEquals(recognizer, _recognizer)))
            {
                _observedCapture?.Read(false, "scan", "cancelled", System.Diagnostics.Stopwatch.GetElapsedTime(startedTick), null, DateTimeOffset.UtcNow);
                InvalidateDiagnosticInventoryObservation();
            }
        }
        catch (Exception error)
        {
            RecordActivity("scan.failed", new
            {
                ScanGeneration = generation, ErrorType = error.GetType().FullName, error.Message, error.StackTrace
            });
            if (IsCurrentRecognition(generation, _scanGeneration, matchGeneration,
                    _adaptivePlanning.MatchGeneration, ReferenceEquals(recognizer, _recognizer)))
            {
                _observedCapture?.Read(false, "scan", "read-error", System.Diagnostics.Stopwatch.GetElapsedTime(startedTick), null, DateTimeOffset.UtcNow);
                InvalidateDiagnosticInventoryObservation();
                _updateObservationPending = true;
                if (UsesMap2320)
                {
                    RecognitionStatus.Text = "유닛을 읽지 못했어요. 다시 확인하고 있어요";
                    RecognitionStatus.Foreground = RandyPickTheme.Warning;
                    return;
                }
                _goroseiObservation.Accept(_adaptivePlanning.MatchGeneration, ++_recognitionRevision,
                    GoroseiMode.None, false);
                SetOverlayHandAvailability(false);
                _automaticStale = _automatic.Count > 0;
                _automaticDisconnected = false;
                RecognitionStatus.Text = "유닛을 읽지 못했어요. 마지막으로 확인한 유닛을 표시해요";
                RecordGameplayRecognitionGap(RecognitionState.TransientReadError);
                _mapSignals = NavigationAfterObservationLoss(_mapSignals) with { RouteQuests = RouteQuestSnapshot.Unknown };
                RecognitionStatus.Foreground = RandyPickTheme.Warning;
                RefreshIfScanStateChanged("인식 중 오류가 발생했습니다. 다음 자동 인식을 기다립니다.");
            }
        }
        finally
        {
            var wasCancelled = cancellation.IsCancellationRequested;
            if (ReferenceEquals(_scanCancellation, cancellation)) _scanCancellation = null;
            cancellation.Dispose();
            _scanInProgress = false;
            RecordActivity("scan.completed", new
            {
                ScanGeneration = generation, Cancelled = wasCancelled,
                DurationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startedTick).TotalMilliseconds
            });
            if (independentCadence)
            {
                _diagnosticCadence.Complete();
                _timer.Interval = _diagnosticCadence.NextDelay;
            }
            _activityScanFinished.TrySetResult();
            if (!independentCadence && !wasCancelled && diagnosticResult is not null && controlledResult is null && controlledFrames is null)
                QueueDiagnosticScan(diagnosticResult, System.Diagnostics.Stopwatch.GetElapsedTime(startedTick),
                    generation, matchGeneration, recognizer);
        }
    }



    private void QueueDiagnosticScan(RecognitionResult result, TimeSpan elapsed, int generation,
        long matchGeneration, IInventoryRecognizer recognizer)
    {
        bool CanContinue() => _execution.LiveMemoryEnabled && !_diagnosticClosed && UsesMap2320 &&
            !_coachPaused && _timer.IsEnabled && AutoScanCheck.IsChecked == true && !_scanInProgress &&
            !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished &&
            IsCurrentRecognition(generation, _scanGeneration, matchGeneration,
                _adaptivePlanning.MatchGeneration, ReferenceEquals(recognizer, _recognizer));
        _diagnosticScanContinuation.TryQueue(result, elapsed,
            _execution.LiveMemoryEnabled && recognizer is WarcraftMemoryRecognitionService,
            action => Dispatcher.BeginInvoke(action, DispatcherPriority.Background),
            CanContinue, () => _ = ScanAsync());
    }

    /// <summary>
    /// 확정된 세션 경계에서 이전 판의 패·추천 선택·추적 상태를 한 번에 비운다.
    /// 일부 필드만 초기화하면 다음 판에 이전 패나 선택 카드가 다시 나타날 수 있다.
    /// </summary>
    private void ResetMatchSession()
    {
        InvalidateDiagnosticInventoryObservation(resetContext: true);
        _updateObservationPending = true;
        _normalFrame = null;
        _normalCandidates?.Reset();
        SendMatchTelemetry();
        ResetGameplayTelemetry();
        _automatic.Clear();
        _automaticStale = false;
        _automaticDisconnected = true;
        _outcome.Reset();
        _telemetrySession.Reset();
        _matchDifficulty = "unknown";
        _liveSessionActive = false;
        _autoStartApplied = false;
        _mapSignals = MapSignals.Empty;
        _navigationSession.Reset();
        _guideRuntime = BulletGuideRuntimeState.Unknown;
        _loadedClearCount = null;
        _guideFastUnique = FastUniqueState.Unknown;
        _guideObservedLegendIds.Clear();
        _guideObservedLegendCounts.Clear();
        _queenInput = QueenConversionInput.Unknown;
        _guidePlan = null;
        _coachCurrent = false;
        _coachSignals = CoachSignalAdapter.Read(null, 0, 0, false);
        _lastCoachSignalRevision = -1;
        _lastCoachFrame = null;
        _lastCoachDecision = null;
        _beginnerGoals = new BeginnerGoalPolicy(_catalog, UsesMap2320 ? ClearBuildStats.Empty : _clearStats, _matchDifficulty);
        _coachPaused = false;
        _updatingSelections = true;
        BothRouteQuestsCheck.IsChecked = false;
        _updatingSelections = false;
        _routeQuestEvaluation = null;
        _adaptivePlanningApplied = null;
        _latestRecognitionObservation = AdaptivePlanningRecognitionObservation.Empty;
        _lastAdaptivePlanningPerformance = null;
        _observedLegendIds.Clear();
        _pendingAdaptiveFingerprint = null;
        _pendingAdaptiveLegendIds = [];
        _adaptivePlanning.ConfirmReset(_adaptivePlanning.MatchGeneration + 1);
        _goroseiObservation.Reset(_adaptivePlanning.MatchGeneration);
        _detectedGorosei = GoroseiMode.None;
        _combatObservations = [];
        if (CurrentPlayMode == PlayMode.Manual) _adaptivePlanning.LatchManualGoalOverride();
        _adaptiveDecisionTrace.ConfirmedMatchReset();
        _completedTopUnits.Reset();
        _firstRareRecommendationGate.Reset();
        _firstRareTargetPolicy.Reset();
        _craftCommitment.Reset();
        _greenBloodUsage.Reset();
        _selectedRouteId = null;
        _clusterHeadRouteId = null;
        _boardRecs = [];
        _boardPlan = [];
        _boardBanner = null;
        _boardShowsClusterChildren = true;
        _lastScanSignature = null;
        _confirmedWaitingScans = 0;
        _overlayVisibility = default;
        _overlayHiddenStreak = 0;
        SetOverlayHandAvailability(false);
    }

    private void GoalCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelections) return;
        // 유저가 직접 상위를 고르면 이번 판의 자동 시작(희귀함 우선) 단계를 끝낸다.
        if (_initialized)
        {
            if (CurrentPlayMode != PlayMode.Manual) return;
            _settings.ManualGoalUnitId = SelectedGoal?.Id;
            ValidateSecondaryGoal();
            _autoStartApplied = true;
            _adaptivePlanning.LatchManualGoalOverride();
        }
        if (_initialized)
        {
            RepopulateNavigationChoices();
            RepopulateBuildVariants();
        }
        RefreshAll();
    }


// 니카 이감/노이감은 추천 엔진이 패 기준으로 자동 판정한다. 사용자 토글은 노출하지 않는다.
private void RepopulateBuildVariants()
{
    _updatingSelections = true;
    try
    {
        BuildVariantLabel.Visibility = Visibility.Collapsed;
        BuildVariantCombo.Visibility = Visibility.Collapsed;
        BuildVariantSummaryText.Visibility = Visibility.Collapsed;
        BuildVariantCombo.ItemsSource = null;
        BuildVariantCombo.SelectedItem = null;
        BuildVariantSummaryText.Text = "";
    }
    finally
    {
        _updatingSelections = false;
    }
}

private void BuildVariantCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelections) return;
        if (BuildVariantCombo.SelectedItem is BuildVariant variant)
            BuildVariantSummaryText.Text = variant.Summary;
        RefreshAll();
    }

    private void NavigationCategoryCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelections) return;
        if (NavigationCategoryCombo.SelectedItem is not NavigationCategory category) return;
        EnsureAutomaticNavigation();

        var current = NavigationCombo.SelectedItem as NavigationOption;
        var options = VisibleNavigations(category.Id);
        if (options.Count == 0) options = ApplicationNavigationsForCategory(category.Id).ToList();
        NavigationCombo.ItemsSource = options;
        NavigationCombo.SelectedItem = current is not null &&
                                       current.CategoryId.Equals(category.Id, StringComparison.OrdinalIgnoreCase)
            ? current
            : options.FirstOrDefault();
    }

    private void NavigationCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelections) return;
        EnsureAutomaticNavigation();
        RefreshAll();
    }

    private void EnsureAutomaticNavigation()
    {
        if (UsesMap2320) { ConfigureMap2320Navigation(); return; }
        if (!_initialized) return;
        _settings.AutoRecommendNavigation = true;
        if (_adaptivePlanning.ManualLatches.NavigationOverride)
            _adaptivePlanning.ClearManualNavigationOverride();
        _updatingSelections = true;
        try { AutoNavigationCheck.IsChecked = true; }
        finally { _updatingSelections = false; }
        UpdateNavigationSelectionVisibility();
    }

    private void UpdateNavigationSelectionVisibility()
    {
        if (UsesMap2320) { ConfigureMap2320Navigation(); return; }
        var visibility = NavigationSelectionVisibility(_settings.AutoRecommendNavigation);
        NavigationLabel.Text = "항법 · 모든 모드 자동 추천";
        NavigationLabel.Visibility = Visibility.Visible;
        NavigationSelectRow.Visibility = visibility;
    }

    internal static Visibility NavigationSelectionVisibility(bool automaticRecommendation) =>
        Visibility.Collapsed;

    private void GoroseiCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelections) return;
        if (GoroseiCombo.SelectedItem is GoroseiOption option)
        {
            if (_initialized)
            {
                _explicitGoroseiScenario = option.Mode;
                _userGoroseiPlan = new(option.Mode, "UserPlan");
                _settings.BulletPlanningGoroseiMode = option.Mode.ToString();
                if (_persistSettings) _execution.SaveSettings(_settings);
            }
            GoroseiSummaryText.Text = "직접 고른 계획 · 실제 효과 확인 아님 · " + option.Summary;
        }
        RefreshAll();
    }

    private void AutoScan_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_execution.LiveMemoryEnabled) return;
        if (!_initialized) return;
        _settings.AutoScanEnabled = AutoScanCheck.IsChecked == true;
        if (AutoScanCheck.IsChecked == true)
        {
            _timer.Start();
            _ = ScanAsync();
        }
        else
        {
            _timer.Stop();
            _scanCancellation?.Cancel();
            InvalidateNormalCandidateObservation();
            ApplyScanStopObservation();
        }
    }

    private void ClearDataRefresh_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        var enabled = ClearDataRefreshCheck.IsChecked == true;
        var changed = _settings.ClearDataAutoRefresh != enabled;
        _settings.ClearDataAutoRefresh = enabled;
        if (changed && enabled) _ = RefreshClearDataAsync();
    }

    private void ClickThrough_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _settings.ClickThroughOverlay = ClickThroughCheck.IsChecked == true;
        _overlay.SetClickThrough(_settings.ClickThroughOverlay);
        if (_persistSettings) _execution.SaveSettings(_settings);
    }

    private void OverlayModeCombo_OnSelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized ||
            OverlayModeCombo.SelectedItem is not ComboBoxItem item ||
            !Enum.TryParse<OverlayDisplayMode>(item.Tag?.ToString(),
                out var mode))
            return;
        ApplyOverlayDisplayState(OverlayDisplayPolicy.Select(
            CurrentOverlayDisplayState(), mode), save: true);
    }

    /// <summary>식별자 없는 매치 집계를 다음 실행의 전송 큐에 넣는다.</summary>
    private void SendMatchTelemetry()
    {
        try
        {
            if (!_liveSessionActive) return;
            RecordCoachOutcome(_outcome.Outcome);
            _telemetrySession.Send(
                UpdateService.CurrentVersion.ToString(3),
                "2.314",
                string.IsNullOrWhiteSpace(_matchDifficulty) ? "unknown" : _matchDifficulty,
                _outcome.Outcome);
        }
        catch { /* fail-silent */ }
    }

    /// <summary>개별 패 대신 coarse count만 마지막 정상 관측으로 남긴다.</summary>
    private void CaptureMatchTelemetry()
    {
        try
        {
            if (!_liveSessionActive) return;
            _telemetrySession.Capture(
                _automatic.Values.Where(entry => entry.Count > 0).Sum(entry => entry.Count),
                _completedTopUnits.CompletedUnitIds.Count);
        }
        catch { /* fail-silent */ }
    }

    private void EnableOverlayMove_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CurrentOverlayDisplayState().Available)
        {
            FooterStatus.Text = "패가 인식되면 오버레이 위치를 옮길 수 있습니다.";
            return;
        }
        var wasClickThrough = _settings.ClickThroughOverlay;
        ClickThroughCheck.IsChecked = false;
        _relockAfterMove = wasClickThrough;
        _settings.ClickThroughOverlay = false;
        if (UsesMap2320)
        {
            ApplyDiagnosticOverlayVisibility();
            _overlay.SetClickThrough(false);
            if (_overlay.IsVisible) { _overlay.EnsureVisible(); _overlay.Activate(); }
            if (_overlay.Stats.IsVisible) _overlay.Stats.EnsureVisible();
            FooterStatus.Text = "화면에 보이는 안내창의 윗부분을 끌어 옮겨 주세요. 숨겨져 있다면 먼저 표시 설정을 바꿔 주세요.";
            return;
        }
        if (!_overlay.IsVisible)
        {
            _overlay.Show();
            OverlayButton.Content = "오버레이 숨기기";
        }
        _overlay.EnsureVisible();
        _overlay.Stats.EnsureVisible();
        _overlay.SetClickThrough(false);
        _overlay.Activate();
        FooterStatus.Text = "유닛 수치 창과 추천 창의 윗부분을 각각 끌어 옮겨 주세요.";
    }

    private void Overlay_OnPositionCommitted(double left, double top)
    {
        _settings.OverlayLeft = left;
        _settings.OverlayTop = top;
        if (_persistSettings) _execution.SaveSettings(_settings);
        FooterStatus.Text = $"추천 창 위치를 저장했습니다: 가로 {left:0}, 세로 {top:0}";
        if (!_relockAfterMove) return;
        _relockAfterMove = false;
        ClickThroughCheck.IsChecked = true;
    }

    /// <summary>
    /// 위치를 한 번도 정하지 않았으면 패 상태 창은 화면 왼쪽 가장자리, 추천 창은 오른쪽 가장자리에
    /// 세로 중앙으로 놓는다. 게임 화면 가운데를 비워 두기 위한 기본값이다.
    /// </summary>
    private void ApplyDefaultOverlayLayout()
    {
        if (System.Windows.Forms.Screen.PrimaryScreen is not { } screen) return;
        var dpi = VisualTreeHelper.GetDpi(_overlay);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
        var work = screen.WorkingArea;
        var left = work.Left / scaleX;
        var right = work.Right / scaleX;
        var top = work.Top / scaleY;
        var height = work.Height / scaleY;
        const double margin = 6;

        if (_settings.StatsOverlayLeft is null)
        {
            _overlay.Stats.Left = left + margin;
            _overlay.Stats.Top = top + Math.Max(0, (height - _overlay.Stats.Height) / 2);
        }
        if (_settings.OverlayLeft is null)
        {
            _overlay.Left = right - _overlay.Width - margin;
            _overlay.Top = top + Math.Max(0, (height - _overlay.Height) / 2);
        }
    }

    private void StatsOverlay_OnPositionCommitted(double left, double top)
    {
        _settings.StatsOverlayLeft = left;
        _settings.StatsOverlayTop = top;
        if (_persistSettings) _execution.SaveSettings(_settings);
        FooterStatus.Text = $"패 상태 창 위치를 저장했습니다: 가로 {left:0}, 세로 {top:0}";
        if (!_relockAfterMove) return;
        _relockAfterMove = false;
        ClickThroughCheck.IsChecked = true;
    }

    private void SaveOverlayPosition()
    {
        if (_overlay is null || !_overlay.IsLoaded) return;
        var position = _overlay.CurrentPosition();
        _settings.OverlayLeft = position.Left;
        _settings.OverlayTop = position.Top;
        if (!_overlay.Stats.IsLoaded) return;
        var statsPosition = _overlay.Stats.CurrentPosition();
        _settings.StatsOverlayLeft = statsPosition.Left;
        _settings.StatsOverlayTop = statsPosition.Top;
    }

    private void ToggleOverlay_OnClick(object sender, RoutedEventArgs e) =>
        ToggleOverlayVisibility();

    // 설정 창도 모니터 해상도(FHD~8K)에 맞춰 창 전체를 비례 확대한다.
    private void ApplyResolutionScale()
    {
        var scale = UiScale.Apply(this, 1080, 720, resizeWindow: !_mainWindowSizedByUser);
        MinWidth = MainWindowGeometry.MinimumWidth * scale;
        MinHeight = MainWindowGeometry.MinimumHeight * scale;
    }

    private void ToggleOverlayVisibility()
    {
        if (!CurrentOverlayDisplayState().Available)
        {
            HideOverlayWindows();
            OverlayButton.Content = UsesMap2320 ? "관측 참고 대기 중" : "패 인식 대기 중";
            FooterStatus.Text = UsesMap2320 ? "유닛 정보를 읽으면 표시해요. 실제 보유와 조합 가능 여부는 게임에서 확인해 주세요." : "게임 패가 인식되면 오버레이가 자동으로 표시됩니다.";
            return;
        }

        var state = OverlayDisplayPolicy.Toggle(CurrentOverlayDisplayState());
        ApplyOverlayDisplayState(state, save: true);
    }

    /// <summary>스캔 결과를 가시성 정책으로 해석해 오버레이를 전이한다. 상태가
    /// 실제로 바뀔 때만 UI를 건드린다 — 신세계 라운드 진입 구간의 깜빡임 방지.</summary>
    private void ApplyOverlayVisibility(RecognitionResult result)
    {
        var shownNow = _overlay.IsVisible || _overlay.Stats.IsVisible;
        if (OverlayVisibilityPolicy.ShouldCountTowardHide(result))
            _overlayHiddenStreak++;
        else
            _overlayHiddenStreak = 0;

        switch (OverlayVisibilityPolicy.Decide(shownNow, result, _overlayHiddenStreak))
        {
            case OverlayVisibilityDecision.Show:
                _overlayVisibility = _overlayVisibility.WithHandAvailability(true);
                ShowOverlayWindows();
                break;
            case OverlayVisibilityDecision.Hide:
                _overlayVisibility = _overlayVisibility.WithHandAvailability(false);
                HideOverlayWindows();
                OverlayButton.Content = "패 인식 대기 중";
                break;
            // KeepShown/KeepHidden: 현재 상태 유지 — UI 문구도 건드리지 않는다.
        }
    }

    private void SetOverlayHandAvailability(bool available)
    {
        if (!available) InvalidateNormalCandidateObservation();
        if (_overlayVisibility.HandAvailable == available)
        {
            if (!available) HideOverlayWindows();
            return;
        }

        _overlayVisibility = _overlayVisibility.WithHandAvailability(available);
        if (!available)
        {
            HideOverlayWindows();
            OverlayButton.Content = "패 인식 대기 중";
            return;
        }

        ShowOverlayWindows();
    }

    private void ShowOverlayWindows()
    {
        if (UsesMap2320) { ApplyDiagnosticOverlayVisibility(); return; }
        _overlay.Stats.SetDisplayMode(_settings.OverlayDisplayMode);
        _craftWindow?.SetDisplayAllowed(_overlayVisibility.HandAvailable && _settings.OverlayDisplayMode != OverlayDisplayMode.Hidden);
        var visibility = OverlayDisplayPolicy.Visibility(
            CurrentOverlayDisplayState());
        if (visibility.RecommendationVisible)
        {
            if (!_overlay.IsVisible) _overlay.Show();
        }
        else if (_overlay.IsVisible) _overlay.Hide();
        if (visibility.StatsVisible)
        {
            if (!_overlay.Stats.IsVisible) _overlay.Stats.Show();
        }
        else if (_overlay.Stats.IsVisible) _overlay.Stats.Hide();
        if (!visibility.RecommendationVisible && !visibility.StatsVisible)
        {
            OverlayButton.Content = _overlayVisibility.HandAvailable
                ? "오버레이 보이기"
                : "패 인식 대기 중";
            return;
        }
        _overlay.Dispatcher.BeginInvoke(new Action(ApplyDefaultOverlayLayout),
            System.Windows.Threading.DispatcherPriority.Loaded);
        if (visibility.RecommendationVisible) _overlay.EnsureVisible();
        if (visibility.StatsVisible) _overlay.Stats.EnsureVisible();
        _overlay.SetClickThrough(_settings.ClickThroughOverlay);
        OverlayButton.Content = visibility.RecommendationVisible
            ? "오버레이 숨기기"
            : "패수치 숨기기";
    }

    private void HideOverlayWindows()
    {
        _craftWindow?.SetDisplayAllowed(false);
        if (_overlay.IsVisible) _overlay.Hide();
        if (_overlay.Stats.IsVisible) _overlay.Stats.Hide();
    }

    private OverlayDisplayState CurrentOverlayDisplayState() =>
        new(_settings.OverlayDisplayMode,
            _settings.LastVisibleOverlayDisplayMode,
            UsesMap2320 ? DiagnosticReferencePresentationPolicy.ToggleAvailable(_diagnosticOverlaySession,
                DiagnosticPresentationFencesHold(), HasPendingApplicationUpdate) : _overlayVisibility.HandAvailable);

    private void ApplyOverlayDisplayState(OverlayDisplayState state,
        bool save)
    {
        _settings.OverlayDisplayMode = state.Mode;
        _settings.LastVisibleOverlayDisplayMode = state.LastVisibleMode;
        if (!UsesMap2320)
            _overlayVisibility = _overlayVisibility.WithHandAvailability(state.Available);
        if (save && _persistSettings) _execution.SaveSettings(_settings);
        ShowOverlayWindows();
    }

    private void HideOverlayByUser() =>
        ApplyOverlayDisplayState(OverlayDisplayPolicy.Select(
            CurrentOverlayDisplayState(), OverlayDisplayMode.Hidden),
            save: true);

    // 워크 창이 포커스를 가진 상태에서도 오버레이를 켜고 끌 수 있는 전역 단축키.
    // 기본 F1. 사용자 지정 키는 유지하며 누르고 있는 동안의 반복은 등록 단계에서 차단한다.
    private const int OverlayHotkeyId = 0xB0BA;
    private const int WmHotkey = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Windows 11 DWM으로 제목바를 앱 테마 색에 맞춘다(네이티브 버튼·동작 유지).
    // 미지원 OS(Win10 등)에서는 다크 모드 캡션까지만 적용되고 색 지정은 무시된다.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value,
        int size);

    private static void ApplyThemedTitleBar(IntPtr handle)
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)); // 다크 캡션
        var caption = RandyPickTheme.ToColorRef(RandyPickTheme.Canvas);
        _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        var text = RandyPickTheme.ToColorRef(RandyPickTheme.Text);
        _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        var border = RandyPickTheme.ToColorRef(RandyPickTheme.Border);
        _ = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
    }

    private IntPtr _hotkeyWindowHandle;
    private bool _hotkeyRegistered;
    private bool _capturingHotkey;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!_initialized) return;
        var helper = new WindowInteropHelper(this);
        ApplyThemedTitleBar(helper.Handle);
        if (!_runtimeEffects) return;
        _hotkeyWindowHandle = helper.Handle;
        HwndSource.FromHwnd(helper.Handle)?.AddHook(OnWindowMessage);
        Closed += (_, _) =>
        {
            if (_hotkeyRegistered) _execution.RunRuntime(() => UnregisterHotKey(_hotkeyWindowHandle, OverlayHotkeyId));
        };
        if (OverlayHotkeyPolicy.Normalize(_settings) && _persistSettings) _execution.SaveSettings(_settings);
        ApplyOverlayHotkey();
    }

    /// <summary>설정된 키로 전역 단축키를 다시 등록하고 화면 문구를 갱신한다.</summary>
    private void ApplyOverlayHotkey()
    {
        if (!_runtimeEffects) return;
        if (_hotkeyWindowHandle == IntPtr.Zero) return;
        if (_hotkeyRegistered)
        {
            _execution.RunRuntime(() => UnregisterHotKey(_hotkeyWindowHandle, OverlayHotkeyId));
            _hotkeyRegistered = false;
        }
        var key = OverlayHotkeyPolicy.ResolveKey(_settings.OverlayToggleKey);
        var label = HotkeyLabel(key);
        HotkeyBox.Text = label;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0 || !_execution.TryRuntime(() => RegisterHotKey(_hotkeyWindowHandle, OverlayHotkeyId, OverlayHotkeyPolicy.RegistrationModifiers, virtualKey)))
        {
            HotkeyHint.Text = $"{label} 키는 다른 프로그램이 쓰고 있어 등록하지 못했습니다. 다른 키로 바꿔 주세요.";
            FooterStatus.Text = $"오버레이 단축키({label}) 등록 실패 — 버튼으로 켜고 끄세요.";
            OverlayButton.ToolTip = "단축키를 설정하지 못했습니다";
            return;
        }
        _hotkeyRegistered = true;
        HotkeyHint.Text = "게임 중에도 이 키로 오버레이를 켜고 끕니다.";
        OverlayButton.ToolTip = $"게임 중에도 쓰는 단축키: {label}";
        FooterStatus.Text = $"게임 중에도 {label} 키로 안내창을 켜거나 끌 수 있어요";
    }

    private static string HotkeyLabel(Key key) => key switch
    {
        Key.Capital => "Caps Lock",
        Key.Scroll => "Scroll Lock",
        Key.Snapshot => "Print Screen",
        Key.Pause => "Pause",
        Key.OemTilde => "`",
        _ => key.ToString()
    };

    private void HotkeyCapture_OnClick(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        HotkeyCaptureButton.Content = "입력 대기";
        HotkeyBox.Text = "키를 누르세요";
        HotkeyHint.Text = "쓸 키를 한 번 누르면 저장됩니다. Esc로 취소.";
        HotkeyBox.Focus();
    }

    private void Hotkey_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // 조합키 단독은 단축키가 될 수 없다. 누르고 있는 동안 무시하고 다음 키를 기다린다.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return;
        _capturingHotkey = false;
        HotkeyCaptureButton.Content = "키 변경";
        if (key == Key.Escape)
        {
            ApplyOverlayHotkey();
            return;
        }
        _settings.OverlayToggleKey = key.ToString();
        _settings.OverlayToggleKeyCustomized = true;
        if (_persistSettings) _execution.SaveSettings(_settings);
        ApplyOverlayHotkey();
    }

    private void Hotkey_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_capturingHotkey) return;
        _capturingHotkey = false;
        HotkeyCaptureButton.Content = "키 변경";
        ApplyOverlayHotkey();
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == OverlayHotkeyId)
        {
            if (OverlayHotkeyPolicy.ShouldToggle(_hotkeyRegistered, _capturingHotkey))
                ToggleOverlayVisibility();
            handled = true;
        }
        return IntPtr.Zero;
    }

}
