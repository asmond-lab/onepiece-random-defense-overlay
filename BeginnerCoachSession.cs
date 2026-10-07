namespace OrandOverlay;

public sealed class BeginnerCoachSession(DataCatalog catalog)
{
    private readonly BeginnerCoachPlanner _planner = new(catalog);
    private readonly ObservedCraftSession _crafts = new(catalog);
    internal FirstLegendRewardGate FirstLegend { get; } = new(catalog);
    private CoachFrame? _lastCurrent;
    private CoachDecision? _lastCurrentDecision;
    private CoachDecision? _displayed;
    private long _generation = -1;
    private long _revision = -1;
    private string _completion = "";
    private string _change = "";

    public CoachDecision Update(CoachFrame frame)
    {
        if (_displayed is not null && (frame.MatchGeneration < _generation ||
            frame.MatchGeneration == _generation && frame.Revision < _revision)) return _displayed;
        if (_generation != frame.MatchGeneration)
        {
            _lastCurrent = null;
            _lastCurrentDecision = null;
            _displayed = null;
            _completion = "";
            _change = "";
        }
        _generation = frame.MatchGeneration;
        _revision = frame.Revision;
        if (frame.Mode == PlayMode.Guide && frame.GuideNumber == 1 && frame.GuidePlan is { } plan)
        {
            // Presentation refreshes are not new inventory observations.
            if (_lastCurrent is { RecognitionRevision: > 0 } accepted && frame.IsCurrent &&
                frame.RecognitionRevision <= accepted.RecognitionRevision)
                frame = frame with { Inventory = accepted.Inventory, RewardWisps = accepted.RewardWisps };
            frame = frame with { GuidePlan = FirstLegend.Prepare(plan, frame) };
        }
        var decision = _planner.Decide(frame);
        decision = _crafts.Update(frame, decision);
        FirstLegend.Record(frame, decision);
        if (frame.IsCurrent && _lastCurrent is { } previous && _lastCurrentDecision is { } prior)
        {
            if (prior.Kind == CoachActionKind.Navigation &&
                prior.NavigationOptionId is { } option && frame.ConfirmedNavigation == option &&
                previous.ConfirmedNavigation != option)
                _completion = "항법 선택 완료를 사용자 확인으로 기록했습니다.";
            else if (prior.Kind == CoachActionKind.Upgrade &&
                     frame.Signals.GetValueOrDefault("upgrade-level") is { } level &&
                     previous.Signals.GetValueOrDefault("upgrade-level") is { } oldLevel && level > oldLevel)
                _completion = "강화 단계 증가를 확인했습니다.";
            else if (prior.TargetUnitId is { } unit &&
                     frame.Inventory.GetValueOrDefault(unit) > previous.Inventory.GetValueOrDefault(unit))
                _completion = $"{catalog.Unit(unit).Name} 보유 증가 확인";
            else if (prior.Kind == CoachActionKind.Upgrade && prior.ConsumedUnitId is { } common &&
                     frame.Inventory.GetValueOrDefault(common) < previous.Inventory.GetValueOrDefault(common))
                _completion = "강화 대상 흔함의 수량 감소 확인 · 정확한 스택은 별도 확인";
            else if (frame.CompletedStoryStage > previous.CompletedStoryStage)
                _completion = $"스토리 {frame.CompletedStoryStage}단계 완료 확인";
            else if (prior.Kind == CoachActionKind.Reward &&
                     frame.RewardWisps.Values.Sum() < previous.RewardWisps.Values.Sum())
                _completion = "보상 위습 수량 변화 확인";
            else if (prior.Kind == CoachActionKind.Item &&
                     previous.GreenBloodAvailable && !frame.GreenBloodAvailable)
                _completion = "그린블러드 보유 변화 확인";
            else if (prior.Kind == CoachActionKind.Economy && prior.TargetUnitId is { } spent &&
                     frame.Inventory.GetValueOrDefault(spent) < previous.Inventory.GetValueOrDefault(spent))
                _completion = $"{catalog.Unit(spent).Name} 수량 변화 확인";
        }
        if (_displayed is not null && _displayed.Id != decision.Id)
            _change = !frame.IsCurrent ? "인식이 불안정해 실행 안내를 보류합니다."
                : _displayed.Kind == CoachActionKind.Recognition ? "인식이 복구되어 안내를 재개합니다."
                : _lastCurrent?.Round != frame.Round ? "라운드와 다음 관문을 반영했습니다."
                : "새 패·스토리·선택 상태를 반영했습니다.";
        _displayed = decision with { CompletionNotice = _completion, ChangeReason = _change };
        if (frame.IsCurrent && !frame.Paused)
        {
            _lastCurrent = frame;
            _lastCurrentDecision = frame.GuideVisible ? decision : null;
        }
        return _displayed;
    }
}
