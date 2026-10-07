namespace OrandOverlay;

public static class NavigationComparisonPresentation
{
    public static string Describe(NavigationIntervalScoringResult? result,
        RouteQuestEvaluation? quest)
    {
        var lines = new List<string>();
        if (quest is not null)
        {
            lines.Add($"항로개척 보상은 해당 유닛을 조합한 뒤 받을 수 있습니다. 현재 패에 미리 더하지 않습니다. " +
                $"계획한 최상위 유닛 {quest.PlannedTopCount}기 · 계획대로 조합하면 받을 수 있는 흔함선택위습 {quest.FutureCommonWisps}개");
            lines.Add(quest.PlannedTopCount > 1
                ? "바운티헌터·로얄로더는 최상위 유닛을 1기만 둘 수 있어 여러 목표를 함께 만들 수 없습니다."
                : "바운티헌터·로얄로더는 최상위 유닛을 1기만 둘 수 있어 초월과 제한됨을 함께 보유하는 목표에는 맞지 않습니다.");
        }
        lines.Add("아래 점수는 항법을 비교할 때만 사용하며 클리어 확률은 아닙니다.");
        if (result is not null)
            lines.AddRange(result.Options.OrderByDescending(option => option.Score.Lower)
                .Select(option => $"{NavigationProfiles.Find(option.OptionId).Name}: " +
                    $"비교 점수 {option.Score.Lower}~{option.Score.Upper} · 판단에 필요한 정보 {option.ConfidenceBp / 100}%"));
        lines.Add("라인 처치 수·상자 기여·스킬 사용 등 확인하지 못한 정보가 있어 실제로 어느 쪽이 더 좋은지는 단정할 수 없습니다.");
        return string.Join("\n", lines);
    }
}
