using System.Windows;
namespace OrandOverlay;

/// <summary>
/// 내 패 상태(수치·정리 안내) 전용 오버레이.
/// 추천 창과 분리해 각자 원하는 위치에 놓을 수 있게 한다.
/// 패널 채우기는 추천 창의 렌더링 코드가 이 창의 패널을 그대로 쓴다.
/// </summary>
public partial class StatsOverlayWindow : OverlayWindowBase
{
    public StatsOverlayWindow()
    {
        InitializeComponent();
        NonActivatingWindowBehavior.Attach(this);
        Loaded += (_, _) =>
        {
            OverlayTheme.AttachRoundClip(StatsChrome, 12);
            OverlayTheme.AttachRoundClip(MetricsFrame, 8);
        };
    }

    private OverlayDisplayMode _mode = OverlayDisplayMode.Full;

    protected override double DesignWidth =>
        OverlayLayoutPolicy.StatsLayout(_mode).Width;
    protected override double DesignHeight =>
        OverlayLayoutPolicy.StatsLayout(_mode).Height;
    protected override UIElement? ClickThroughIndicator => ClickThroughBadge;

    public void SetDisplayMode(OverlayDisplayMode mode)
    {
        _mode = mode;
        NonCoreSectionsPanel.Visibility = Visibility.Visible;
        ApplyResolutionScale();
    }

    public bool HasCurrentObservation { get; private set; }
    public bool TargetsKnown { get; private set; }
    private bool _valuesKnown;
    private readonly List<(FrameworkElement Row, System.Windows.Controls.TextBlock Value, string Actual)> _statValues = [];
    private bool _referenceOnly;
    private bool _frameTargetsKnown;
    public const string SourceDisclaimer = "이 수치는 이전 안내 자료를 참고한 값이에요. 게임에서 실제 적용되는 수치와 다를 수 있어요.\n자료에 없는 유닛은 기존 능력치 목록을 참고했어요. 역할별 유닛 수는 별도 자료를 참고하며 수치 합계에 더하지 않아요.";
    private bool _ready;
    private bool _magicGoal;
    public void SetDamageType(bool magicGoal) => _magicGoal = magicGoal;
    private string? _readiness;

    public void SetStatSource(InventoryStatSummary stats)
    {
        if (_diagnosticReferenceMode)
        {
            _diagnosticCombatStats = stats;
            RenderDiagnosticReference();
            return;
        }
        _valuesKnown = stats.UnknownValueUnitCount == 0;
        _referenceOnly = stats.IsLegacyReferenceForSelectedMap;
        SourceEvidenceText.Text = !_valuesKnown ? "참고 부분합" : "참고 합계";
        SourceDetailsText.Text = SourceDisclaimer +
            $"\n수치가 불확실한 유닛 {stats.UnknownValueUnitCount}기 · 기존 능력치 목록을 참고한 유닛 {stats.UnlistedUnitCount}기\n" +
            "수치를 확인할 수 없는 항목은 0으로 계산하고, 미확인 효과는 합계에서 뺐어요.\n" +
            (stats.IsLegacyReferenceForSelectedMap ? "선택한 게임 버전의 실제 적용량은 확인되지 않았어요. 목표 충족 여부도 판단하지 않아요.\n" : "") +
            "조건에 따라 발동하거나 한 대상에만 적용되는 효과, 겹치는 효과는 게임에서 다를 수 있어요.";
        SourceEvidenceText.ToolTip = SourceDetailsText.Text;
        System.Windows.Automation.AutomationProperties.SetItemStatus(SourceEvidenceText,
            stats.IsLegacyReferenceForSelectedMap ? "이전 자료 참고값이에요. 실제 적용량은 게임에서 확인해 주세요." :
            !_valuesKnown ? "확인된 값만 더했어요. 빠진 효과는 게임에서 확인해 주세요." :
            "안내 자료를 참고한 합계예요. 실제 적용량은 게임에서 확인해 주세요.");
        StatsFooterText.Text = !_valuesKnown ? "미확인 효과는 합계 제외 · 목표 판단 보류"
            : stats.IsLegacyReferenceForSelectedMap ? "이전 자료 참고 · 목표 판단 보류" : "표기 합계 · 실제 적용량과 다를 수 있음";
        ApplyMetricSafety();
    }

    public void ClearStatValues() => _statValues.Clear();
    public void RegisterStatValue(FrameworkElement row, System.Windows.Controls.TextBlock value, string actual) =>
        _statValues.Add((row, value, actual));

    public void SetObservation(CoachDecision decision, CoachFrame frame)
    {
        if (_diagnosticReferenceMode) { RenderDiagnosticReference(); return; }
        HasCurrentObservation = frame.IsCurrent && decision.Id != "start" && decision.Kind != CoachActionKind.Finished;
        _frameTargetsKnown = frame.HasKnownDifficulty && frame.GoalId is not null;
        TargetsKnown = HasCurrentObservation && _frameTargetsKnown && _valuesKnown && !_referenceOnly;
        StatsScroll.Visibility = HasCurrentObservation ? Visibility.Visible : Visibility.Collapsed;
        SourceEvidenceText.Visibility = HasCurrentObservation ? Visibility.Visible : Visibility.Collapsed;
        UnknownStateText.Visibility = HasCurrentObservation ? Visibility.Collapsed : Visibility.Visible;
        UnknownStateText.Text = decision.Id == "start" ? "현재 패를 기다리고 있어요.\n패를 확인하기 전에는 합계나 목표 충족 여부를 판단하지 않아요."
            : "현재 패를 다시 확인해 주세요.\n이전 수치와 목표 충족 여부는 숨겼어요.";
        ObservationText.Text = HasCurrentObservation
            ? decision.CraftProgress?.AwaitingRecognition == true ? "패 확인 · 조합 결과 대기"
                : !_valuesKnown ? "확인된 수치만 더했어요 · 목표 판단 보류"
                : _referenceOnly ? "이전 자료 참고 · 목표 판단 보류"
                : !TargetsKnown ? "목표 미확인 · 패 확인" : _magicGoal ? "마법 · 현재 패 확인" : "물리 · 현재 패 확인"
            : decision.Id == "start" ? "게임 연결 대기" : "현재 패 미확인";
        System.Windows.Automation.AutomationProperties.SetItemStatus(ObservationText,
            HasCurrentObservation ? "방금 확인한 패예요." : "현재 패를 확인해 주세요.");
        ApplyMetricSafety();
    }

    public void ApplyMetricSafety()
    {
        if (_diagnosticReferenceMode) { RenderDiagnosticReference(); return; }
        TargetsKnown = HasCurrentObservation && _frameTargetsKnown && _valuesKnown && !_referenceOnly;
        CurrentStatsPanel.Visibility = HasCurrentObservation ? Visibility.Visible : Visibility.Collapsed;
        ApplyMetricValues(HasCurrentObservation, TargetsKnown);
        ApplyReadiness();
    }

    private void ApplyMetricValues(bool current, bool targetsKnown)
    {
        foreach (var (row, value, actual) in _statValues)
        {
            var known = current && actual != "?";
            value.Text = known ? actual : "?";
            value.Foreground = known ? RandyPickTheme.Text : RandyPickTheme.Muted;
            System.Windows.Automation.AutomationProperties.SetHelpText(value, !known ? "수치 미확인"
                : !_valuesKnown ? "미확인 효과를 제외한 부분합" : _referenceOnly ? "이전 자료 참고값" : "표기 합계");
            System.Windows.Automation.AutomationProperties.SetItemStatus(row, known ? actual : "수치를 확인해 주세요.");
        }
        foreach (var metric in CoreKpiPanel.Children.OfType<StatsMetricView>())
        {
            metric.SetObservationKnown(current, partialValue: !_valuesKnown, referenceValue: _referenceOnly);
            metric.SetTargetsKnown(targetsKnown);
        }
    }

    private void ApplyReadiness()
    {
        if (_diagnosticReferenceMode) { RenderDiagnosticReference(); return; }
        ReadinessSummaryText.Text = _ready ? "지원 목표 충족" : "지원 목표 보완";
        ReadinessSummaryText.ToolTip = _readiness is null ? null : "현재 패의 지원 목표 안내예요. 실제 적용 여부는 게임에서 확인해 주세요.";
        ReadinessSummaryText.Foreground = _ready ? OverlayTheme.PlanSuccess : OverlayTheme.PlanWarning;
        ReadinessSummaryText.Visibility = TargetsKnown && !string.IsNullOrWhiteSpace(_readiness)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetReadiness(string? text, bool ready)
    {
        _readiness = text;
        _ready = ready;
        ApplyReadiness();
    }
}
