using System.Windows;

namespace OrandOverlay;

// No combat-number conversion: 48129 roles are NOT a stats dataset.
public sealed record DiagnosticReferenceStats(int? ObservedCount, string AliveCount = "?", string LocalCount = "?",
    string Stun = "?", string Slow = "?", string Armor = "?", string Damage = "?")
{
    public int? ObservedRound { get; init; }
    public static DiagnosticReferenceStats From(IDiagnosticInventoryReference? value, bool current) =>
        new(current && value is (DiagnosticInventoryObservation or DiagnosticBasicInventoryObservation) && value.Availability == DiagnosticInventoryAvailability.Ready ? value.Entries.Sum(e => e.Count) : null)
        { ObservedRound = current && value is (DiagnosticInventoryObservation or DiagnosticBasicInventoryObservation) && value.Availability == DiagnosticInventoryAvailability.Ready ? value.ObservedRound : null };
}

public partial class StatsOverlayWindow
{
    internal string DiagnosticReferenceDisplayText { get { Dispatcher.VerifyAccess(); return _diagnosticReferenceMode ? UnknownStateText.Text : ""; } }
    private readonly DiagnosticRoundPresentation _diagnosticRoundPresentation = new();
    private bool _diagnosticReferenceMode;
    internal bool IsDiagnosticReferenceMode => _diagnosticReferenceMode;
    private IDiagnosticInventoryReference? _diagnosticReferenceValue;
    private Func<bool>? _diagnosticReferenceCurrent;
    private string _diagnosticReferenceText = "";
    private InventoryStatSummary? _diagnosticCombatStats;
    private string _diagnosticDirection = "unknown";
    private string? _diagnosticAnchorName;
    internal void SetDiagnosticDirection(string direction, string? anchorName)
    {
        _diagnosticDirection = direction is "physical" or "magical" or "both" ? direction : "unknown";
        _diagnosticAnchorName = anchorName;
    }
    public void SetDiagnosticReference(IDiagnosticInventoryReference? observation, Func<bool> current, InventoryStatSummary? stats = null)
    {
        Dispatcher.VerifyAccess();
        _diagnosticReferenceMode = true;
        _diagnosticReferenceValue = observation is DiagnosticInventoryObservation or DiagnosticBasicInventoryObservation ? observation : null;
        _diagnosticReferenceCurrent = current;
        if (stats is not null) _diagnosticCombatStats = stats;
        else if (_diagnosticReferenceValue is null) _diagnosticCombatStats = null;
        RenderDiagnosticReference();
    }
    public void ClearDiagnosticReference()
    {
        Dispatcher.VerifyAccess();
        if (!_diagnosticReferenceMode) return;
        _diagnosticReferenceMode = false; _diagnosticReferenceValue = null; _diagnosticReferenceCurrent = null;
        _diagnosticReferenceText = "";
        _diagnosticRoundPresentation.Update(null, false);
        _diagnosticCombatStats = null;
        _valuesKnown = false;
        ApplyMetricValues(false, false);
        HasCurrentObservation = false; TargetsKnown = false;
        RareRerollPanel.Visibility = Visibility.Visible;
        StatsScroll.Visibility = Visibility.Collapsed; SourceEvidenceText.Visibility = Visibility.Collapsed;
        UnknownStateText.Visibility = Visibility.Visible; UnknownStateText.Text = "현재 패 확인 대기";
        ObservationText.Text = "현재 패 확인 대기";
        System.Windows.Automation.AutomationProperties.SetItemStatus(ObservationText, "현재 패를 확인해 주세요.");
        // Do not replay a previous regular snapshot. Legacy must supply its next observation.
    }
    private void RenderDiagnosticReference()
    {
        Dispatcher.VerifyAccess();
        var current = _diagnosticReferenceCurrent?.Invoke() == true;
        var ready = _diagnosticReferenceValue is (DiagnosticInventoryObservation or DiagnosticBasicInventoryObservation) &&
            _diagnosticReferenceValue.Availability == DiagnosticInventoryAvailability.Ready;
        var show = ready && _diagnosticCombatStats is not null;
        var observedCount = ready ? _diagnosticReferenceValue!.Entries.Sum(e => e.Count) : (int?)null;
        HasCurrentObservation = false; TargetsKnown = false;
        _valuesKnown = _diagnosticCombatStats is { UnknownValueUnitCount: 0 };
        _referenceOnly = true; _frameTargetsKnown = false;
        ApplyMetricValues(show, false);
        EmergencyPanel.Visibility = EmergencyHeader.Visibility = RareRerollPanel.Visibility = Visibility.Collapsed;
        SpecialPanel.Visibility = SpecialHeader.Visibility = GreenBloodPanel.Visibility = GreenBloodHeader.Visibility = Visibility.Collapsed;
        StatsScroll.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CurrentStatsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ReadinessSummaryText.Visibility = Visibility.Collapsed;
        UnknownStateText.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        var text = _diagnosticRoundPresentation.Update(_diagnosticReferenceValue, current) +
            "\n" + (observedCount is { } count ? $"읽은 유닛 {count}개" : "유닛 확인 중") +
            "\n\n" + (current ? "방금 읽은 패" : "마지막으로 읽은 패") + "의 이전 자료 참고 수치예요. 실제 보유나 게임 적용량과 다를 수 있어요.";
        var signature = text + "|" + _diagnosticCombatStats + "|" + _diagnosticDirection + "|" + _diagnosticAnchorName + "|" + current + "|" + show;
        if (_diagnosticReferenceText == signature) return;
        _diagnosticReferenceText = signature;
        var directionLabel = _diagnosticDirection switch
        {
            "physical" => "물리 주력",
            "magical" => "마법 주력",
            "both" => "물리·마법 복합 주력",
            _ => "주력 미확정"
        };
        ObservationText.Text = show ? (current ? directionLabel : "마지막 인식 기준 · " + directionLabel) : "유닛 확인 중";
        ObservationText.ToolTip = string.IsNullOrWhiteSpace(_diagnosticAnchorName) ? directionLabel : _diagnosticAnchorName + " · " + directionLabel;
        SourceEvidenceText.Text = _valuesKnown ? "참고 합계" : "참고 부분합";
        SourceEvidenceText.Visibility = Visibility.Visible;
        SourceDetailsText.Text = SourceDisclaimer +
            $"\n수치가 불확실한 유닛 {_diagnosticCombatStats?.UnknownValueUnitCount}기 · 기존 능력치 목록을 참고한 유닛 {_diagnosticCombatStats?.UnlistedUnitCount}기\n" +
            "수치를 확인할 수 없는 항목은 0으로 계산하고, 미확인 효과는 합계에서 뺐어요.\n" +
            "읽은 유닛이 실제 보유 유닛과 다를 수 있어요. 선택한 게임 버전의 적용량은 확인되지 않았어요. 강화·장비·조건에 따라 달라질 수 있어요.\n" +
            "단일·끝딜·방어력 감소·보스 제어 역할의 숫자는 피해량이 아닌 해당 역할의 유닛 수예요. 목표 충족 여부는 판단하지 않아요.";
        SourceEvidenceText.ToolTip = SourceDetailsText.Text;
        System.Windows.Automation.AutomationProperties.SetItemStatus(SourceEvidenceText,
            "이전 자료를 참고한 값이에요. 실제 적용량은 게임에서 확인해 주세요.");
        StatsFooterText.Text = _valuesKnown ? "이전 자료 참고 · 목표 판단 보류" : "미확인 효과는 합계 제외 · 목표 판단 보류";
        UnknownStateText.Text = text;
        System.Windows.Automation.AutomationProperties.SetItemStatus(ObservationText,
            "이전 자료를 참고한 값이에요. 실제 보유와 적용량은 게임에서 확인해 주세요.");
    }
}
