namespace OrandOverlay;

/// <summary>
/// 현재 판·목표에서 첫 목표 희귀함을 한 번이라도 확인했는지 기억한다.
/// 희귀함이 전설 제작에 소비돼 사라져도 첫 희귀함 단계로 되돌아가지 않는다.
/// </summary>
public sealed class FirstRareRecommendationGate
{
    public const int QuestDeadlineRound = 8;
    public const int MaximumUnknownRoundInventory = 20;
    private string? _goalUnitId;
    private bool _observedTargetRare;
    private bool _confirmedMatchContext;

    public bool ShouldPrioritize(string goalUnitId, IEnumerable<InventoryEntry> inventory,
        IReadOnlyCollection<string> targetRareUnitIds, int currentRound,
        bool matchActive = false)
    {
        if (!string.Equals(_goalUnitId, goalUnitId, StringComparison.OrdinalIgnoreCase))
        {
            _goalUnitId = goalUnitId;
            _observedTargetRare = false;
        }

        if (!_observedTargetRare)
        {
            var targetRares = targetRareUnitIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _observedTargetRare = inventory.Any(entry =>
                entry.Count > 0 && targetRares.Contains(entry.UnitId));
        }

        // 패스트 유니크 퀘스트 마감과 목표 상위의 첫 희귀함 진행은 별개다.
        // 목표를 대깨하는 중이면 라운드가 지나도 실제 목표 희귀함을 보기 전까지 유지한다.
        // 라운드 판독이 0이어도 이미 실제 판으로 확인됐거나 초반 패 규모를 한 번
        // 통과했다면 같은 판에서 그 사실을 유지한다. 패가 20→21장을 오갈 때
        // 첫 희귀함 카드가 사라졌다 나타나는 현상을 막는다.
        _confirmedMatchContext |= matchActive || currentRound > 0 ||
                                  inventory.Where(entry => entry.Count > 0)
                                      .Sum(entry => entry.Count) <=
                                  MaximumUnknownRoundInventory;
        return _confirmedMatchContext && targetRareUnitIds.Count > 0 &&
               !_observedTargetRare;
    }

    public static bool IsQuestWindow(IEnumerable<InventoryEntry> inventory, int currentRound)
    {
        return currentRound is > 0 and < QuestDeadlineRound;
    }

    public void Reset()
    {
        _goalUnitId = null;
        _observedTargetRare = false;
        _confirmedMatchContext = false;
    }
}
