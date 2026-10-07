using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OrandOverlay;

public partial class OverlayWindow : OverlayWindowBase
{
    private string _lastRecommendationSignature = "";
    private string _lastStatsSignature = "";
    private string _lastRerollSignature = "";
    private IReadOnlyList<Recommendation> _recommendations = [];
    private IReadOnlyList<AutoCombineStep> _combinePlan = [];
    private string? _selectedRouteId;
    private string? _clusterHeadRouteId;
    private Func<Recommendation, IReadOnlyList<Recommendation>>? _storyChildren;
    private Func<IReadOnlyList<Recommendation>, string?, IReadOnlyList<Recommendation>>? _recascade;
    private PlannerEvidenceView? _plannerEvidence;
    private readonly BulletStrategyProfile? _bulletProfile;
    private int _plannerRound;
    private int? _completedStoryStage;
    private AdaptivePlanningApplied? _plannerApplied;
    private InventoryStatSummary? _inventoryStats;
    private bool _greenBloodKnown;
    private bool _greenBloodUsed;
    private double _stunTarget = 1.4;
    private IReadOnlyDictionary<string, int>? _recognizedInventory;
    private bool _showRouteRootAsCurrentCraft;
    private Action<string>? _onRouteSelected;
    private string? _navigationExplanation;

    public void RenderNavigationContext(string candidate, string explanation)
    {
        NavigationCandidateText.Text = candidate;
        NavigationCandidateText.Visibility = Visibility.Visible;
        _navigationExplanation = explanation;
        FillBoard();
        HideExpertCoachHeader();
    }

    public OverlayWindow()
    {
        InitializeComponent();
        InitializeResizeBehavior();
        NonActivatingWindowBehavior.Attach(this);
        InitializePendingUpdateNotice();
        try
        {
            _bulletProfile = BulletStrategyProfileLoader.LoadFromDirectory(
                Path.Combine(AppContext.BaseDirectory, "Data"));
        }
        catch (InvalidDataException)
        {
            _bulletProfile = null;
        }
        OverlayVersionText.Text = RandyPickBrand.BetaLabel;
        Loaded += (_, _) =>
        {
            OverlayTheme.AttachRoundClip(NowWell, OverlayTheme.WellRadius);
            OverlayTheme.AttachRoundClip(FlowWell, OverlayTheme.WellRadius);
            OverlayTheme.AttachRoundClip(BoardWell, OverlayTheme.WellRadius);
        };
    }

    protected override double DesignWidth => 540;
    protected override double DesignHeight => _detachedCraftLayout && _presentedMode == PlayMode.Normal ? 480 : 740;
    protected override UIElement? ClickThroughIndicator => ClickThroughBadge;

    public StatsOverlayWindow Stats { get; } = new();

    public override void SetClickThrough(bool enabled)
    {
        base.SetClickThrough(enabled);
        Stats.SetClickThrough(enabled);
        CoachView.SetInputHint(enabled
            ? "클릭 통과 켜짐 · 항법 확인과 설정 변경은 메인 창에서 하세요." : "");
    }

    private WrapPanel EmergencyPanel => Stats.EmergencyPanel;
    private TextBlock EmergencyHeader => Stats.EmergencyHeader;
    private UniformGrid CoreKpiPanel => Stats.CoreKpiPanel;
    private StackPanel CurrentStatsPanel => Stats.CurrentStatsPanel;
    private WrapPanel RareRerollPanel => Stats.RareRerollPanel;
    private WrapPanel SpecialPanel => Stats.SpecialPanel;
    private TextBlock SpecialHeader => Stats.SpecialHeader;
    private WrapPanel GreenBloodPanel => Stats.GreenBloodPanel;
    private TextBlock GreenBloodHeader => Stats.GreenBloodHeader;

    public event Action<FrameworkElement>? ReRecommendRequested;

    private void ReRecommendButton_OnClick(object sender, RoutedEventArgs e) =>
        ReRecommendRequested?.Invoke((FrameworkElement)sender);

    private void SettlementButton_OnClick(object sender, RoutedEventArgs e)
    {
        // 레이아웃 유지용 플레이스홀더 — 정산 동작은 임시 비활성화됐다.
    }

    public void UpdateStatus(string status) => StatusText.Text = status;

    public void RenderPlannerEvidence(int round, AdaptivePlanningApplied? applied,
        bool signalsUnknown = false, string? unknownReason = null,
        StoryRewardSequenceDecision? storySequence = null)
    {
        RenderPlannerEvidence(round, applied, signalsUnknown, unknownReason, storySequence,
            _completedStoryStage);
    }

    public void RenderPlannerEvidence(int round, AdaptivePlanningApplied? applied,
        bool signalsUnknown, string? unknownReason,
        StoryRewardSequenceDecision? storySequence, int? completedStoryStage,
        ManualLatches? currentManualLatches = null)
    {
        _plannerRound = round;
        _completedStoryStage = completedStoryStage;
        _plannerApplied = applied;
        _plannerEvidence = RecommendationPresentation.PlannerEvidence(
            round, applied, signalsUnknown, unknownReason, storySequence, currentManualLatches);
        ApplyPhaseBanner();
        if (currentManualLatches?.GoalOverride == true &&
            CarryModeText.Text == RecommendationPresentation.CarryModeLabel(GoalCarryMode.Unknown))
            CarryModeText.Text = "상위 수 판단 미확인 · 1상위 우선";
        FillBoard();
        HideExpertCoachHeader();
    }

    public void Render(string goalName, IReadOnlyList<Recommendation> recommendations,
        InventoryStatSummary stats, IReadOnlyList<RareRerollAdvice> rareRerolls,
        IReadOnlyList<GreenBloodAdvice> greenBloodAdvice, bool greenBloodOwned,
        IReadOnlyList<AutoCombineStep> combinePlan, string status, bool magicGoal = false,
        IReadOnlyList<EmergencySummonAdvice>? emergencySummons = null,
        GoroseiMode gorosei = GoroseiMode.None,
        bool greenBloodUsed = false,
        IReadOnlyList<SpecialDismantleAdvice>? specialAdvice = null,
        double stunTarget = 1.4,
        double stunCap = 1.5,
        string? phaseHint = null,
        Func<Recommendation, IReadOnlyList<Recommendation>>? storyChildren = null,
        Func<IReadOnlyList<Recommendation>, string?, IReadOnlyList<Recommendation>>? recascade = null,
        IReadOnlyList<InventoryEntry>? inventory = null,
        bool showRouteRootAsCurrentCraft = false,
        Action<string>? onRouteSelected = null)
    {
        _showRouteRootAsCurrentCraft = showRouteRootAsCurrentCraft;
        _onRouteSelected = onRouteSelected;
        _inventoryStats = stats;
        _greenBloodKnown = greenBloodOwned || greenBloodUsed;
        _greenBloodUsed = greenBloodUsed;
        _stunTarget = stunTarget;
        _recognizedInventory = inventory?.Where(entry => entry.Count > 0)
            .GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        _storyChildren = storyChildren;
        _recascade = recascade;
        GoalText.Text = goalName;
        var lead = recommendations.FirstOrDefault();
        if (lead?.CombatReadiness is { } readiness)
        {
            CarryModeText.Text =
                RecommendationPresentation.CarryModeLabel(lead.CarryMode);
            CarryModeText.Visibility = Visibility.Visible;
            ReadinessText.Text =
                RecommendationPresentation.ReadinessLine(readiness) +
                (lead.DeferredSecondaryTopReason is { Length: > 0 } reason
                    ? $"\n{reason}"
                    : "");
            ReadinessText.Foreground = readiness.IsReady
                ? OverlayTheme.OkBrush
                : OverlayTheme.WarnBrush;
            ReadinessText.Visibility = Visibility.Visible;
            Stats.SetReadiness(
                RecommendationPresentation.ReadinessLine(readiness),
                readiness.IsReady);
        }
        else
        {
            CarryModeText.Visibility = Visibility.Collapsed;
            ReadinessText.Visibility = Visibility.Collapsed;
            Stats.SetReadiness(null, ready: false);
        }
        ApplyPhaseBanner(phaseHint);
        StatusText.Text = status;
        RenderCurrentStats(stats, magicGoal, gorosei, stunTarget, stunCap, lead?.CombatReadiness);
        RenderRareRerolls(rareRerolls, recommendations.Count > 0);
        RenderSpecialAdvice(specialAdvice ?? []);
        RenderGreenBloodAdvice(greenBloodAdvice, greenBloodOwned, greenBloodUsed);
        RenderEmergencySummons(emergencySummons ?? []);

        var signature = RecommendationSignature(recommendations) + "|" +
                        BulletSignalSignature() + "|" +
                        showRouteRootAsCurrentCraft + "|" +
                        string.Join("|", combinePlan.Select(step =>
                            $"{step.TargetUnitId}:{step.TriggerUnitId}:{step.Key}:{string.Join(",", step.Commands)}"));
        if (signature == _lastRecommendationSignature) return;
        _lastRecommendationSignature = signature;
        _recommendations = recommendations;
        _combinePlan = combinePlan;
        PreserveBoardSelection();
        ApplySelectionCascade();
        FillBoard();
    }

    private void FillBoard()
    {
        var head = ClusterHead();
        var children = head is null
            ? []
            : _storyChildren?.Invoke(head) ?? [];
        if (!BoardSelection.IsKnown(_recommendations, children, _selectedRouteId))
            _selectedRouteId = head?.Route.Id;
        var selected = BoardSelection.Resolve(_recommendations, children, _selectedRouteId);
        var bulletBoard = BuildBulletOperatingBoard(selected);
        RecommendationBoard.Fill(NowPanel, FlowPanel, BoardPanel, _recommendations, _combinePlan,
            _selectedRouteId, SelectRoute, PhaseHintText.Visibility == Visibility.Visible
                ? PhaseHintText.Text
                : null, children, head?.Route.Id, _plannerEvidence, bulletBoard,
            _showRouteRootAsCurrentCraft);
        if (_navigationExplanation is { } explanation)
        {
            var text = new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap,
                Foreground = OverlayTheme.MutedBrush, FontSize = OverlayTheme.PlannerValueTypeSize,
                Margin = OverlayTheme.PlannerBlockMargin };
            AutomationProperties.SetAutomationId(text, "navigation-comparison");
            AutomationProperties.SetName(text, "항법 비교와 항로개척 근거");
            BoardPanel.Children.Insert(0, text);
        }
    }

    private BulletOperatingBoard? BuildBulletOperatingBoard(Recommendation? selected)
    {
        if (_bulletProfile is null) return null;
        var selectedGoalId = selected?.Route.GoalUnitId;
        var committedGoalId = _plannerApplied?.State.RouteLock?.GoalUnitId;
        var isBullet = selectedGoalId == _bulletProfile.GoalUnitId ||
                       committedGoalId == _bulletProfile.GoalUnitId;
        if (!isBullet) return null;
        if (_recognizedInventory is null || _inventoryStats is null)
            return BulletOperatingBoardPolicy.Evaluate(_bulletProfile,
                BulletOperatingBoardInput.Unknown(_plannerRound, selectedGoalId, committedGoalId,
                    "현재 보유 패나 능력 수치를 확인하지 못했습니다."));

        var firstLegendKnown = Count(_bulletProfile.FirstLegendPriorityUnitIds) > 0;
        var flying = Count(_bulletProfile.FlyingCapableLegendUnitIds);
        var bossKill = Count(_bulletProfile.BossKillUnitIds);
        var bulletCrafted = _recognizedInventory.GetValueOrDefault(_bulletProfile.GoalUnitId) > 0;
        return BulletOperatingBoardPolicy.Evaluate(_bulletProfile,
            new BulletOperatingBoardInput(_plannerRound, selectedGoalId, committedGoalId,
                firstLegendKnown, _completedStoryStage, flying, bossKill, _inventoryStats.TotalSlow,
                _inventoryStats.TotalArmorReduction, _inventoryStats.Stun, _stunTarget,
                _greenBloodKnown, bulletCrafted, null,
                "현재 강화 단계를 확인하지 못했습니다."));

        int Count(IEnumerable<string> unitIds) => unitIds.Sum(id =>
            _recognizedInventory.GetValueOrDefault(id));
    }

    private string BulletSignalSignature() => _inventoryStats is null
        ? "bullet-signals:none"
        : string.Join(':', _plannerRound, _inventoryStats.TotalSlow,
            _inventoryStats.TotalArmorReduction, _inventoryStats.Stun,
            _inventoryStats.AirMovementProviders, _inventoryStats.SingleDamageProviders,
            _inventoryStats.FinisherDamageProviders, _greenBloodKnown, _greenBloodUsed,
            _stunTarget, _recognizedInventory is null ? "inventory:none" : string.Join(',',
                _recognizedInventory.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}")));

    private void ApplyPhaseBanner(string? fallback = null)
    {
        var field = _plannerEvidence?[PlannerEvidenceFieldKind.Phase];
        PhaseHintText.Text = field?.DisplayValue ?? fallback ?? "";
        PhaseHintText.Visibility = PhaseHintText.Text.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        AutomationProperties.SetName(PhaseHintText,
            field?.AccessibilityName ?? "안내");
        AutomationProperties.SetItemStatus(PhaseHintText,
            field?.AccessibilityValue ?? PhaseHintText.Text);
    }

    private void SelectRoute(string routeId)
    {
        _selectedRouteId = routeId;
        _onRouteSelected?.Invoke(routeId);
        var head = ClusterHead();
        var currentChildren = head is null
            ? []
            : _storyChildren?.Invoke(head) ?? [];
        if (BoardSelection.Contains(_recommendations, routeId) &&
            !BoardSelection.Contains(currentChildren, routeId))
            _clusterHeadRouteId = BoardSelection.Find(_recommendations, routeId)!.Route.Id;
        ApplySelectionCascade();
        FillBoard();
    }

    private void PreserveBoardSelection()
    {
        var head = ClusterHead();
        var children = head is null
            ? []
            : _storyChildren?.Invoke(head) ?? [];
        if (BoardSelection.IsKnown(_recommendations, children, _selectedRouteId)) return;
        _selectedRouteId = head?.Route.Id;
        _clusterHeadRouteId = head?.Route.Id;
    }

    private Recommendation? ClusterHead()
    {
        if (_recommendations.Count == 0) return null;
        var id = BoardSelection.ClusterHeadId(
            _recommendations, [], _selectedRouteId, _clusterHeadRouteId);
        return BoardSelection.Find(_recommendations, id) ?? _recommendations[0];
    }

    private void ApplySelectionCascade()
    {
        if (_recascade is null || _recommendations.Count == 0) return;
        var head = ClusterHead();
        _recommendations = _recascade(_recommendations, head?.Route.Id ?? _selectedRouteId);
        _clusterHeadRouteId = ClusterHead()?.Route.Id;
    }

    private string? _lastSpecialSignature;

    internal static IReadOnlyList<SpecialDismantleAdvice> DismantleOnly(
        IReadOnlyList<SpecialDismantleAdvice> advice) =>
        advice.Where(item => item.Dismantle).ToList();

    private void RenderSpecialAdvice(IReadOnlyList<SpecialDismantleAdvice> advice)
    {
        var dismantles = DismantleOnly(advice);
        var signature = string.Join("|", dismantles.Select(item =>
            $"{item.UnitId}:{item.Dismantle}:{item.Reason}"));
        if (signature == _lastSpecialSignature) return;
        _lastSpecialSignature = signature;
        SpecialPanel.Children.Clear();
        var visible = dismantles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SpecialHeader.Visibility = visible;
        SpecialPanel.Visibility = visible;
        foreach (var item in dismantles)
            SpecialPanel.Children.Add(AdviceChip(
                $"{item.Name} 분해", OverlayTheme.WarnBrush));
    }

    private string? _lastEmergencySignature;

    private void RenderEmergencySummons(IReadOnlyList<EmergencySummonAdvice> advice)
    {
        var signature = string.Join("|", advice.Select(item =>
            $"{item.UnitId}:{item.Count}:{item.Reason}"));
        if (signature == _lastEmergencySignature) return;
        _lastEmergencySignature = signature;
        EmergencyPanel.Children.Clear();
        var visible = advice.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmergencyHeader.Visibility = visible;
        EmergencyPanel.Visibility = visible;
        foreach (var item in advice)
            EmergencyPanel.Children.Add(AdviceChip(
                item.Count > 1 ? $"{item.Name} ×{item.Count}" : item.Name,
                OverlayTheme.WarnBrush));
    }

    private string? _lastGreenBloodSignature;

    private void RenderGreenBloodAdvice(IReadOnlyList<GreenBloodAdvice> advice, bool owned,
        bool used)
    {
        var signature = owned + "|" + used + "|" + string.Join("|", advice.Select(item =>
            $"{item.UnitId}:{item.Reason}:{item.Warning}"));
        if (signature == _lastGreenBloodSignature) return;
        _lastGreenBloodSignature = signature;
        GreenBloodPanel.Children.Clear();
        if (used)
        {
            GreenBloodHeader.Text = "그린블러드 · 사용됨";
            GreenBloodHeader.Visibility = Visibility.Visible;
            GreenBloodPanel.Visibility = Visibility.Collapsed;
            return;
        }
        GreenBloodHeader.Text = owned ? "그린블러드 · 보유 중" : "그린블러드";
        var visible = advice.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        GreenBloodHeader.Visibility = visible;
        GreenBloodPanel.Visibility = visible;
        foreach (var item in advice)
            GreenBloodPanel.Children.Add(AdviceChip(item.Name,
                item.Seraphim ? OverlayTheme.GoldBrush : OverlayTheme.OkBrush));
    }

    private void RenderRareRerolls(IReadOnlyList<RareRerollAdvice> advice, bool hasPlan)
    {
        var signature = hasPlan + "|" + string.Join("|", advice.Select(item =>
            $"{item.UnitId}:{item.OwnedCount}:{item.NeededCount}:{item.RerollCount}:{item.Sell}"));
        if (signature == _lastRerollSignature) return;
        _lastRerollSignature = signature;
        RareRerollPanel.Children.Clear();

        if (!hasPlan)
        {
            RareRerollPanel.Children.Add(AdviceChip("추천 계산 후 표시", OverlayTheme.MutedBrush));
            return;
        }
        if (advice.Count == 0)
        {
            RareRerollPanel.Children.Add(AdviceChip("버릴 희귀패 없음", OverlayTheme.OkBrush));
            return;
        }

        foreach (var item in advice.Take(8))
            RareRerollPanel.Children.Add(AdviceChip(
                item.Sell ? $"{item.Name} 판매×{item.RerollCount}" : $"{item.Name} 리롤×{item.RerollCount}",
                item.Sell ? OverlayTheme.OkBrush : OverlayTheme.WarnBrush));
        if (advice.Count > 8)
            RareRerollPanel.Children.Add(AdviceChip($"외 {advice.Count - 8}종", OverlayTheme.MutedBrush));
    }

    private static UIElement AdviceChip(string text, Brush color) => new Border
    {
        Background = OverlayTheme.RowBrush,
        BorderBrush = OverlayTheme.HairlineBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(OverlayTheme.ChipRadius),
        Padding = new Thickness(8, 4, 8, 4),
        Margin = new Thickness(0, 0, 6, 6),
        Child = new TextBlock
        {
            Text = text,
            Foreground = color,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        }
    };

    internal readonly record struct CoreKpiTargets(double Stun, double Slow, double Armor, double MagicArmor);

    internal static CoreKpiTargets ResolveCoreKpiTargets(CombatReadiness? readiness,
        GoroseiMode gorosei, double stunTarget)
    {
        if (readiness is { } ready)
            return new CoreKpiTargets(ready.RequiredStun, ready.RequiredSlow,
                ready.RequiredArmorReduction, ready.RequiredMagicArmorReduction);
        return new CoreKpiTargets(
            stunTarget,
            GoroseiEffects.AdjustSlowTarget(GoalStrategyCalculator.FullSlowTarget, gorosei),
            GoroseiEffects.AdjustArmorTarget(GoalStrategyCalculator.FullArmorReductionTarget, gorosei),
            GoroseiEffects.AdjustMagicArmorTarget(GoalStrategyCalculator.MagicArmorSourceTarget, gorosei));
    }

    private void RenderCurrentStats(InventoryStatSummary stats, bool magicGoal, GoroseiMode gorosei,
        double stunTarget, double stunCap, CombatReadiness? readiness = null,
        bool allCombatStats = false, string? diagnosticDirection = null)
    {
        if (Stats.IsDiagnosticReferenceMode && !allCombatStats) return;
        Stats.SetDamageType(magicGoal);
        Stats.SetStatSource(stats);
        var targets = ResolveCoreKpiTargets(readiness, gorosei, stunTarget);
        var signature = stats + "|" + magicGoal + "|" + gorosei + "|" + targets.Stun + "|" + stunCap + "|" +
            targets.Slow + "|" + targets.Armor + "|" + targets.MagicArmor + "|" + allCombatStats + "|" +
            diagnosticDirection;
        if (signature == _lastStatsSignature) return;
        _lastStatsSignature = signature;
        CurrentStatsPanel.Children.Clear(); CoreKpiPanel.Children.Clear();
        Stats.ConditionalStatsPanel.Children.Clear(); Stats.AdditionalStatsPanel.Children.Clear();
        Stats.AdditionalRolesPanel.Children.Clear(); Stats.ClearStatValues();

        var neutralDirection = allCombatStats && diagnosticDirection is not ("physical" or "magical");
        CoreKpiPanel.Columns = neutralDirection ? 2 : 3;
        CoreKpiPanel.Rows = neutralDirection ? 2 : 1;
        var focus = neutralDirection ? new[] { "스턴", "이감", "방깎", "마방깎" }
            : magicGoal ? new[] { "마방깎", "마젠", "폭뎀증" } : new[] { "스턴", "이감", "방깎" };
        // A missing target is not zero. These support effects have no verified threshold.
        var metrics = new (string Label, double Value, double Target, string Detail)[]
        {
            ("스턴", stats.Stun, targets.Stun, "이전 안내 자료의 수치를 더한 참고값이에요."),
            ("이감", stats.TotalSlow, targets.Slow, stats.TriggeredSlow > 0
                ? $"항상 적용되는 값 {FormatNumber(stats.Slow)} · 조건에 따라 발동하는 값 {FormatNumber(stats.TriggeredSlow)}" : $"항상 적용되는 값 {FormatNumber(stats.Slow)}"),
            ("방깎", stats.TotalArmorReduction, targets.Armor, ArmorBreakdown(stats)),
            ("마방깎", stats.MagicArmorReduction, targets.MagicArmor, "이전 자료의 수치예요. 적용 조건은 게임에서 확인해 주세요."),
            ("공증", stats.TotalAttackBoost, double.NaN, "이전 안내 자료의 수치를 더한 참고값이에요."),
            ("공속", stats.AttackSpeed, double.NaN, "이전 안내 자료의 수치를 더한 참고값이에요."),
            ("체젠", stats.HealthRegen, double.NaN, "이전 안내 자료의 수치를 더한 참고값이에요."),
            ("마젠", stats.ManaRegen, double.NaN, "이전 안내 자료의 수치를 더한 참고값이에요."),
            ("폭뎀증", stats.ExplosionAmp, double.NaN, "이전 안내 자료의 수치를 더한 참고값이에요."),
            ("마뎀증", stats.MagicAmp, double.NaN, "이전 안내 자료의 수치를 더한 참고값이에요.")
        };
        for (var index = 0; index < focus.Length; index++)
        {
            var item = metrics.Single(metric => metric.Label == focus[index]);
            CoreKpiPanel.Children.Add(new StatsMetricView(item.Label, item.Value, item.Target, item.Detail)
            {
                BorderThickness = new Thickness(0, 0,
                    (index + 1) % CoreKpiPanel.Columns == 0 ? 0 : 1,
                    neutralDirection && index < 2 ? 1 : 0)
            });
        }

        CurrentStatsPanel.Children.Add(StatsSection("전체 지원"));
        var support = new UniformGrid { Columns = 3 };
        AutomationProperties.SetAutomationId(support, "stats-support-grid");
        foreach (var item in metrics.Where(metric => !focus.Contains(metric.Label)))
            support.Children.Add(StatChip(item.Label, FormatNumber(item.Value), true));
        CurrentStatsPanel.Children.Add(support);
        CurrentStatsPanel.Children.Add(StatsSection("보유 역할 · 유닛 수"));
        var roles = new UniformGrid { Columns = 4 };
        AutomationProperties.SetAutomationId(roles, "stats-role-details");
        roles.Children.Add(StatsRoleChip("단일", stats.SingleDamageProviders));
        roles.Children.Add(StatsRoleChip("끝딜", stats.FinisherDamageProviders));
        roles.Children.Add(StatsRoleChip("암브", stats.ArmorBreakProviders));
        roles.Children.Add(StatsRoleChip("보잡", stats.BossControlProviders));
        if (stats.BerserkControlProviders > 0) Stats.AdditionalRolesPanel.Children.Add(StatsRoleChip("광보잡", stats.BerserkControlProviders));
        if (stats.AirMovementProviders > 0) Stats.AdditionalRolesPanel.Children.Add(StatsRoleChip("공중이동", stats.AirMovementProviders));
        if (stats.TeleportProviders > 0) Stats.AdditionalRolesPanel.Children.Add(StatsRoleChip("순간이동", stats.TeleportProviders));
        if (stats.BurgessProviders > 0) Stats.AdditionalRolesPanel.Children.Add(StatsRoleChip("바제스", stats.BurgessProviders));
        CurrentStatsPanel.Children.Add(roles);

        if (stats.SourceUnitCount > 0)
        {
            Stats.AdditionalStatsPanel.Children.Add(StatChip("모든피해증", FormatNumber(stats.AllDamageAmp), stats.AllDamageAmp > 0));
            Stats.AdditionalStatsPanel.Children.Add(StatChip("단일마증", FormatNumber(stats.SingleMagicAmp), stats.SingleMagicAmp > 0));
            if (magicGoal)
            {
                Stats.AdditionalStatsPanel.Children.Add(StatChip("단일 지표", FormatNumber(stats.SingleDamageWeight), stats.SingleDamageWeight > 0));
                Stats.AdditionalStatsPanel.Children.Add(StatChip("끝딜 지표", FormatNumber(stats.FinisherDamageWeight), stats.FinisherDamageWeight > 0));
            }
        }
        if (GoroseiEffects.StatsNote(gorosei) is { } goroseiNote)
            Stats.ConditionalStatsPanel.Children.Add(new TextBlock { Text = goroseiNote, TextWrapping = TextWrapping.Wrap,
                Foreground = RandyPickTheme.Warning, FontSize = 10, Margin = new Thickness(0, 0, 0, 5) });
        Stats.ConditionalStatsPanel.Children.Add(new TextBlock
        {
            Text = stats.UnknownValueUnitCount > 0
                ? "확인되지 않은 효과는 더하지 않았어요. 실제 적용 여부는 게임에서 확인해 주세요."
                : "조건에 따라 발동하는 값은 게임에서 실제로 적용된 수치가 아니에요.\n" +
                  $"이동 속도 감소: 항상 {FormatNumber(stats.Slow)} · 조건부 {FormatNumber(stats.TriggeredSlow)}\n" +
                  "방어력 감소: " + ArmorBreakdown(stats) + "\n" +
                  $"공격력 증가: 항상 {FormatNumber(stats.AttackBoost)} · 조건부 {FormatNumber(stats.TriggeredAttackBoost)}\n" +
                  "한 대상에게만 적용되거나 실제 중첩을 확인하지 못한 값은 주요 합계에서 뺐어요.",
            TextWrapping = TextWrapping.Wrap, FontSize = 10, LineHeight = 16, Foreground = RandyPickTheme.Muted
        });
        Stats.ApplyMetricSafety();
    }

    private UIElement StatChip(string name, string value, bool accent)
    {
        var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new TextBlock { Text = StatsDisplayName(name), FontSize = 10, Foreground = RandyPickTheme.Secondary, VerticalAlignment = VerticalAlignment.Center });
        var number = new TextBlock { Text = "?", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(number, 1); content.Children.Add(number);
        var row = new Border { Child = content, BorderBrush = RandyPickTheme.Border, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(1, 6, 1, 6), Margin = new Thickness(3, 0, 3, 0) };
        AutomationProperties.SetAutomationId(row, "stats-value:" + name); AutomationProperties.SetName(row, StatsDisplayName(name));
        Stats.RegisterStatValue(row, number, value is "NaN" or "Infinity" or "-Infinity" ? "?" : value);
        return row;
    }

    private UIElement StatsRoleChip(string name, int count)
    {
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        body.Children.Add(new TextBlock { Text = StatsDisplayName(name), FontSize = 10, Foreground = RandyPickTheme.Secondary, HorizontalAlignment = HorizontalAlignment.Center });
        var value = new TextBlock { Text = "?", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        body.Children.Add(value);
        var chip = new Border { Child = body, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 2, 0),
            ToolTip = "역할 숫자는 해당 역할을 제공하는 유닛 수예요. 피해량이 아니며 이전 자료의 수치 합계에 더하지 않아요." };
        AutomationProperties.SetAutomationId(chip, "stats-value:" + name); AutomationProperties.SetName(chip, StatsDisplayName(name));
        Stats.RegisterStatValue(chip, value, count >= 0 ? $"{count}기" : "?");
        return chip;
    }

    private static string StatsDisplayName(string name) => name switch
    {
        "이감" => "이동 속도 감소", "방깎" => "방어력 감소", "마방깎" => "마법 방어력 감소",
        "공증" => "공격력 증가", "공속" => "공격 속도", "체젠" => "체력 재생",
        "마젠" => "마나 재생", "폭뎀증" => "폭발 피해 증가", "마뎀증" => "마법 피해 증가",
        "암브" => "방어력 약화", "보잡" => "보스 제어", "광보잡" => "광폭 보스 제어",
        "모든피해증" => "전체 피해 증가", "단일마증" => "단일 마법 피해 증가",
        _ => name
    };

    private static TextBlock StatsSection(string text) => new()
    {
        Text = text, Foreground = RandyPickTheme.Muted, FontSize = 10,
        Margin = new Thickness(2, 8, 0, 4)
    };

    private static string ArmorBreakdown(InventoryStatSummary stats)
    {
        var values = new List<string> { $"고정 {FormatNumber(stats.ArmorReduction)}" };
        if (stats.TriggeredArmorReduction > 0)
            values.Add($"발동 {FormatNumber(stats.TriggeredArmorReduction)}");
        var legacyStacking = stats.StackingArmorReduction - stats.UnobservedStackingArmorReduction;
        if (legacyStacking > 0)
            values.Add($"중첩 {FormatNumber(legacyStacking)}");
        if (stats.UnobservedStackingArmorReduction > 0)
            values.Add($"중첩 최대 {FormatNumber(stats.UnobservedStackingArmorReduction)} (실제 중첩 미확인 · 합계 제외)");
        if (stats.SingleArmorReduction > 0)
            values.Add($"단일 {FormatNumber(stats.SingleArmorReduction)}");
        return string.Join(" · ", values);
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string RecommendationSignature(IReadOnlyList<Recommendation> recommendations) =>
        string.Join("|", recommendations.Select(item => string.Join("~",
            item.Route.Id,
            item.Score.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            item.RecipeProgress.OwnedLeafCount,
            item.RecipeProgress.RequiredLeafCount,
            string.Join(",", item.RecipeProgress.Leaves.Select(leaf =>
                $"{leaf.UnitId}:{leaf.OwnedCount}:{leaf.RequiredCount}")),
            string.Join(",", item.RemainingCraftSteps.Select(step =>
                $"{step.UnitId}:{step.OwnedCount}:{step.RequiredCount}")),
            string.Join(",", item.CompositionUnits.Select(unit => $"{unit.UnitId}:{unit.OwnedCount}")),
            string.Join(",", item.Warnings),
            string.Join(",", item.CombineCommands),
            item.ClearEvidence is null
                ? ""
                : $"{item.ClearEvidence.SampleCount}:{item.ClearEvidence.SharePercent}")));
}
