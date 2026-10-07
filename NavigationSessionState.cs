namespace OrandOverlay;

public sealed class NavigationSessionState
{
    public string? ConfirmedOptionId { get; private set; }
    public void Confirm(string optionId)
    {
        if (!NavigationProfiles.Options.Any(option => option.Id == optionId))
            throw new ArgumentException("Unknown navigation option.", nameof(optionId));
        ConfirmedOptionId = optionId;
    }
    public void Reset() => ConfirmedOptionId = null;
    public string Header(string goalName) => $"{goalName} · " +
        (ConfirmedOptionId is null ? "항법 미선택" :
            $"{NavigationProfiles.Find(ConfirmedOptionId).Name} (선택 확인)");
    public static string Candidate(NavigationIntervalScoringResult? result) =>
        result?.RecommendedOptionId is { } id
            ? $"추천 후보: {NavigationProfiles.Find(id).Name} · 게임 내 선택과 별개"
            : "추천 후보: 계산 중 · 실제 선택 미확인";
}

public enum RouteQuestStatus { Unknown, Inactive, Active, Completed }
