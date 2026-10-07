using System.Windows.Media;

namespace OrandOverlay;

public partial class MainWindow
{
    private readonly DiagnosticRoundPresentation _diagnosticRoundPresentation = new();
    private string _diagnosticStatusKey = "";
    private void RenderDiagnosticInventoryReference()
    {
        Dispatcher.VerifyAccess();
        if (!UsesMap2320 || _diagnosticClosed) return;
        var activityRenderingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        var previous = _diagnosticInventory;
        _diagnosticInventory = DiagnosticPresentationFencesHold() ? _diagnosticReferences.Current(DateTimeOffset.UtcNow,
            _catalog.MapVersion, _catalog.SelectedDatasetFingerprint) : null;
        var roundDisplay = _diagnosticRoundPresentation.Update(_diagnosticInventory, _diagnosticInventory is not null);
        if (_diagnosticInventory is { } value)
        {
            _diagnosticStatus = $"{roundDisplay} · 유닛 {value.Entries.Sum(item => item.Count)}개 읽음 · 실제 보유 여부는 게임에서 확인해 주세요";
            _diagnosticContext = value.BindingContextId.Length > 0 ? value.BindingContextId : value.ContextId;
            _diagnosticView = value.ViewSlot ?? -1;
            _diagnosticRevision = value.SourceRevision;
        }
        else
        {
            if (previous is not null) _diagnosticStatus = DiagnosticPresentationLossStatus(previous);
            _diagnosticExpiry?.Stop();
        }
        _diagnosticCandidates ??= CreateDiagnosticCandidateBrowser();
        var candidateKey = DiagnosticReferenceContentKey(_diagnosticInventory);
        if (_diagnosticInventory is { } reference) _diagnosticCandidates.UpdateReference(reference, _diagnosticMatchGeneration);
        else if (candidateKey != _diagnosticCandidateKey) _diagnosticCandidates.InvalidateReference(_coachPaused);
        _diagnosticCandidateKey = candidateKey;
        _overlay.SetDiagnosticStats(_diagnosticInventory, IsDiagnosticInventoryPresentable, _catalog, _diagnosticCandidates.FirstUpperDirection,
            _diagnosticCandidates.FirstUpperId is { } firstUpperId ? _catalog.Unit(firstUpperId).Name : null,
            retainLastKnown: DiagnosticPresentationFencesHold());
        var renderKey = string.Join("|", candidateKey, CurrentPlayMode,
            _diagnosticCandidates.SessionRevision, _diagnosticCandidates.Stage, _diagnosticCandidates.ProgressStage, _diagnosticCandidates.FollowingProgress,
            _diagnosticCandidates.FirstUpperId, _diagnosticCandidates.FirstUpperDirection,
            _diagnosticCandidates.SelectedUnitId, _diagnosticCandidates.UserDirection,
            string.Join(",", _diagnosticCandidates.CollapsedCategories.OrderBy(x => x, StringComparer.Ordinal)));
        if (_diagnosticRenderKey != renderKey)
        {
            _diagnosticRenderKey = renderKey;
            NormalBrowserView.SetModel(_diagnosticCandidates);
            _overlay.NormalView.SetModel(_diagnosticCandidates);
            RenderDiagnosticInventoryList();
        }
        var statusKey = string.Join("|", _diagnosticStatus, CurrentPlayMode, _diagnosticInventory is null);
        if (_diagnosticStatusKey != statusKey || MainObservationStatus.Text != _diagnosticStatus ||
            RecognitionStatus.Text != _diagnosticStatus)
        {
            _diagnosticStatusKey = statusKey;
            MainObservationStatus.Text = _diagnosticStatus;
            FooterStatus.Text = _diagnosticInventory is null ? "유닛 정보를 다시 확인하고 있어요." : "읽은 유닛으로 조합을 안내해요. 실제 보유와 조합 가능 여부는 게임에서 확인해 주세요.";
            RecognitionStatus.Text = _diagnosticStatus;
            RecognitionStatus.Foreground = RandyPickTheme.Warning;
            RecognitionStatus.ToolTip = "최근 3초 안에 읽은 유닛을 참고해요. 실제 보유와 조합 가능 여부는 게임에서 확인해 주세요.";
            System.Windows.Automation.AutomationProperties.SetItemStatus(RecognitionStatus,
                _diagnosticInventory is null ? "유닛을 다시 확인하고 있어요" : "읽은 유닛 · 실제 보유 여부는 확인되지 않았어요");
            _overlay.SetNormalBrowserActive(CurrentPlayMode == PlayMode.Normal, CurrentPlayMode, _diagnosticStatus);
        }
        CaptureObservedPresentation(previous);
        ApplyDiagnosticOverlayVisibility();
        CaptureActivityPresentation(activityRenderingStarted);
    }

    private string DiagnosticReferenceContentKey(IDiagnosticInventoryReference? value) => value is null ? "unavailable:" + _coachPaused :
        string.Join("|", value.BindingContextId.Length > 0 ? value.BindingContextId : value.ContextId,
            value.ViewSlot, string.Join(",", value.Entries.OrderBy(item => item.UnitId, StringComparer.Ordinal)
                .Select(item => item.UnitId + ":" + item.Count)));

    // Presentation only: never merge these unverified observations into _automatic or coach inputs.
    private void RenderDiagnosticInventoryList()
    {
        var value = IsDiagnosticInventoryPresentable() ? _diagnosticInventory : null;
        var lastKnown = _diagnosticCandidates?.LastKnownInventory;
        var rows = value is { Entries.Length: > 0 }
            ? value.Entries.Select(item => $"{_catalog.Unit(item.UnitId).Name}  ×{item.Count}").ToArray()
            : lastKnown is { Count: > 0 }
            ? lastKnown.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{_catalog.Unit(item.Key).Name}  ×{item.Value}").ToArray()
            : value is null ? ["유닛 확인 중"] : ["아직 읽힌 유닛이 없어요."];
        if (!InventoryList.Items.Cast<object>().SequenceEqual(rows.Cast<object>()))
        {
            InventoryList.Items.Clear();
            foreach (var row in rows) InventoryList.Items.Add(row);
        }
        InventoryOriginText.Text = value is not null ? "읽은 유닛 · 실제 보유 여부는 게임에서 확인해 주세요."
            : lastKnown is { Count: > 0 } ? "마지막으로 읽은 유닛 · 현재 상태를 다시 확인하고 있어요"
            : "새 유닛 정보를 기다리고 있어요.";
        System.Windows.Automation.AutomationProperties.SetItemStatus(InventoryOriginText,
            value is null ? "유닛을 다시 확인하고 있어요" : "읽은 유닛 · 실제 보유 여부는 확인되지 않았어요");
    }

    private string DiagnosticPresentationLossStatus(IDiagnosticInventoryReference? previous = null) => _coachPaused ? "인식 일시정지" :
        AutoScanCheck.IsChecked != true ? "자동 인식 꺼짐" :
        (previous ?? _diagnosticInventory) is { } value && DateTimeOffset.UtcNow - value.StartedAt >= DiagnosticInventoryObservation.FreshnessBudget
            ? "유닛 정보가 오래되어 다시 확인 중" : "게임 상태가 바뀌어 다시 확인 중";
}
