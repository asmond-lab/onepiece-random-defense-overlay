namespace OrandOverlay;

internal static class ObservedTelemetryFooter
{
    internal static string Format(bool backpressured, bool hasError) =>
        backpressured ? "동의한 플레이 기록을 보내는 중이에요. 유닛 확인은 계속돼요."
        : hasError ? "플레이 기록을 보내지 못했어요. 연결되면 다시 보내요."
        : "동의한 플레이 기록을 보내고 있어요.";
}
