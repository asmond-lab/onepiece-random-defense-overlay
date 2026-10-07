namespace OrandOverlay;

public static class TelemetryConsentPolicy
{
    public const int CurrentVersion = 4;

    public static bool IsCurrent(AppSettings settings) =>
        settings.TelemetryConsentAccepted && settings.TelemetryConsentVersion == CurrentVersion;

    internal static void ApplyAgreement(AppSettings settings)
    {
        settings.TelemetryConsentAccepted = true;
        settings.TelemetryConsentVersion = CurrentVersion;
        settings.TelemetryEnabled = true;
    }
}
