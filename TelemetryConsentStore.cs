namespace OrandOverlay;

internal sealed class TelemetryConsentStore(string settingsPath)
{
    public bool IsCurrent => SettingsStore.ReadForConsent(settingsPath) is { } settings &&
        TelemetryConsentPolicy.IsCurrent(settings);

    public bool EnsureConsent(Func<bool?> requestConsent)
    {
        if (IsCurrent) return true;
        if (requestConsent() != true) return false;
        // Only explicit agreement permits normal settings migration/recovery and writing.
        var settings = SettingsStore.Load(settingsPath);
        TelemetryConsentPolicy.ApplyAgreement(settings);
        SettingsStore.SaveEnsuringDirectory(settings, settingsPath);
        return IsCurrent;
    }
}
