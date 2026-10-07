using System.Reflection;
using System.Windows;

namespace OrandOverlay;

internal static class TelemetryConsentStartup
{
    internal static void ClearStartupUri(Application application)
    {
        if (application.StartupUri is null) return;
        // WPF's public setter rejects null. Legacy capture callers can still set a URI;
        // clear the pending automatic navigation before a modal pumps the dispatcher.
        var field = typeof(Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(Application).FullName, "_startupUri");
        field.SetValue(application, null);
    }
}
