using System.Net.Http;

namespace OrandOverlay;

internal sealed partial class OverlayExecutionContext
{
    private static readonly Lazy<HttpClient> ObservedHttp = new(() => new HttpClient(
        new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) });

    internal ObservedTelemetryClient? CreateObservedTelemetry(DataCatalog catalog, HttpClient? httpClient = null)
    {
        RequireConsent();
        if (!RuntimeEnabled || !Map2320DataBundle.IsCompatible(catalog.MapVersion) || catalog.OfflineBundle is not { } bundle) return null;
        var consent = 1;
        bool CheckConsent()
        {
            var allowed = HasCurrentConsent;
            Volatile.Write(ref consent, allowed ? 1 : 0);
            return allowed;
        }
        var outbox = new ObservedTelemetryOutbox(CheckConsent, _userRoot!, httpClient ?? ObservedHttp.Value);
        return new ObservedTelemetryClient(() => Volatile.Read(ref consent) == 1,
            new ObservedTelemetryMetadata(UpdateService.CurrentBuildVersion, bundle.Fingerprint), outbox);
    }
}
