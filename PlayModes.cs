using System.Text.Json.Serialization;

namespace OrandOverlay;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayMode { Beginner, Normal, Guide, Manual }

public sealed record PlayModeOption(PlayMode Mode, string Name)
{
    public bool IsBetaAvailable => BetaPlayModes.IsAvailable(Mode);
    public string BetaName => (Mode == PlayMode.Manual ? "매뉴얼" : Name) + (IsBetaAvailable ? "" : " · 잠금");
}

public static class PlayModes
{
    public static IReadOnlyList<PlayModeOption> Options { get; } =
    [
        new(PlayMode.Beginner, "초보자"),
        new(PlayMode.Normal, "일반"),
        new(PlayMode.Guide, "대깨"),
        new(PlayMode.Manual, "직접 설정")
    ];

    public static PlayMode Current(AppSettings settings) => settings.Mode is { } mode && Enum.IsDefined(mode)
        ? mode : !settings.AutoStartGoal ? PlayMode.Manual
        : settings.BeginnerCoachEnabled ? PlayMode.Beginner
        : settings.ManualGoalUnitId is not null ? PlayMode.Manual : PlayMode.Normal;

    public static bool AutomaticGoals(PlayMode mode) => mode is PlayMode.Beginner or PlayMode.Normal;

    public static void Normalize(AppSettings settings)
    {
        var mode = Current(settings);
        settings.Mode = mode;
        settings.BeginnerCoachEnabled = mode == PlayMode.Beginner;
        settings.AutoStartGoal = AutomaticGoals(mode);
        settings.AutoRecommendNavigation = true;
        if (mode == PlayMode.Manual)
        {
            settings.ManualGoalUnitId ??= settings.GoalUnitId;
            settings.GoalUnitId = settings.ManualGoalUnitId;
        }
        if (string.IsNullOrWhiteSpace(settings.SecondaryGoalUnitId) ||
            settings.SecondaryGoalUnitId.Equals(settings.ManualGoalUnitId ?? settings.GoalUnitId,
                StringComparison.OrdinalIgnoreCase))
            settings.SecondaryGoalUnitId = null;
    }
}
