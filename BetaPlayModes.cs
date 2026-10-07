namespace OrandOverlay;

public static class BetaPlayModes
{
    public const string LockedReason = "일반 모드 피드백을 반영한 뒤 차례로 공개합니다.";
    public static bool IsAvailable(PlayMode mode) => mode == PlayMode.Normal;
    public static PlayMode Resolve(PlayMode mode) => IsAvailable(mode) ? mode : PlayMode.Normal;

    public static void Normalize(AppSettings settings)
    {
        settings.Mode = Resolve(PlayModes.Current(settings));
        PlayModes.Normalize(settings);
    }
}
