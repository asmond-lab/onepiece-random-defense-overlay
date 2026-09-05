namespace OrandOverlay;

public static class NavigationComparisonPresentation
{
    public static string Describe(NavigationIntervalScoringResult? result,
        RouteQuestEvaluation? quest)
    {
        var lines = new List<string>();
        if (quest is not null)
        {
            lines.Add(quest.Description);
            lines.Add(quest.PlannedTopCount > 1
                ? "바운티헌터·로얄로더: 최상위 1기 제한과 목표 묶음 충돌 → 후보 제외"
                : "바운티헌터·로얄로더: 최상위 1기 제한. 초월+제한 동시 보유 목표와는 양립 불가");
        }
        lines.Add("항법 점수는 내부 비교 지표이며 클리어 확률이 아닙니다.");
        if (result is not null)
            lines.AddRange(result.Options.OrderByDescending(option => option.Score.Lower)
                .Select(option => $"{NavigationProfiles.Find(option.OptionId).Name}: " +
                    $"점수 {option.Score.Lower}~{option.Score.Upper} · 신뢰 {option.ConfidenceBp / 100}%"));
        lines.Add("라인 처치 수·상자 기여·스킬 사용 등 미관측 항목은 실제 우열을 확정할 수 없습니다.");
        return string.Join("\n", lines);
    }
}
