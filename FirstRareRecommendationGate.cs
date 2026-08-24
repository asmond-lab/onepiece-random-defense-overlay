namespace OrandOverlay;

/// <summary>
/// 현재 판·목표에서 첫 목표 희귀함을 한 번이라도 확인했는지 기억한다.
/// 희귀함이 전설 제작에 소비돼 사라져도 첫 희귀함 단계로 되돌아가지 않는다.
/// </summary>
public sealed class FirstRareRecommendationGate
{
    public const int QuestDeadlineRound = 8;
    private string? _goalUnitId;
    private bool _observedTargetRare;

    public bool ShouldPrioritize(string goalUnitId, IEnumerable<InventoryEntry> inventory,
        IReadOnlyCollection<string> targetRareUnitIds, int currentRound)
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

        return currentRound < QuestDeadlineRound &&
               targetRareUnitIds.Count > 0 &&
               !_observedTargetRare;
    }

    public void Reset()
    {
        _goalUnitId = null;
        _observedTargetRare = false;
    }
}
