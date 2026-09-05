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
    }

    public OverlayWindow()
    {
        InitializeComponent();
        try
        {
            _bulletProfile = BulletStrategyProfileLoader.LoadFromDirectory(
                Path.Combine(AppContext.BaseDirectory, "Data"));
        }
        catch (InvalidDataException)
        {
            _bulletProfile = null;
        }
        var appVersion = UpdateService.CurrentVersion;
        OverlayVersionText.Text = $"v{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}";
        Loaded += (_, _) =>
        {
            OverlayTheme.AttachRoundClip(NowWell, OverlayTheme.WellRadius);
            OverlayTheme.AttachRoundClip(FlowWell, OverlayTheme.WellRadius);
            OverlayTheme.AttachRoundClip(BoardWell, OverlayTheme.WellRadius);
        };
    }

    protected override double DesignWidth => 540;
    protected override double DesignHeight => 740;
    protected override UIElement? ClickThroughIndicator => ClickThroughBadge;

    public StatsOverlayWindow Stats { get; } = new();

    public override void SetClickThrough(bool enabled)
    {
        base.SetClickThrough(enabled);
        Stats.SetClickThrough(enabled);
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
        RenderCurrentStats(stats, magicGoal, gorosei, stunTarget, stunCap);
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
                    "현재 인벤토리 구성 또는 능력 수치 신호가 없습니다."));

        var firstLegendKnown = Count(_bulletProfile.FirstLegendPriorityUnitIds) > 0;
        var flying = Count(_bulletProfile.FlyingCapableLegendUnitIds);
        var bossKill = Count(_bulletProfile.BossKillUnitIds);
        var bulletCrafted = _recognizedInventory.GetValueOrDefault(_bulletProfile.GoalUnitId) > 0;
        return BulletOperatingBoardPolicy.Evaluate(_bulletProfile,
            new BulletOperatingBoardInput(_plannerRound, selectedGoalId, committedGoalId,
                firstLegendKnown, _completedStoryStage, flying, bossKill, _inventoryStats.TotalSlow,
                _inventoryStats.TotalArmorReduction, _inventoryStats.Stun, _stunTarget,
                _greenBloodKnown, bulletCrafted, null,
                "현재 인식 입력에 강화 단계 신호가 없습니다."));

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

    private void RenderCurrentStats(InventoryStatSummary stats, bool magicGoal, GoroseiMode gorosei,
        double stunTarget, double stunCap)
    {
        var signature = stats + "|" + magicGoal + "|" + gorosei + "|" + stunTarget + "|" + stunCap;
        if (signature == _lastStatsSignature) return;
        _lastStatsSignature = signature;
        CurrentStatsPanel.Children.Clear();
        CoreKpiPanel.Children.Clear();

        var slowTarget = GoroseiEffects.AdjustSlowTarget(102, gorosei);
        var armorTarget = GoroseiEffects.AdjustArmorTarget(211, gorosei);
        var magicArmorTarget = GoroseiEffects.AdjustMagicArmorTarget(1, gorosei);
        if (GoroseiEffects.StatsNote(gorosei) is { } goroseiNote)
            CurrentStatsPanel.Children.Add(AdviceChip(goroseiNote, OverlayTheme.WarnBrush));

        CoreKpiPanel.Children.Add(OverlayTheme.Kpi("스턴", stats.Stun, stunTarget, ""));
        CoreKpiPanel.Children.Add(OverlayTheme.Kpi("이감", stats.TotalSlow, slowTarget,
            stats.TriggeredSlow > 0
                ? $"고정 {FormatNumber(stats.Slow)} · 발동 {FormatNumber(stats.TriggeredSlow)}"
                : $"고정 {FormatNumber(stats.Slow)}"));
        CoreKpiPanel.Children.Add(magicGoal
            ? OverlayTheme.Kpi("마방깎", stats.MagicArmorReduction, magicArmorTarget,
                gorosei == GoroseiMode.Warcury ? "워큐리 보정: 마방깎 10" : "마방깎 1기 이상")
            : OverlayTheme.Kpi("방깎", stats.TotalArmorReduction, armorTarget,
                ArmorBreakdown(stats)));

        if (magicGoal)
        {
            CurrentStatsPanel.Children.Add(StatChip("단일", $"{stats.SingleDamageProviders}기",
                stats.SingleDamageProviders > 0));
            CurrentStatsPanel.Children.Add(StatChip("끝딜", $"{stats.FinisherDamageProviders}기",
                stats.FinisherDamageProviders > 0));
        }

        CurrentStatsPanel.Children.Add(StatChip("공증", FormatNumber(stats.TotalAttackBoost), true));
        CurrentStatsPanel.Children.Add(StatChip("공속", FormatNumber(stats.AttackSpeed), true));
        if (magicGoal) CurrentStatsPanel.Children.Add(StatChip("마뎀증", FormatNumber(stats.MagicAmp), true));
        CurrentStatsPanel.Children.Add(StatChip("체젠", FormatNumber(stats.HealthRegen), true));
        CurrentStatsPanel.Children.Add(StatChip("마젠", FormatNumber(stats.ManaRegen), true));
        if (!magicGoal)
            CurrentStatsPanel.Children.Add(StatChip("암브", $"{stats.ArmorBreakProviders}기",
                stats.ArmorBreakProviders > 0));
        CurrentStatsPanel.Children.Add(StatChip("보잡", $"{stats.BossControlProviders}기",
            stats.BossControlProviders > 0));
        CurrentStatsPanel.Children.Add(StatChip("광보잡", $"{stats.BerserkControlProviders}기",
            stats.BerserkControlProviders > 0));
        CurrentStatsPanel.Children.Add(StatChip("공중이동", $"{stats.AirMovementProviders}기",
            stats.AirMovementProviders > 0));
        CurrentStatsPanel.Children.Add(StatChip("순간이동", $"{stats.TeleportProviders}기",
            stats.TeleportProviders > 0));
        CurrentStatsPanel.Children.Add(StatChip("바제스", $"{stats.BurgessProviders}기",
            stats.BurgessProviders > 0));
    }

    private static UIElement StatChip(string name, string value, bool accent)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = OverlayTheme.MutedBrush,
            FontSize = 12
        });
        var number = new TextBlock
        {
            Text = value,
            Foreground = accent ? OverlayTheme.GoldBrush : OverlayTheme.MutedBrush,
            FontSize = 13,
            FontWeight = FontWeights.Bold
        };
        Grid.SetColumn(number, 1);
        row.Children.Add(number);
        return row;
    }

    private static string ArmorBreakdown(InventoryStatSummary stats)
    {
        var values = new List<string> { $"고정 {FormatNumber(stats.ArmorReduction)}" };
        if (stats.TriggeredArmorReduction > 0)
            values.Add($"발동 {FormatNumber(stats.TriggeredArmorReduction)}");
        if (stats.StackingArmorReduction > 0)
            values.Add($"중첩 {FormatNumber(stats.StackingArmorReduction)}");
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
