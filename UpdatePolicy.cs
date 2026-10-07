namespace OrandOverlay;

public static class UpdatePolicy
{
    // ORAND_DEV is diagnostic identity only, never permission to interrupt an active game.
    public static bool ShouldInstallNow(bool liveSessionActive, bool developerMachine) => !liveSessionActive;
    public static bool IsDeveloperMachine =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ORAND_DEV"));
}
