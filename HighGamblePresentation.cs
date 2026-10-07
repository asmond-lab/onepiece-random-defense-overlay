namespace OrandOverlay;

public static class HighGamblePresentation
{
    // Saved map h06D ugol/ulum/ureq; these are not current payment or availability observations.
    public const string TrainingRawcode = "h06D";
    public const int GoldCost = 2000;
    public const int LumberCost = 4;
    public const string RequiredTech = "R00G";
    public static string Describe(HighGambleObservation observed)
    {
        var progress = observed is { IsVerified: true, Active: true, Failures: >= 0 and < 4 }
            ? $"누적 실패 {observed.Failures}회 · 남은 실패 {4 - observed.Failures}회 (확정 시도 횟수 아님)"
            : observed is { IsVerified: true, Active: true, Failures: >= 4 }
                ? "완료 여부 다시 확인 중 · 추가 도박은 보류"
                : observed is { IsVerified: true, Active: false } ? "현재 진행 중이지 않거나 완료됨 · 추가 도박을 권하지 않습니다"
                : "남은 실패 횟수 미확인 · 0회로 취급하지 않습니다";
        return $"고급 도박 실패 퀘스트 · {progress}\n안내된 1회 비용: 금 {GoldCost} · 목재 {LumberCost}. " +
            "도박을 할 수 있는 상태인지, 이미 진행 중인지, 자원을 남겨야 하는지는 아직 모릅니다. 게임에서 지출해도 되는지 확인해 주세요. 성공할 수도 있으므로 퀘스트 완료까지 필요한 횟수와 총비용, 돌려받을 자원은 알 수 없습니다.";
    }
}
